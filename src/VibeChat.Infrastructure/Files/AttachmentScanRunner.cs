using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Files;
using VibeChat.Realtime;
using VibeChat.SharedKernel;

namespace VibeChat.Infrastructure;

/// <summary>
/// Applies one <c>attachment.scan.requested</c> outbox row. Returns false when the
/// same row should be retried (timeout / dead scanner under the attempt budget).
/// </summary>
public static class AttachmentScanRunner
{
    public const string RequestedEvent = "attachment.scan.requested";

    public static async Task<bool> TryCompleteAsync(
        IServiceProvider services,
        VibeChatDbContext db,
        IChatPublisher publisher,
        OutboxMessage outbox,
        CancellationToken cancellationToken)
    {
        var options = services.GetRequiredService<IOptions<MalwareScanOptions>>().Value;
        var scanner = services.GetRequiredService<IMalwareScanner>();
        var storage = services.GetRequiredService<IObjectStorage>();
        var clock = services.GetRequiredService<IClock>();
        var audit = services.GetRequiredService<IAuditWriter>();
        var root = JsonNode.Parse(outbox.Payload)?.AsObject()
            ?? throw new InvalidOperationException("Invalid scan outbox payload.");
        var tenantId = new TenantId(root["tenantId"]?.GetValue<Guid>()
            ?? throw new InvalidOperationException("Scan payload missing tenantId"));
        var channelId = new ChannelId(root["channelId"]?.GetValue<Guid>()
            ?? throw new InvalidOperationException("Scan payload missing channelId"));
        var attachmentId = root["attachmentId"]?.GetValue<Guid>()
            ?? throw new InvalidOperationException("Scan payload missing attachmentId");

        var attachment = await db.Attachments.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                x => x.Id == attachmentId && x.TenantId == tenantId && x.ChannelId == channelId,
                cancellationToken);
        if (attachment is null || attachment.ScanStatus is AttachmentScanStatus.Clean or AttachmentScanStatus.Quarantined)
        {
            return true;
        }

        var maxAttempts = Math.Max(1, options.MaxAttempts);
        if (attachment.ScanStatus == AttachmentScanStatus.ScanFailed && attachment.ScanAttempts >= maxAttempts)
        {
            return true;
        }

        if (!options.Enabled || scanner is NullMalwareScanner)
        {
            await FinishAsync(attachment, new MalwareScanResult(AttachmentScanStatus.ScanFailed, scanner.Engine, scanner.SignatureVersion, "ScannerMissing"), options, clock, db, audit, publisher, cancellationToken);
            return true;
        }

        await using var stream = await storage.GetObjectAsync(attachment.StorageKey, cancellationToken);
        if (stream is null)
        {
            await FinishAsync(attachment, new MalwareScanResult(AttachmentScanStatus.ScanFailed, scanner.Engine, scanner.SignatureVersion, "ObjectMissing"), options, clock, db, audit, publisher, cancellationToken);
            return true;
        }

        var result = await scanner.ScanAsync(stream, attachment.ContentType, cancellationToken);
        attachment.ScanAttempts++;
        attachment.ScanEngine = Trim(result.Engine, 32);
        attachment.ScanSignatureVersion = Trim(result.SignatureVersion, 32);
        if (AttachmentScanPolicies.ShouldRetry(result, attachment.ScanAttempts, maxAttempts))
        {
            attachment.ScanStatus = AttachmentScanStatus.PendingScan;
            attachment.ScanReason = Trim(result.Reason, 64);
            return false;
        }

        await FinishAsync(attachment, result, options, clock, db, audit, publisher, cancellationToken);
        return true;
    }

    private static async Task FinishAsync(
        Attachment attachment,
        MalwareScanResult result,
        MalwareScanOptions options,
        IClock clock,
        VibeChatDbContext db,
        IAuditWriter audit,
        IChatPublisher publisher,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        attachment.ScanEngine = Trim(result.Engine, 32);
        attachment.ScanSignatureVersion = Trim(result.SignatureVersion, 32);
        attachment.ScanReason = Trim(result.Reason, 64);
        attachment.ScannedAt = now;
        var metric = "failed";

        if (result.Status == AttachmentScanStatus.Clean)
        {
            attachment.ScanStatus = AttachmentScanStatus.Clean;
            attachment.ScanReason = null;
            attachment.Status = AttachmentStatus.Ready;
            attachment.ReadyAt = now;
            if (AttachmentPolicies.IsThumbnailEligible(attachment.ContentType))
            {
                attachment.ThumbnailStatus = ThumbnailStatus.Pending;
            }

            db.OutboxMessages.Add(new OutboxMessage
            {
                TenantId = attachment.TenantId,
                Type = "files.attachment.ready",
                Payload = JsonSerializer.Serialize(new
                {
                    tenantId = attachment.TenantId.Value,
                    channelId = attachment.ChannelId.Value,
                    attachmentId = attachment.Id,
                    fileName = attachment.FileName,
                    contentType = attachment.ContentType,
                    sizeBytes = attachment.SizeBytes,
                    readyAt = attachment.ReadyAt,
                    thumbnailStatus = attachment.ThumbnailStatus?.ToString()
                })
            });
            metric = "clean";
        }
        else if (result.Status == AttachmentScanStatus.Quarantined)
        {
            attachment.ScanStatus = AttachmentScanStatus.Quarantined;
            attachment.Status = AttachmentStatus.PendingUpload;
            metric = "quarantined";
        }
        else
        {
            attachment.ScanStatus = AttachmentScanStatus.ScanFailed;
            if (!options.FailClosed)
            {
                attachment.Status = AttachmentStatus.Ready;
                attachment.ReadyAt ??= now;
            }
        }

        VibeChatMetrics.AttachmentScans.Add(1, new KeyValuePair<string, object?>("result", metric));
        VibeChatMetrics.AttachmentScanQueue.Add(-1);
        audit.Add(new AuditEvent
        {
            TenantId = attachment.TenantId,
            Action = AuditActions.AttachmentScan,
            EntityType = "Attachment",
            EntityId = attachment.Id.ToString(),
            MetadataJson = JsonSerializer.Serialize(new
            {
                result = attachment.ScanStatus.ToString(),
                engine = attachment.ScanEngine,
                signatureVersion = attachment.ScanSignatureVersion,
                reason = attachment.ScanReason
            })
        });

        var payload = JsonNode.Parse(JsonSerializer.Serialize(new
        {
            tenantId = attachment.TenantId.Value,
            channelId = attachment.ChannelId.Value,
            attachmentId = attachment.Id,
            status = attachment.Status.ToString(),
            scanStatus = attachment.ScanStatus?.ToString(),
            scanReason = attachment.ScanReason
        }))!;
        await publisher.PublishAsync(
            new RealtimeMessage("AttachmentScanCompleted", attachment.TenantId, attachment.ChannelId, payload),
            cancellationToken);
    }

    private static string? Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
