using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VibeChat.Files;
using VibeChat.Messaging;
using VibeChat.Realtime;
using VibeChat.SharedKernel;

namespace VibeChat.Infrastructure;

public sealed class LinkPreviewSettingsResolver(
    ProcessSettingsResolver processSettings,
    VibeChatDbContext dbContext)
{
    public async Task<EffectiveProcessSettings> ResolveProcessAsync(CancellationToken cancellationToken) =>
        await processSettings.ResolveAsync(cancellationToken);

    public async Task<bool> IsEnabledForTenantAsync(TenantId tenantId, CancellationToken cancellationToken)
    {
        var process = await processSettings.ResolveAsync(cancellationToken);
        if (!process.LinkPreviewEnabled)
        {
            return false;
        }

        var row = await dbContext.TenantLinkPreviewSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken);
        return row?.Enabled ?? true;
    }
}

public sealed class LinkPreviewGenerator(
    VibeChatDbContext dbContext,
    LinkPreviewFetcher fetcher,
    LinkPreviewSettingsResolver settings,
    IObjectStorage storage,
    IClock clock,
    ILogger<LinkPreviewGenerator> logger)
{
    public async Task TryProcessMessageCreatedAsync(
        TenantId tenantId,
        ChannelId channelId,
        MessageId messageId,
        string body,
        IChatPublisher publisher,
        CancellationToken cancellationToken)
    {
        if (!await settings.IsEnabledForTenantAsync(tenantId, cancellationToken))
        {
            return;
        }

        var url = LinkPreviewPolicies.ExtractFirstUrl(body);
        if (url is null)
        {
            return;
        }

        var existingLink = await dbContext.MessageLinkPreviews
            .FirstOrDefaultAsync(
                x => x.TenantId == tenantId && x.MessageId == messageId && x.RemovedAt == null,
                cancellationToken);
        if (existingLink is not null)
        {
            return;
        }

        var hash = LinkPreviewPolicies.ComputeUrlHash(url);
        var now = clock.UtcNow;
        var preview = await dbContext.LinkPreviews
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.UrlHash == hash, cancellationToken);

        if (preview is null)
        {
            preview = new LinkPreview
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UrlHash = hash,
                Url = url,
                Status = LinkPreviewStatus.Pending,
                FetchedAt = now,
                ExpiresAt = now.AddDays(LinkPreviewPolicies.CacheTtlDays)
            };
            dbContext.LinkPreviews.Add(preview);
        }
        else if (preview.Status == LinkPreviewStatus.Ready
                 && preview.ExpiresAt > now
                 && !string.IsNullOrWhiteSpace(preview.ImageKey))
        {
            // Only reuse cache when the thumbnail is already stored — text-only Ready
            // entries are retried so a transient image/MinIO failure can recover.
            await AttachAndPublishAsync(tenantId, channelId, messageId, preview, publisher, cancellationToken);
            return;
        }
        else
        {
            preview.Status = LinkPreviewStatus.Pending;
            preview.Url = url;
            preview.FetchedAt = now;
            preview.ExpiresAt = now.AddDays(LinkPreviewPolicies.CacheTtlDays);
            preview.Title = null;
            preview.Description = null;
            preview.SiteName = null;
            preview.ImageKey = null;
            preview.ImageContentType = null;
        }

        var junction = new MessageLinkPreview
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            MessageId = messageId,
            ChannelId = channelId,
            LinkPreviewId = preview.Id,
            CreatedAt = now
        };
        dbContext.MessageLinkPreviews.Add(junction);
        await dbContext.SaveChangesAsync(cancellationToken);

        var process = await settings.ResolveProcessAsync(cancellationToken);
        var result = await fetcher.FetchAsync(url, process.LinkPreviewTimeoutMs, cancellationToken);
        preview.FetchedAt = clock.UtcNow;
        preview.ExpiresAt = preview.FetchedAt.AddDays(LinkPreviewPolicies.CacheTtlDays);
        preview.Status = result.Status;

        if (result.Status == LinkPreviewStatus.Ready)
        {
            preview.Title = string.IsNullOrWhiteSpace(result.Title) ? null : result.Title;
            preview.Description = string.IsNullOrWhiteSpace(result.Description) ? null : result.Description;
            preview.SiteName = string.IsNullOrWhiteSpace(result.SiteName) ? null : result.SiteName;

            if (result.ImageBytes is { Length: > 0 })
            {
                var key = LinkPreviewPolicies.BuildImageKey(tenantId, preview.Id);
                try
                {
                    await using var upload = new MemoryStream(result.ImageBytes, writable: false);
                    await storage.PutObjectAsync(
                        key,
                        upload,
                        result.ImageContentType ?? "application/octet-stream",
                        cancellationToken);
                    preview.ImageKey = key;
                    preview.ImageContentType = result.ImageContentType;
                }
                catch (Exception uploadEx)
                {
                    // Keep the text card even if MinIO is misconfigured (e.g. worker without Minio__*).
                    logger.LogWarning(
                        uploadEx,
                        "Link preview image upload failed for {Url}; publishing text-only card",
                        url);
                }
            }
            else
            {
                logger.LogInformation(
                    "Link preview for {Url} has no image bytes (meta image may be missing or blocked)",
                    url);
            }
        }
        else
        {
            logger.LogWarning(
                "Link preview {Status} for message {MessageId}: {Error}",
                result.Status,
                messageId.Value,
                result.Error);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        if (preview.Status == LinkPreviewStatus.Ready)
        {
            await PublishReadyAsync(tenantId, channelId, messageId, preview, publisher, cancellationToken);
        }
    }

    private async Task AttachAndPublishAsync(
        TenantId tenantId,
        ChannelId channelId,
        MessageId messageId,
        LinkPreview preview,
        IChatPublisher publisher,
        CancellationToken cancellationToken)
    {
        dbContext.MessageLinkPreviews.Add(new MessageLinkPreview
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            MessageId = messageId,
            ChannelId = channelId,
            LinkPreviewId = preview.Id,
            CreatedAt = clock.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        await PublishReadyAsync(tenantId, channelId, messageId, preview, publisher, cancellationToken);
    }

    private static async Task PublishReadyAsync(
        TenantId tenantId,
        ChannelId channelId,
        MessageId messageId,
        LinkPreview preview,
        IChatPublisher publisher,
        CancellationToken cancellationToken)
    {
        var payload = JsonNode.Parse(JsonSerializer.Serialize(new
        {
            tenantId = tenantId.Value,
            channelId = channelId.Value,
            messageId = messageId.Value,
            linkPreviewId = preview.Id,
            url = preview.Url,
            title = preview.Title,
            description = preview.Description,
            siteName = preview.SiteName,
            hasImage = !string.IsNullOrWhiteSpace(preview.ImageKey),
            status = preview.Status.ToString()
        }))!;

        await publisher.PublishAsync(
            new RealtimeMessage("LinkPreviewReady", tenantId, channelId, payload),
            cancellationToken);
    }
}
