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

public sealed class SummarizeChannelFeature(
    VibeChatDbContext dbContext,
    OpenRouterAiProvider openRouter,
    AiSettingsResolver aiSettings,
    IClock clock) : ISummarizeChannelFeature
{
    public async Task<SummarizeChannelResult> SummarizeAsync(TenantId tenantId, WorkspaceId workspaceId, ChannelId channelId, CancellationToken cancellationToken)
    {
        var runtime = await aiSettings.ResolveAsync(tenantId, workspaceId, cancellationToken);
        // D-06 / ADR-012: process kill switch evaluated per call.
        if (!runtime.ProcessEnabled || !runtime.WorkspaceEnabled)
        {
            return new SummarizeChannelResult(false, "AI is disabled.", "AiDisabled");
        }

        if (string.Equals(runtime.ApiKeySource, "unavailable", StringComparison.Ordinal))
        {
            return new SummarizeChannelResult(false, "AI credential unavailable.", "ProviderError");
        }

        var activeProvider = WorkspaceAiRuntime.SelectProvider(openRouter, runtime);
        if (activeProvider is null)
        {
            return new SummarizeChannelResult(false, "AI is disabled.", "AiDisabled");
        }

        var recent = await dbContext.Messages.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.ConversationId == channelId && x.DeletedAt == null)
            .OrderByDescending(x => x.Sequence)
            .Take(20)
            .OrderBy(x => x.Sequence)
            .Select(x => new { x.Sequence, x.Body })
            .ToArrayAsync(cancellationToken);

        var prompt = string.Join('\n', recent.Select(x => $"#{x.Sequence}: {x.Body}"));
        var response = await activeProvider.CompleteAsync(
            new AiCompletionRequest(
                "Summarize recent channel messages without exposing sensitive details.",
                prompt,
                runtime.ApiKey,
                runtime.OpenRouterBaseUrl),
            cancellationToken);

        if (string.Equals(activeProvider.Name, "OpenRouter", StringComparison.OrdinalIgnoreCase)
            && response.Text.Contains("provider is unavailable", StringComparison.OrdinalIgnoreCase))
        {
            return new SummarizeChannelResult(false, response.Text, "ProviderError");
        }

        dbContext.AiUsageRecords.Add(new AiUsageRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            WorkspaceId = workspaceId,
            Provider = activeProvider.Name,
            PromptTokens = response.PromptTokens,
            CompletionTokens = response.CompletionTokens,
            CostUsd = 0m,
            LatencyMs = response.LatencyMs,
            CreatedAt = clock.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return new SummarizeChannelResult(true, response.Text);
    }
}

internal static class WorkspaceAiRuntime
{
    public static IAiCompletionProvider? SelectProvider(OpenRouterAiProvider openRouter, EffectiveAiRuntime runtime)
    {
        if (string.Equals(runtime.Provider, "OpenRouter", StringComparison.OrdinalIgnoreCase))
        {
            return SecretMasking.IsConfigured(runtime.ApiKey) ? openRouter : null;
        }

        return new MockAiProvider();
    }
}

public sealed class SuggestChannelReplyFeature(
    VibeChatDbContext dbContext,
    OpenRouterAiProvider openRouter,
    AiSettingsResolver aiSettings,
    IClock clock) : ISuggestChannelReplyFeature
{
    public async Task<SuggestChannelReplyResult> SuggestAsync(TenantId tenantId, WorkspaceId workspaceId, ChannelId channelId, CancellationToken cancellationToken)
    {
        var runtime = await aiSettings.ResolveAsync(tenantId, workspaceId, cancellationToken);
        if (!runtime.ProcessEnabled || !runtime.WorkspaceEnabled)
        {
            return new SuggestChannelReplyResult(false, "AI is disabled.", "AiDisabled");
        }

        if (string.Equals(runtime.ApiKeySource, "unavailable", StringComparison.Ordinal))
        {
            return new SuggestChannelReplyResult(false, "AI credential unavailable.", "ProviderError");
        }

        var activeProvider = WorkspaceAiRuntime.SelectProvider(openRouter, runtime);
        if (activeProvider is null)
        {
            return new SuggestChannelReplyResult(false, "AI is disabled.", "AiDisabled");
        }

        var recent = await dbContext.Messages.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.ConversationId == channelId && x.DeletedAt == null)
            .OrderByDescending(x => x.Sequence)
            .Take(20)
            .OrderBy(x => x.Sequence)
            .Select(x => new { x.Sequence, x.Body })
            .ToArrayAsync(cancellationToken);

        var prompt = string.Join('\n', recent.Select(x => $"#{x.Sequence}: {x.Body}"));
        var response = await activeProvider.CompleteAsync(
            new AiCompletionRequest(
                "Suggest one short, professional reply to the recent channel messages without exposing sensitive details.",
                prompt,
                runtime.ApiKey,
                runtime.OpenRouterBaseUrl),
            cancellationToken);

        if (string.Equals(activeProvider.Name, "OpenRouter", StringComparison.OrdinalIgnoreCase)
            && response.Text.Contains("provider is unavailable", StringComparison.OrdinalIgnoreCase))
        {
            return new SuggestChannelReplyResult(false, response.Text, "ProviderError");
        }

        dbContext.AiUsageRecords.Add(new AiUsageRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            WorkspaceId = workspaceId,
            Provider = activeProvider.Name,
            PromptTokens = response.PromptTokens,
            CompletionTokens = response.CompletionTokens,
            CostUsd = 0m,
            LatencyMs = response.LatencyMs,
            CreatedAt = clock.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return new SuggestChannelReplyResult(true, response.Text);
    }
}

