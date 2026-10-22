using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Minio;
using StackExchange.Redis;
using VibeChat.Administration;
using VibeChat.AI;
using VibeChat.Infrastructure.Compliance;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Directory;
using VibeChat.Files;
using VibeChat.Identity;
using VibeChat.Integrations;
using VibeChat.Messaging;
using VibeChat.Notifications;
using VibeChat.Realtime;
using NpgsqlTypes;
using VibeChat.Search;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using Role = VibeChat.SharedKernel.Role;

namespace VibeChat.Infrastructure;

public sealed class OutboxProcessor(IServiceScopeFactory scopeFactory, ILogger<OutboxProcessor> logger)
{
    // Hosted OutboxDispatcher and an explicit drain share this singleton.
    // Overlapping batches both see ProcessedAt == null and deliver the same row twice.
    private readonly SemaphoreSlim _batchGate = new(1, 1);

    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        await _batchGate.WaitAsync(cancellationToken);
        try
        {
            return await ProcessBatchCoreAsync(cancellationToken);
        }
        finally
        {
            _batchGate.Release();
        }
    }

    private async Task<int> ProcessBatchCoreAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VibeChatDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IChatPublisher>();
        var searchIndexer = scope.ServiceProvider.GetRequiredService<ISearchIndexer>();
        var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
        var webhooks = scope.ServiceProvider.GetRequiredService<IOutboundWebhookDispatcher>();
        var pluginCallbacks = scope.ServiceProvider.GetRequiredService<IPluginCallbackDispatcher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenant.SetJobRole("outbox");
        await RlsSession.EnsureAppliedAsync(dbContext, tenant, cancellationToken);

        var messages = await dbContext.OutboxMessages.IgnoreQueryFilters()
            .Where(x => x.ProcessedAt == null)
            .OrderBy(x => x.OccurredAt)
            .Take(20)
            .ToArrayAsync(cancellationToken);

        foreach (var outbox in messages)
        {
            try
            {
                tenant.SetTenant(outbox.TenantId);
                await RlsSession.EnsureAppliedAsync(dbContext, tenant, cancellationToken);

                try
                {
                    var automations = scope.ServiceProvider.GetRequiredService<AutomationService>();
                    await automations.ObserveOutboxAsync(outbox.Type, outbox.Payload, cancellationToken);
                }
                catch (Exception automationEx)
                {
                    logger.LogWarning(
                        automationEx,
                        "Automation trigger skipped for outbox {OutboxMessageId}",
                        outbox.Id);
                }

                if (outbox.Type is nameof(MemberRoleChangedEmailEvent) or nameof(MemberInvitedEmailEvent))
                {
                    var to = "";
                    var subject = "";
                    var body = "";
                    Guid? emailTenantId = null;
                    if (outbox.Type == nameof(MemberRoleChangedEmailEvent))
                    {
                        var emailEvent = JsonSerializer.Deserialize<MemberRoleChangedEmailEvent>(outbox.Payload)
                            ?? throw new InvalidOperationException("Invalid MemberRoleChangedEmailEvent payload");
                        to = emailEvent.To;
                        subject = emailEvent.Subject;
                        body = emailEvent.BodyText;
                        emailTenantId = emailEvent.TenantId;
                    }
                    else
                    {
                        var emailEvent = JsonSerializer.Deserialize<MemberInvitedEmailEvent>(outbox.Payload)
                            ?? throw new InvalidOperationException("Invalid MemberInvitedEmailEvent payload");
                        to = emailEvent.To;
                        subject = emailEvent.Subject;
                        body = emailEvent.BodyText;
                        emailTenantId = emailEvent.TenantId;
                    }

                    await emailSender.SendAsync(
                        new EmailMessage(to, subject, body, From: null, TenantId: emailTenantId ?? outbox.TenantId.Value),
                        cancellationToken);

                    outbox.ProcessedAt = now;
                    outbox.Error = null;
                    continue;
                }

                if (outbox.Type is ScheduleEventTypes.ScheduledMessageDue or ScheduleEventTypes.ReminderDue)
                {
                    try
                    {
                        await ScheduleNoticeFanout.DispatchAsync(scope.ServiceProvider, outbox, cancellationToken);
                    }
                    catch (Exception noticeEx)
                    {
                        logger.LogWarning(
                            noticeEx,
                            "Schedule notice failed for outbox {OutboxMessageId}",
                            outbox.Id);
                    }

                    outbox.ProcessedAt = now;
                    outbox.Error = null;
                    continue;
                }

                if (outbox.Type == "files.attachment.ready")
                {
                    await ProcessAttachmentReadyAsync(
                        scope.ServiceProvider,
                        dbContext,
                        publisher,
                        outbox,
                        now,
                        cancellationToken);
                    continue;
                }

                if (outbox.Type == AttachmentScanRunner.RequestedEvent)
                {
                    var completed = await AttachmentScanRunner.TryCompleteAsync(
                        scope.ServiceProvider,
                        dbContext,
                        publisher,
                        outbox,
                        cancellationToken);
                    if (completed)
                    {
                        outbox.ProcessedAt = now;
                        outbox.Error = null;
                    }
                    else
                    {
                        outbox.Attempts++;
                        outbox.Error = "ScanRetry";
                    }

                    continue;
                }

                if (CommitmentPolicies.IsCommitmentEvent(outbox.Type))
                {
                    await VibeChat.Infrastructure.Messaging.CommitmentEventFanout.DispatchAsync(
                        scope.ServiceProvider, publisher, outbox, cancellationToken);
                    outbox.ProcessedAt = now;
                    outbox.Error = null;
                    continue;
                }

                if (TaskPolicies.IsTaskEvent(outbox.Type))
                {
                    await VibeChat.Infrastructure.Messaging.TaskEventFanout.DispatchAsync(
                        scope.ServiceProvider, publisher, outbox, cancellationToken);
                    outbox.ProcessedAt = now;
                    outbox.Error = null;
                    continue;
                }

                if (outbox.Type == DigestEventTypes.Generated)
                {
                    await DigestNoticeFanout.DispatchAsync(scope.ServiceProvider, outbox, cancellationToken);
                    outbox.ProcessedAt = now;
                    outbox.Error = null;
                    continue;
                }

                if (KnowledgePolicies.IsKnowledgeEvent(outbox.Type))
                {
                    await VibeChat.Infrastructure.Messaging.KnowledgeEventFanout.DispatchAsync(
                        publisher, outbox, cancellationToken);
                    try
                    {
                        var knowledgeRoot = JsonNode.Parse(outbox.Payload)?.AsObject();
                        if (knowledgeRoot?["pageId"] is JsonNode pageNode)
                        {
                            var semantic = scope.ServiceProvider.GetRequiredService<ISemanticIndex>();
                            await semantic.SyncPageAsync(outbox.TenantId, pageNode.GetValue<Guid>(), cancellationToken);
                        }
                    }
                    catch (Exception semanticEx)
                    {
                        logger.LogWarning(
                            semanticEx,
                            "Semantic page index failed for outbox {OutboxMessageId}",
                            outbox.Id);
                    }

                    outbox.ProcessedAt = now;
                    outbox.Error = null;
                    continue;
                }

                var payloadNode = JsonNode.Parse(outbox.Payload)
                    ?? throw new InvalidOperationException("Invalid outbox payload JSON");
                var root = payloadNode.AsObject();
                var tenantId = new TenantId(root["tenantId"]?.GetValue<Guid>()
                    ?? throw new InvalidOperationException("Outbox payload missing tenantId"));
                var channelId = new ChannelId(root["channelId"]?.GetValue<Guid>()
                    ?? throw new InvalidOperationException("Outbox payload missing channelId"));
                var eventName = outbox.Type switch
                {
                    nameof(MessageCreatedEvent) => "MessageCreated",
                    nameof(MessageEditedEvent) => "MessageEdited",
                    nameof(MessageMovedEvent) => "MessageMoved",
                    nameof(MessageDeletedEvent) => "MessageDeleted",
                    nameof(ReactionChangedEvent) => "ReactionChanged",
                    nameof(PinChangedEvent) => "PinChanged",
                    nameof(PollChangedEvent) => "PollChanged",
                    _ => outbox.Type
                };

                // Fan-out realtime first — search reindex must not block MessageCreated/edit/delete (B-070).
                // Publish JsonNode so SignalR emits a JSON object the JS client can ingest.
                await publisher.PublishAsync(new RealtimeMessage(eventName, tenantId, channelId, payloadNode), cancellationToken);

                if (outbox.Type is nameof(MessageCreatedEvent) or nameof(MessageEditedEvent) or nameof(MessageDeletedEvent)
                    && root["messageId"] is JsonNode messageIdNode)
                {
                    try
                    {
                        var messageId = new MessageId(messageIdNode.GetValue<Guid>());
                        var body = root["body"]?.GetValue<string>() ?? string.Empty;
                        var isDeleted = outbox.Type == nameof(MessageDeletedEvent);
                        await searchIndexer.IndexMessageAsync(
                            new MessageIndexed(messageId, tenantId, channelId, body, isDeleted, now),
                            cancellationToken);
                    }
                    catch (Exception indexEx)
                    {
                        // Trigger on messaging.messages already maintains search_vector; log and continue.
                        logger.LogWarning(
                            indexEx,
                            "Search reindex failed for outbox {OutboxMessageId}; realtime already published",
                            outbox.Id);
                    }

                    try
                    {
                        var dlp = scope.ServiceProvider.GetRequiredService<DlpService>();
                        var index = true;
                        if (dlp.Enabled)
                        {
                            var channelRow = await dbContext.Channels.AsNoTracking()
                                .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == channelId, cancellationToken);
                            if (channelRow is null)
                            {
                                index = false;
                            }
                            else
                            {
                                var ai = await dlp.EvaluateAsync(
                                    tenantId,
                                    channelRow.WorkspaceId,
                                    channelRow.SpaceId,
                                    channelId.Value,
                                    channelRow.Type.ToString(),
                                    DlpActions.Ai,
                                    null,
                                    messageIdNode.GetValue<Guid>(),
                                    save: false,
                                    cancellationToken);
                                index = ai.Proceed;
                            }
                        }

                        if (index)
                        {
                            var semantic = scope.ServiceProvider.GetRequiredService<ISemanticIndex>();
                            await semantic.SyncMessageAsync(tenantId, new MessageId(messageIdNode.GetValue<Guid>()), cancellationToken);
                        }
                    }
                    catch (Exception semanticEx)
                    {
                        logger.LogWarning(
                            semanticEx,
                            "Semantic index failed for outbox {OutboxMessageId}; FTS and realtime already published",
                            outbox.Id);
                    }
                }

                // B-048 / B-108: best-effort outbound webhook after realtime.
                // HTTP failure must not throw — the outbox row is still marked processed.
                if (outbox.Type is nameof(MessageCreatedEvent)
                    or nameof(MessageEditedEvent)
                    or nameof(MessageDeletedEvent)
                    or nameof(ReactionChangedEvent))
                {
                    await webhooks.TryDispatchAsync(
                        tenantId,
                        eventName,
                        outbox.Id,
                        outbox.Payload,
                        channelId.Value,
                        cancellationToken);
                    try
                    {
                        await pluginCallbacks.TryDispatchEventAsync(
                            tenantId,
                            eventName,
                            outbox.Id,
                            outbox.Payload,
                            channelId.Value,
                            cancellationToken);
                    }
                    catch (Exception pluginEx)
                    {
                        logger.LogWarning(
                            pluginEx,
                            "Plugin callback failed for outbox {OutboxMessageId}; realtime already published",
                            outbox.Id);
                    }
                }

                // B-091: link preview after realtime — never on SendMessage hot path.
                if (outbox.Type == nameof(MessageCreatedEvent)
                    && root["messageId"] is JsonNode linkPreviewMessageIdNode)
                {
                    try
                    {
                        var messageId = new MessageId(linkPreviewMessageIdNode.GetValue<Guid>());
                        var body = root["body"]?.GetValue<string>() ?? string.Empty;
                        var generator = scope.ServiceProvider.GetRequiredService<LinkPreviewGenerator>();
                        await generator.TryProcessMessageCreatedAsync(
                            tenantId,
                            channelId,
                            messageId,
                            body,
                            publisher,
                            cancellationToken);
                    }
                    catch (Exception linkEx)
                    {
                        logger.LogWarning(
                            linkEx,
                            "Link preview failed for outbox {OutboxMessageId}; realtime already published",
                            outbox.Id);
                    }
                }

                // B-095: web push after realtime — never on SendMessage hot path.
                if (outbox.Type == nameof(MessageCreatedEvent))
                {
                    try
                    {
                        var push = scope.ServiceProvider.GetRequiredService<PushDispatcher>();
                        await push.TryDispatchMessageCreatedAsync(root, cancellationToken);
                    }
                    catch (Exception pushEx)
                    {
                        logger.LogWarning(
                            pushEx,
                            "Web push failed for outbox {OutboxMessageId}; realtime already published",
                            outbox.Id);
                    }
                }

                outbox.ProcessedAt = now;
                outbox.Error = null;
            }
            catch (Exception ex)
            {
                outbox.Attempts++;
                outbox.Error = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
                logger.LogWarning(ex, "Outbox message {OutboxMessageId} processing failed", outbox.Id);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await RlsSession.CommitAsync(dbContext, cancellationToken);
        if (!SiemTestDoubles.PauseBackgroundDrain)
        {
            try
            {
                using var siemScope = scopeFactory.CreateScope();
                var siem = siemScope.ServiceProvider.GetRequiredService<SiemExportService>();
                await siem.DrainAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "SIEM drain failed after the outbox batch was committed");
            }
        }

        return messages.Length;
    }

    private static async Task ProcessAttachmentReadyAsync(
        IServiceProvider services,
        VibeChatDbContext dbContext,
        IChatPublisher publisher,
        OutboxMessage outbox,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var payloadNode = JsonNode.Parse(outbox.Payload)
            ?? throw new InvalidOperationException("Invalid outbox payload JSON");
        var root = payloadNode.AsObject();
        var tenantId = new TenantId(root["tenantId"]?.GetValue<Guid>()
            ?? throw new InvalidOperationException("Outbox payload missing tenantId"));
        var channelId = new ChannelId(root["channelId"]?.GetValue<Guid>()
            ?? throw new InvalidOperationException("Outbox payload missing channelId"));
        var attachmentId = root["attachmentId"]?.GetValue<Guid>()
            ?? throw new InvalidOperationException("Outbox payload missing attachmentId");

        // Fan-out legacy ready event for any listeners.
        await publisher.PublishAsync(
            new RealtimeMessage("files.attachment.ready", tenantId, channelId, payloadNode),
            cancellationToken);

        var attachment = await dbContext.Attachments.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                x => x.Id == attachmentId && x.TenantId == tenantId && x.ChannelId == channelId,
                cancellationToken);

        if (attachment is not null
            && AttachmentPolicies.IsThumbnailEligible(attachment.ContentType)
            && attachment.ThumbnailStatus is ThumbnailStatus.Pending or null)
        {
            var generator = services.GetRequiredService<AttachmentThumbnailGenerator>();
            await generator.TryGenerateAsync(attachment, cancellationToken);

            var thumbPayload = JsonNode.Parse(JsonSerializer.Serialize(new
            {
                tenantId = tenantId.Value,
                channelId = channelId.Value,
                attachmentId = attachment.Id,
                thumbnailStatus = attachment.ThumbnailStatus?.ToString(),
                width = attachment.Width,
                height = attachment.Height,
                pageCount = attachment.PageCount,
                thumbnailKey = attachment.ThumbnailKey
            }))!;
            await publisher.PublishAsync(
                new RealtimeMessage("AttachmentThumbnailReady", tenantId, channelId, thumbPayload),
                cancellationToken);
        }

        outbox.ProcessedAt = now;
        outbox.Error = null;
    }
}

public sealed class OutboxDispatcher(OutboxProcessor processor, ILogger<OutboxDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await processor.ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Outbox dispatcher loop failed");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(750), stoppingToken);
        }
    }
}
