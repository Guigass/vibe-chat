using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VibeChat.Administration;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Messaging;
using VibeChat.Notifications;
using VibeChat.SharedKernel;
using PushSubscriptionEntity = VibeChat.Notifications.PushSubscription;
using WebPushSubscription = Lib.Net.Http.WebPush.PushSubscription;

namespace VibeChat.Infrastructure;

public static class PushOptions
{
    public const string HttpClientName = "WebPush";
}

public readonly record struct VapidKeyPair(string PublicKey, string PrivateKey);

public static class VapidKeyGenerator
{
    public static VapidKeyPair Create()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = ecdsa.ExportParameters(includePrivateParameters: true);
        var publicKey = new byte[65];
        publicKey[0] = 0x04;
        Buffer.BlockCopy(parameters.Q.X!, 0, publicKey, 1, 32);
        Buffer.BlockCopy(parameters.Q.Y!, 0, publicKey, 33, 32);
        return new VapidKeyPair(ToBase64Url(publicKey), ToBase64Url(parameters.D!));
    }

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>Captures deliveries in tests when Push:UseRecordingSender=true.</summary>
public sealed class RecordingPushSender : IPushSender
{
    public string Name => "Recording";
    public bool IsEnabled => true;

    public ConcurrentBag<PushDeliveryRequest> Attempts { get; } = [];
    public ConcurrentDictionary<string, PushSendStatus> NextStatusByEndpoint { get; } = new(StringComparer.Ordinal);

    public Task<PushSendResult> SendAsync(PushDeliveryRequest request, CancellationToken cancellationToken)
    {
        Attempts.Add(request);
        var status = NextStatusByEndpoint.TryRemove(request.Endpoint, out var next)
            ? next
            : PushSendStatus.Delivered;
        return Task.FromResult(new PushSendResult(status));
    }

    public void Reset()
    {
        while (Attempts.TryTake(out _))
        {
        }

        NextStatusByEndpoint.Clear();
    }
}