public sealed class TranscribeAttachmentFeature(
    VibeChatDbContext dbContext,
    OpenRouterAiProvider openRouter,
    AiSettingsResolver aiSettings,
    IClock clock) : ITranscribeAttachmentFeature
{
    public async Task<TranscribeAttachmentResult> TranscribeAsync(
        TenantId tenantId,
        WorkspaceId workspaceId,
        ChannelId channelId,
        MessageId messageId,
        Guid attachmentId,
        CancellationToken cancellationToken)
    {
        var runtime = await aiSettings.ResolveAsync(tenantId, workspaceId, cancellationToken);
        if (!runtime.ProcessEnabled || !runtime.WorkspaceEnabled)
        {
            return new TranscribeAttachmentResult(false, string.Empty, null, null, "AiDisabled");
        }

        if (string.Equals(runtime.ApiKeySource, "unavailable", StringComparison.Ordinal))
        {
            return new TranscribeAttachmentResult(false, string.Empty, null, null, "ProviderError");
        }

        var activeProvider = WorkspaceAiRuntime.SelectProvider(openRouter, runtime);
        if (activeProvider is null)
        {
            return new TranscribeAttachmentResult(false, string.Empty, null, null, "AiDisabled");
        }

        var attachment = await dbContext.Attachments.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == attachmentId
                    && x.TenantId == tenantId
                    && x.ChannelId == channelId
                    && x.MessageId == messageId
                    && x.Status == AttachmentStatus.Ready,
                cancellationToken);

        if (attachment is null)
        {
            return new TranscribeAttachmentResult(false, string.Empty, null, null, "NotFound");
        }

        if (attachment.Kind != AttachmentKind.Audio)
        {
            return new TranscribeAttachmentResult(false, string.Empty, null, null, "NotAudio");
        }

        var durationSec = attachment.DurationMs.HasValue
            ? Math.Round(attachment.DurationMs.Value / 1000.0, 1)
            : 0;
        var prompt = $"Audio attachment {attachment.FileName}; duration {durationSec}s; content-type {attachment.ContentType}.";
        var response = await activeProvider.CompleteAsync(
            new AiCompletionRequest(
                "Transcribe the described audio attachment without exposing sensitive details. Return plain text only.",
                prompt,
                runtime.ApiKey,
                runtime.OpenRouterBaseUrl),
            cancellationToken);

        if (string.Equals(activeProvider.Name, "OpenRouter", StringComparison.OrdinalIgnoreCase)
            && response.Text.Contains("provider is unavailable", StringComparison.OrdinalIgnoreCase))
        {
            return new TranscribeAttachmentResult(false, string.Empty, null, null, "ProviderError");
        }

        dbContext.AiUsageRecords.Add(new AiUsageRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            WorkspaceId = workspaceId,
            Provider = activeProvider.Name,
            PromptTokens = response.PromptTokens,
            CompletionTokens = response.CompletionTokens,
            CostUsd = 0m,
            LatencyMs = response.LatencyMs,
            CreatedAt = clock.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return new TranscribeAttachmentResult(true, response.Text, "und", activeProvider.Name);
    }
}
