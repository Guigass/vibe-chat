using System.Text.Json;
using Microsoft.Extensions.Options;
using VibeChat.Api;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Files;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;

namespace VibeChat.Api.Endpoints;

internal static class FilesEndpointHelpers
{
    internal static async Task<IResult> CompleteOwnedUploadAsync(
        Channel channel,
        Attachment attachment,
        UserId actorId,
        VibeChatDbContext db,
        IObjectStorage storage,
        FilesSettingsResolver filesSettings,
        IOutboxWriter outbox,
        IOptions<MalwareScanOptions> scanOptions,
        IMalwareScanner scanner,
        IAuditWriter audit,
        IClock clock,
        StorageLifecycleService lifecycle,
        CancellationToken ct)
    {
        if (attachment.Status == AttachmentStatus.Ready
            || attachment.ScanStatus is AttachmentScanStatus.PendingScan
                or AttachmentScanStatus.Quarantined
                or AttachmentScanStatus.ScanFailed)
        {
            return Results.Ok(ToAttachmentResponse(attachment));
        }

        var stat = await storage.StatObjectAsync(attachment.StorageKey, ct);
        if (stat is null || stat.SizeBytes <= 0)
        {
            attachment.Status = AttachmentStatus.Failed;
            await lifecycle.ReleaseUploadAsync(channel.TenantId, attachment.Id, ct);
            await db.SaveChangesAsync(ct);
            return Results.BadRequest(new { error = "Uploaded object was not found in storage." });
        }

        var files = await filesSettings.ResolveAsync(channel.TenantId, ct);
        var maxSize = attachment.Kind switch
        {
            AttachmentKind.Audio => files.AudioMaxSizeBytes,
            AttachmentKind.Video => files.VideoMaxSizeBytes,
            _ => files.MaxSizeBytes
        };
        if (stat.SizeBytes > maxSize || stat.SizeBytes > attachment.SizeBytes)
        {
            attachment.Status = AttachmentStatus.Failed;
            await lifecycle.ReleaseUploadAsync(channel.TenantId, attachment.Id, ct);
            await db.SaveChangesAsync(ct);
            return Results.BadRequest(new { error = "Uploaded object exceeds declared or allowed size." });
        }

        attachment.SizeBytes = stat.SizeBytes;
        attachment.ChecksumSha256 = string.IsNullOrWhiteSpace(stat.ETag) ? null : stat.ETag.Trim('"');
        var malware = scanOptions.Value;
        if (!malware.Enabled)
        {
            if (scanner is not NullMalwareScanner)
            {
                attachment.Status = AttachmentStatus.Failed;
                attachment.ScanStatus = AttachmentScanStatus.ScanFailed;
                attachment.ScanReason = "ScannerMissing";
                await lifecycle.ReleaseUploadAsync(channel.TenantId, attachment.Id, ct);
                await db.SaveChangesAsync(ct);
                return Results.Json(new { error = "ScannerMissing" }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            attachment.ScanStatus = AttachmentScanStatus.NotRequired;
            attachment.ScanEngine = NullMalwareScanner.EngineName;
            attachment.ScanSignatureVersion = scanner.SignatureVersion;
            attachment.Status = AttachmentStatus.Ready;
            attachment.ReadyAt = clock.UtcNow;
            if (AttachmentPolicies.IsThumbnailEligible(attachment.ContentType))
            {
                attachment.ThumbnailStatus = ThumbnailStatus.Pending;
            }
        }
        else if (scanner is NullMalwareScanner)
        {
            attachment.Status = AttachmentStatus.PendingUpload;
            attachment.ScanStatus = AttachmentScanStatus.ScanFailed;
            attachment.ScanReason = "ScannerMissing";
            attachment.ScannedAt = clock.UtcNow;
            await lifecycle.ReleaseUploadAsync(channel.TenantId, attachment.Id, ct);
            await db.SaveChangesAsync(ct);
            return Results.Json(new { error = "ScannerMissing" }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        else
        {
            attachment.Status = AttachmentStatus.PendingUpload;
            attachment.ScanStatus = AttachmentScanStatus.PendingScan;
            attachment.ScanEngine = null;
            attachment.ScanReason = null;
            attachment.ScanRequestedAt = clock.UtcNow;
            attachment.ReadyAt = null;
            VibeChatMetrics.AttachmentScanQueue.Add(1);
            outbox.Add(new OutboxMessage
            {
                TenantId = channel.TenantId,
                Type = AttachmentScanRunner.RequestedEvent,
                Payload = JsonSerializer.Serialize(new
                {
                    tenantId = channel.TenantId.Value,
                    channelId = channel.Id.Value,
                    attachmentId = attachment.Id
                })
            });
            audit.Add(new AuditEvent
            {
                TenantId = channel.TenantId,
                ActorUserId = actorId,
                Action = AuditActions.AttachmentScan,
                EntityType = "Attachment",
                EntityId = attachment.Id.ToString(),
                MetadataJson = JsonSerializer.Serialize(new { stage = "requested" })
            });
            await lifecycle.CommitUploadAsync(channel.TenantId, attachment.Id, clean: false, ct);
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToAttachmentResponse(attachment));
        }

        await lifecycle.CommitUploadAsync(channel.TenantId, attachment.Id, clean: true, ct);
        outbox.Add(new OutboxMessage
        {
            TenantId = channel.TenantId,
            Type = "files.attachment.ready",
            Payload = JsonSerializer.Serialize(new
            {
                tenantId = channel.TenantId.Value,
                channelId = channel.Id.Value,
                attachmentId = attachment.Id,
                fileName = attachment.FileName,
                contentType = attachment.ContentType,
                sizeBytes = attachment.SizeBytes,
                readyAt = attachment.ReadyAt,
                thumbnailStatus = attachment.ThumbnailStatus?.ToString()
            })
        });
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToAttachmentResponse(attachment));
    }

    internal static AttachmentResponse ToAttachmentResponse(Attachment attachment) =>
        new(
            attachment.Id,
            attachment.FileName,
            attachment.ContentType,
            attachment.SizeBytes,
            attachment.Status.ToString(),
            attachment.Kind.ToString(),
            attachment.DurationMs,
            attachment.Waveform,
            attachment.ThumbnailStatus?.ToString(),
            attachment.Width,
            attachment.Height,
            attachment.PageCount,
            attachment.ScanStatus?.ToString(),
            attachment.ScanReason);
}