public sealed class WebPushSender(
    IHttpClientFactory httpClientFactory,
    ProcessSettingsResolver processSettings,
    ILogger<WebPushSender> logger) : IPushSender
{
    public string Name => "WebPush";
    public bool IsEnabled => true;

    public async Task<PushSendResult> SendAsync(PushDeliveryRequest request, CancellationToken cancellationToken)
    {
        var process = await processSettings.ResolveAsync(cancellationToken);
        if (!process.PushEnabled
            || !SecretMasking.IsConfigured(process.VapidPublicKey)
            || !SecretMasking.IsConfigured(process.VapidPrivateKey))
        {
            return new PushSendResult(PushSendStatus.Delivered);
        }

        var publicKey = process.VapidPublicKey!.Trim();
        var privateKey = process.VapidPrivateKey!.Trim();
        var subject = string.IsNullOrWhiteSpace(process.VapidSubject)
            ? ProcessSettingsDefaults.VapidSubject
            : process.VapidSubject;

        var client = new PushServiceClient(httpClientFactory.CreateClient(PushOptions.HttpClientName));
        var vapid = new VapidAuthentication(publicKey, privateKey) { Subject = subject };
        var subscription = new WebPushSubscription
        {
            Endpoint = request.Endpoint
        };
        subscription.SetKey(PushEncryptionKeyName.P256DH, request.P256dh);
        subscription.SetKey(PushEncryptionKeyName.Auth, request.Auth);

        try
        {
            await client.RequestPushMessageDeliveryAsync(
                subscription,
                new PushMessage(request.PayloadJson) { Urgency = PushMessageUrgency.Normal },
                vapid,
                cancellationToken);
            return new PushSendResult(PushSendStatus.Delivered);
        }
        catch (PushServiceClientException ex) when (
            ex.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
        {
            return new PushSendResult(PushSendStatus.Gone);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Web push delivery failed");
            return new PushSendResult(PushSendStatus.Failed);
        }
    }
}

public sealed class PushDispatcher(
    VibeChatDbContext dbContext,
    IChannelMembershipReader memberships,
    IPushSender pushSender,
    IClock clock,
    ProcessSettingsResolver processSettings,
    ILogger<PushDispatcher> logger)
{
    public async Task TryDispatchMessageCreatedAsync(JsonObject root, CancellationToken cancellationToken)
    {
        var process = await processSettings.ResolveAsync(cancellationToken);
        if (!process.PushEnabled || !pushSender.IsEnabled)
        {
            return;
        }

        var tenantId = new TenantId(root["tenantId"]?.GetValue<Guid>()
            ?? throw new InvalidOperationException("Outbox payload missing tenantId"));
        var channelId = new ChannelId(root["channelId"]?.GetValue<Guid>()
            ?? throw new InvalidOperationException("Outbox payload missing channelId"));
        var messageId = root["messageId"]?.GetValue<Guid>() ?? Guid.Empty;
        var authorId = root["authorId"]?.GetValue<Guid>() ?? Guid.Empty;
        var sequence = root["sequence"]?.GetValue<long>() ?? 0;
        var authorName = root["authorName"]?.GetValue<string>() ?? string.Empty;
        var body = root["body"]?.GetValue<string>() ?? string.Empty;
        var mentioned = ParseMentionedUserIds(root["mentionedUserIds"]);

        var channel = await dbContext.Channels.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == channelId && x.TenantId == tenantId, cancellationToken);
        if (channel is null || messageId == Guid.Empty || authorId == Guid.Empty)
        {
            return;
        }

        var isDirect = channel.Type == ChannelType.Direct;
        var candidateIds = await LoadCandidateUserIdsAsync(tenantId, channel, cancellationToken);
        var mentionedSet = mentioned.ToHashSet();
        var recipients = new List<UserId>();
        foreach (var userId in candidateIds)
        {
            if (!PushDispatchPolicies.ShouldNotify(isDirect, mentionedSet, userId.Value, authorId))
            {
                continue;
            }

            if (!await memberships.CanAccessAsync(tenantId, channelId, userId, cancellationToken))
            {
                continue;
            }

            recipients.Add(userId);
        }

        if (recipients.Count == 0)
        {
            return;
        }

        var prefs = await dbContext.NotificationPreferences.AsNoTracking()
            .Where(x => recipients.Contains(x.UserId))
            .ToDictionaryAsync(x => x.UserId, cancellationToken);
        var cursors = await dbContext.ReadCursors.AsNoTracking()
            .Where(x => x.ChannelId == channelId && recipients.Contains(x.UserId))
            .ToDictionaryAsync(x => x.UserId, cancellationToken);

        var eligible = new List<UserId>();
        foreach (var userId in recipients)
        {
            prefs.TryGetValue(userId, out var pref);
            if (!PushDispatchPolicies.IsPushEnabled(pref?.PushEnabled))
            {
                continue;
            }

            cursors.TryGetValue(userId, out var cursor);
            if (PushDispatchPolicies.IsSuppressedByCursor(cursor?.LastReadSequence, sequence))
            {
                continue;
            }

            eligible.Add(userId);
        }

        if (eligible.Count == 0)
        {
            return;
        }

        var subscriptions = await dbContext.PushSubscriptions
            .Where(x => eligible.Contains(x.UserId))
            .OrderBy(x => x.CreatedAt)
            .Take(PushDispatchPolicies.MaxSubscriptionsPerMessage)
            .ToListAsync(cancellationToken);
        if (subscriptions.Count == 0)
        {
            return;
        }

        var mentionNames = await LoadMentionDisplayNamesAsync(body, mentioned, cancellationToken);
        var payload = PushDispatchPolicies.BuildNgswPayload(
            authorName,
            isDirect,
            channel.Name,
            PushDispatchPolicies.TruncatePreview(MentionTokens.FormatPlainText(body, mentionNames)),
            channelId.Value,
            messageId,
            sequence);
        var now = clock.UtcNow;
        var gone = new List<PushSubscriptionEntity>();
        foreach (var subscription in subscriptions)
        {
            var result = await pushSender.SendAsync(
                new PushDeliveryRequest(subscription.Endpoint, subscription.P256dh, subscription.Auth, payload),
                cancellationToken);
            if (result.Status == PushSendStatus.Gone)
            {
                gone.Add(subscription);
            }
            else if (result.Status == PushSendStatus.Failed)
            {
                subscription.FailedAt = now;
            }
            else
            {
                subscription.LastSeenAt = now;
                subscription.FailedAt = null;
            }
        }

        if (gone.Count > 0)
        {
            dbContext.PushSubscriptions.RemoveRange(gone);
            logger.LogInformation("Removed {Count} expired web push subscriptions", gone.Count);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public static async Task DeleteForUserAsync(
        VibeChatDbContext dbContext,
        TenantId tenantId,
        UserId userId,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.PushSubscriptions
            .Where(x => x.TenantId == tenantId && x.UserId == userId)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return;
        }

        dbContext.PushSubscriptions.RemoveRange(rows);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<UserId>> LoadCandidateUserIdsAsync(
        TenantId tenantId,
        Channel channel,
        CancellationToken cancellationToken)
    {
        if (channel.Type is ChannelType.Public or ChannelType.Announcement)
        {
            return await dbContext.WorkspaceMembers.AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.WorkspaceId == channel.WorkspaceId)
                .Select(x => x.UserId)
                .Distinct()
                .ToListAsync(cancellationToken);
        }

        return await dbContext.ChannelMembers.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.ChannelId == channel.Id)
            .Select(x => x.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyDictionary<UserId, string>> LoadMentionDisplayNamesAsync(
        string body,
        IReadOnlyList<Guid> mentioned,
        CancellationToken cancellationToken)
    {
        var userIds = MentionTokens.ParseBody(body)
            .Where(token => token.UserId.HasValue)
            .Select(token => token.UserId.GetValueOrDefault())
            .Concat(mentioned.Select(id => new UserId(id)))
            .Distinct()
            .ToList();
        if (userIds.Count == 0)
        {
            return new Dictionary<UserId, string>();
        }

        return await dbContext.UserProfiles.AsNoTracking()
            .Where(x => userIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, cancellationToken);
    }

    private static IReadOnlyList<Guid> ParseMentionedUserIds(JsonNode? node)
    {
        if (node is not JsonArray array)
        {
            return [];
        }

        var ids = new List<Guid>(array.Count);
        foreach (var item in array)
        {
            if (item is JsonValue value)
            {
                if (value.TryGetValue<Guid>(out var guid))
                {
                    ids.Add(guid);
                    continue;
                }

                if (value.TryGetValue<string>(out var raw) && Guid.TryParse(raw, out var parsedFromString))
                {
                    ids.Add(parsedFromString);
                    continue;
                }
            }

            if (item is not null && Guid.TryParse(item.ToString().Trim('"'), out var parsed))
            {
                ids.Add(parsed);
            }
        }

        return ids;
    }
}
