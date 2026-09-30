using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VibeChat.Api;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Identity;
using VibeChat.Infrastructure;
using VibeChat.Integrations;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using static VibeChat.Api.Endpoints.ConversationsEndpointHelpers;

namespace VibeChat.Api.Endpoints;

internal static class IntegrationBotEndpoints
{
    internal static void MapIntegrationBots(this RouteGroupBuilder v1)
    {
        v1.MapGet("/admin/workspaces/{workspaceId:guid}/bots", ListBots)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/admin/workspaces/{workspaceId:guid}/bots", CreateBot)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPut("/admin/workspaces/{workspaceId:guid}/bots/{botId:guid}", UpdateBot)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/admin/workspaces/{workspaceId:guid}/bots/{botId:guid}/rotate", RotateToken)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/admin/workspaces/{workspaceId:guid}/bots/{botId:guid}/revoke", RevokeToken)
            .RequirePermission(Permissions.Workspace.Admin);

        v1.MapPost("/integrations/v1/channels/{channelId:guid}/messages", SendChannel)
            .AllowAnonymous()
            .AllowPermissionGateExempt("integration token auth (B-109)");
        v1.MapPost("/integrations/v1/dms", SendDirect)
            .AllowAnonymous()
            .AllowPermissionGateExempt("integration token auth (B-109)");
    }

    private static async Task<IResult> ListBots(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IOptions<BotIntegrationOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        if (workspace is null)
        {
            return Results.Forbid();
        }

        var bots = await db.IntegrationBots.AsNoTracking()
            .Where(x => x.WorkspaceId == workspace.Id)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);
        var responses = new List<IntegrationBotResponse>(bots.Count);
        foreach (var bot in bots)
        {
            responses.Add(await ToResponseAsync(db, bot, ct));
        }

        return Results.Ok(responses);
    }

    private static async Task<IResult> CreateBot(
        Guid workspaceId,
        CreateIntegrationBotRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IAuditWriter audit,
        IOptions<BotIntegrationOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        if (!BotIntegrationPolicies.TryNormalizeName(request.Name, out var name))
        {
            return Results.BadRequest(new { error = "InvalidBotName" });
        }

        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        if (workspace is null)
        {
            return Results.Forbid();
        }

        var channels = await LoadGrantChannelsAsync(db, workspace.Id, request.ChannelIds, ct);
        if (channels is null)
        {
            return Results.BadRequest(new { error = "InvalidChannelScope" });
        }

        var now = clock.UtcNow;
        var botId = Guid.NewGuid();
        var userId = UserId.New();
        var bot = new IntegrationBot
        {
            Id = botId,
            TenantId = workspace.TenantId,
            WorkspaceId = workspace.Id,
            UserId = userId,
            Name = name,
            Enabled = true,
            AllowDms = request.AllowDms,
            CreatedAt = now
        };
        db.UserProfiles.Add(new UserProfile
        {
            Id = userId,
            Subject = $"bot:{botId:N}",
            Email = $"bot+{botId:N}@bots.vibechat.local",
            DisplayName = name,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.WorkspaceMembers.Add(new WorkspaceMember
        {
            Id = Guid.NewGuid(),
            TenantId = workspace.TenantId,
            WorkspaceId = workspace.Id,
            UserId = userId,
            Role = Role.Bot,
            JoinedAt = now
        });
        db.IntegrationBots.Add(bot);
        var (raw, token) = IssueToken(bot, now);
        db.IntegrationBotTokens.Add(token);
        await ReplaceScopesAsync(db, bot, channels, now, ct);
        audit.Add(new AuditEvent
        {
            TenantId = workspace.TenantId,
            ActorUserId = profile.Id,
            Action = AuditActions.IntegrationBotCreate,
            EntityType = "IntegrationBot",
            EntityId = bot.Id.ToString(),
            MetadataJson = "{}",
            OccurredAt = now
        });
        await db.SaveChangesAsync(ct);
        return Results.Created(
            $"/api/v1/admin/workspaces/{workspace.Id.Value}/bots/{bot.Id}",
            ToSecret(bot, channels.Select(x => x.Id.Value).ToArray(), raw, token.Last4));
    }

    private static async Task<IResult> UpdateBot(
        Guid workspaceId,
        Guid botId,
        UpdateIntegrationBotRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IOptions<BotIntegrationOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        if (!BotIntegrationPolicies.TryNormalizeName(request.Name, out var name))
        {
            return Results.BadRequest(new { error = "InvalidBotName" });
        }

        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        if (workspace is null)
        {
            return Results.Forbid();
        }

        var bot = await db.IntegrationBots.FirstOrDefaultAsync(x => x.Id == botId && x.WorkspaceId == workspace.Id, ct);
        if (bot is null)
        {
            return Results.NotFound(new { error = "BotNotFound" });
        }

        var channels = await LoadGrantChannelsAsync(db, workspace.Id, request.ChannelIds, ct);
        if (channels is null)
        {
            return Results.BadRequest(new { error = "InvalidChannelScope" });
        }

        var now = clock.UtcNow;
        bot.Name = name;
        bot.Enabled = request.Enabled;
        bot.AllowDms = request.AllowDms;
        var user = await db.UserProfiles.FirstOrDefaultAsync(x => x.Id == bot.UserId, ct);
        if (user is not null)
        {
            user.DisplayName = name;
            user.UpdatedAt = now;
        }

        await ReplaceScopesAsync(db, bot, channels, now, ct);
        await db.SaveChangesAsync(ct);
        return Results.Ok(await ToResponseAsync(db, bot, ct));
    }

    private static async Task<IResult> RotateToken(
        Guid workspaceId,
        Guid botId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IAuditWriter audit,
        IOptions<BotIntegrationOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        if (workspace is null)
        {
            return Results.Forbid();
        }

        var bot = await db.IntegrationBots.FirstOrDefaultAsync(x => x.Id == botId && x.WorkspaceId == workspace.Id, ct);
        if (bot is null)
        {
            return Results.NotFound(new { error = "BotNotFound" });
        }

        var now = clock.UtcNow;
        await RevokeActiveTokensAsync(db, bot.Id, now, ct);
        var (raw, token) = IssueToken(bot, now);
        db.IntegrationBotTokens.Add(token);
        audit.Add(new AuditEvent
        {
            TenantId = workspace.TenantId,
            ActorUserId = profile.Id,
            Action = AuditActions.IntegrationTokenRotate,
            EntityType = "IntegrationBot",
            EntityId = bot.Id.ToString(),
            MetadataJson = "{}",
            OccurredAt = now
        });
        await db.SaveChangesAsync(ct);
        var channelIds = await db.IntegrationBotChannelScopes.AsNoTracking()
            .Where(x => x.BotId == bot.Id)
            .Select(x => x.ChannelId.Value)
            .ToArrayAsync(ct);
        return Results.Ok(ToSecret(bot, channelIds, raw, token.Last4));
    }

    private static async Task<IResult> RevokeToken(
        Guid workspaceId,
        Guid botId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IAuditWriter audit,
        IOptions<BotIntegrationOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        if (workspace is null)
        {
            return Results.Forbid();
        }

        var bot = await db.IntegrationBots.FirstOrDefaultAsync(x => x.Id == botId && x.WorkspaceId == workspace.Id, ct);
        if (bot is null)
        {
            return Results.NotFound(new { error = "BotNotFound" });
        }

        var now = clock.UtcNow;
        await RevokeActiveTokensAsync(db, bot.Id, now, ct);
        audit.Add(new AuditEvent
        {
            TenantId = workspace.TenantId,
            ActorUserId = profile.Id,
            Action = AuditActions.IntegrationTokenRevoke,
            EntityType = "IntegrationBot",
            EntityId = bot.Id.ToString(),
            MetadataJson = "{}",
            OccurredAt = now
        });
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { revoked = true });
    }

    private static async Task<IResult> SendChannel(
        Guid channelId,
        IntegrationSendRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IMessageWriter messages,
        IAuditWriter audit,
        IRateLimiter rateLimiter,
        RateLimitSettingsResolver rateLimits,
        IOptions<BotIntegrationOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        var principal = await AuthenticateAsync(http, db, tenant, clock, ct);
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        if (!principal.Bot.Enabled)
        {
            return Results.Json(new { error = "BotDisabled" }, statusCode: StatusCodes.Status403Forbidden);
        }

        var channel = await db.Channels.FirstOrDefaultAsync(
            x => x.Id == new ChannelId(channelId) && x.TenantId == principal.Bot.TenantId,
            ct);
        if (channel is null || channel.Type is ChannelType.Direct or ChannelType.GroupDm)
        {
            return Results.Forbid();
        }

        var inScope = await db.IntegrationBotChannelScopes.AnyAsync(
            x => x.BotId == principal.Bot.Id && x.ChannelId == channel.Id,
            ct);
        if (!inScope)
        {
            return Results.Json(new { error = "ChannelOutOfScope" }, statusCode: StatusCodes.Status403Forbidden);
        }

        return await SendAsync(principal, channel, request.Body, request.IdempotencyKey, request.ThreadId, db, messages, audit, rateLimiter, rateLimits, clock, ct);
    }

    private static async Task<IResult> SendDirect(
        IntegrationDirectMessageRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IMessageWriter messages,
        IAuditWriter audit,
        IRateLimiter rateLimiter,
        RateLimitSettingsResolver rateLimits,
        IOptions<BotIntegrationOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        var principal = await AuthenticateAsync(http, db, tenant, clock, ct);
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        if (!principal.Bot.Enabled)
        {
            return Results.Json(new { error = "BotDisabled" }, statusCode: StatusCodes.Status403Forbidden);
        }

        if (!principal.Bot.AllowDms)
        {
            return Results.Json(new { error = "DirectMessagesDisabled" }, statusCode: StatusCodes.Status403Forbidden);
        }

        if (request.UserId == Guid.Empty || request.UserId == principal.Bot.UserId.Value)
        {
            return Results.BadRequest(new { error = "PeerNotFound" });
        }

        var peerId = new UserId(request.UserId);
        var peer = await db.WorkspaceMembers.AsNoTracking().FirstOrDefaultAsync(
            x => x.WorkspaceId == principal.Bot.WorkspaceId && x.UserId == peerId,
            ct);
        if (peer is null)
        {
            return Results.BadRequest(new { error = "PeerNotFound" });
        }

        var channel = await FindDirectChannelAsync(principal.Bot.WorkspaceId, principal.Bot.UserId, peerId, db, ct);
        if (channel is null)
        {
            var now = clock.UtcNow;
            channel = new Channel
            {
                Id = ChannelId.New(),
                TenantId = principal.Bot.TenantId,
                WorkspaceId = principal.Bot.WorkspaceId,
                Name = BuildDirectChannelName(principal.Bot.UserId, peerId),
                Type = ChannelType.Direct,
                CreatedAt = now,
                CreatedBy = principal.Bot.UserId
            };
            db.Channels.Add(channel);
            db.ChannelMembers.AddRange(
                new ChannelMember
                {
                    Id = Guid.NewGuid(),
                    TenantId = principal.Bot.TenantId,
                    ChannelId = channel.Id,
                    UserId = principal.Bot.UserId,
                    JoinedAt = now
                },
                new ChannelMember
                {
                    Id = Guid.NewGuid(),
                    TenantId = principal.Bot.TenantId,
                    ChannelId = channel.Id,
                    UserId = peerId,
                    JoinedAt = now
                });
            await db.SaveChangesAsync(ct);
        }

        return await SendAsync(principal, channel, request.Body, request.IdempotencyKey, request.ThreadId, db, messages, audit, rateLimiter, rateLimits, clock, ct);
    }

    private static async Task<IResult> SendAsync(
        BotSession principal,
        Channel channel,
        string? body,
        string? idempotencyKey,
        Guid? threadId,
        VibeChatDbContext db,
        IMessageWriter messages,
        IAuditWriter audit,
        IRateLimiter rateLimiter,
        RateLimitSettingsResolver rateLimits,
        IClock clock,
        CancellationToken ct)
    {
        var key = (idempotencyKey ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 160)
        {
            return Results.BadRequest(new { error = "IdempotencyKeyRequired" });
        }

        key = $"bot:{principal.Bot.Id:N}:{key}";

        if (string.IsNullOrWhiteSpace(body))
        {
            return Results.BadRequest(new { error = "MessageBodyRequired" });
        }

        var rate = await rateLimits.ResolveAsync(principal.Bot.TenantId, ct);
        var allowed = await rateLimiter.TryAcquireAsync(
            RateLimitKeys.IntegrationSend(principal.Bot.TenantId, principal.Bot.Id),
            rate.SendPerMinute,
            TimeSpan.FromMinutes(1),
            ct);
        if (!allowed)
        {
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        try
        {
            var result = await messages.SendAsync(
                new SendMessageCommand(
                    principal.Bot.TenantId,
                    principal.Bot.UserId,
                    channel.Id,
                    MessageId.New(),
                    key,
                    body.Trim(),
                    null,
                    threadId is { } tid && tid != Guid.Empty ? tid : null,
                    null,
                    AuthorIsBot: true),
                ct);
            if (!result.Idempotent)
            {
                audit.Add(new AuditEvent
                {
                    TenantId = principal.Bot.TenantId,
                    ActorUserId = principal.Bot.UserId,
                    Action = AuditActions.IntegrationMessageSend,
                    EntityType = "Message",
                    EntityId = result.MessageId.Value.ToString(),
                    MetadataJson = "{}",
                    OccurredAt = clock.UtcNow
                });
            }

            await db.SaveChangesAsync(ct);
            return Results.Accepted(
                $"/api/v1/channels/{channel.Id.Value}/messages?after={Math.Max(0, result.Sequence - 1)}",
                new IntegrationMessageResponse(
                    result.MessageId.Value,
                    channel.Id.Value,
                    result.Sequence,
                    result.CreatedAt,
                    result.Idempotent));
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }
        catch (InvalidOperationException)
        {
            return Results.NotFound(new { error = "ThreadNotFound" });
        }
    }

    private static async Task<BotSession?> AuthenticateAsync(
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        CancellationToken ct)
    {
        var raw = ReadToken(http.Request);
        if (raw is null)
        {
            return null;
        }

        var hash = IntegrationToken.Hash(raw);
        await RlsSession.SetIntegrationTokenHashAsync(db, hash, ct);
        var token = await db.IntegrationBotTokens.FirstOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (token is null || token.RevokedAt is not null)
        {
            return null;
        }

        tenant.SetTenant(token.TenantId);
        await RlsSession.EnsureAppliedAsync(db, tenant, ct);
        var bot = await db.IntegrationBots.FirstOrDefaultAsync(x => x.Id == token.BotId, ct);
        if (bot is null)
        {
            return null;
        }

        tenant.SetUser(bot.UserId);
        await RlsSession.EnsureAppliedAsync(db, tenant, ct);
        token.LastUsedAt = clock.UtcNow;
        return new BotSession(bot, token);
    }

    private static string? ReadToken(HttpRequest request)
    {
        if (request.Headers.TryGetValue("X-VibeChat-Integration-Token", out var header)
            && !string.IsNullOrWhiteSpace(header))
        {
            var custom = header.ToString().Trim();
            return custom.StartsWith(BotIntegrationPolicies.TokenPrefix, StringComparison.Ordinal) ? custom : null;
        }

        var authorization = request.Headers.Authorization.ToString();
        const string bearer = "Bearer ";
        if (!authorization.StartsWith(bearer, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = authorization[bearer.Length..].Trim();
        return token.StartsWith(BotIntegrationPolicies.TokenPrefix, StringComparison.Ordinal) ? token : null;
    }

    private static async Task<Channel[]?> LoadGrantChannelsAsync(
        VibeChatDbContext db,
        WorkspaceId workspaceId,
        Guid[]? channelIds,
        CancellationToken ct)
    {
        var ids = (channelIds ?? []).Where(x => x != Guid.Empty).Distinct().Select(x => new ChannelId(x)).ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        var rows = await db.Channels.Where(x => x.WorkspaceId == workspaceId && ids.Contains(x.Id)).ToListAsync(ct);
        if (rows.Count != ids.Length || rows.Any(x => x.Type is ChannelType.Direct or ChannelType.GroupDm))
        {
            return null;
        }

        return rows.ToArray();
    }

    private static async Task ReplaceScopesAsync(
        VibeChatDbContext db,
        IntegrationBot bot,
        Channel[] channels,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var wanted = channels.Select(x => x.Id).ToHashSet();
        var current = await db.IntegrationBotChannelScopes.Where(x => x.BotId == bot.Id).ToListAsync(ct);
        foreach (var scope in current.Where(x => !wanted.Contains(x.ChannelId)).ToArray())
        {
            db.IntegrationBotChannelScopes.Remove(scope);
            var member = await db.ChannelMembers.FirstOrDefaultAsync(
                x => x.ChannelId == scope.ChannelId && x.UserId == bot.UserId && x.LeftAt == null,
                ct);
            if (member is not null)
            {
                member.LeftAt = now;
            }
        }

        var have = current.Select(x => x.ChannelId).ToHashSet();
        foreach (var channel in channels)
        {
            if (!have.Contains(channel.Id))
            {
                db.IntegrationBotChannelScopes.Add(new IntegrationBotChannelScope
                {
                    Id = Guid.NewGuid(),
                    TenantId = bot.TenantId,
                    BotId = bot.Id,
                    ChannelId = channel.Id
                });
            }

            var member = await db.ChannelMembers.FirstOrDefaultAsync(
                x => x.ChannelId == channel.Id && x.UserId == bot.UserId,
                ct);
            if (member is null)
            {
                db.ChannelMembers.Add(new ChannelMember
                {
                    Id = Guid.NewGuid(),
                    TenantId = bot.TenantId,
                    ChannelId = channel.Id,
                    UserId = bot.UserId,
                    JoinedAt = now
                });
            }
            else if (member.LeftAt is not null)
            {
                member.LeftAt = null;
                member.LeftSeq = null;
            }
        }
    }

    private static async Task RevokeActiveTokensAsync(VibeChatDbContext db, Guid botId, DateTimeOffset now, CancellationToken ct)
    {
        var active = await db.IntegrationBotTokens.Where(x => x.BotId == botId && x.RevokedAt == null).ToListAsync(ct);
        foreach (var token in active)
        {
            token.RevokedAt = now;
        }
    }

    private static (string Raw, IntegrationBotToken Token) IssueToken(IntegrationBot bot, DateTimeOffset now)
    {
        var raw = IntegrationToken.CreateRaw();
        return (raw, new IntegrationBotToken
        {
            Id = Guid.NewGuid(),
            TenantId = bot.TenantId,
            BotId = bot.Id,
            TokenHash = IntegrationToken.Hash(raw),
            Last4 = IntegrationToken.Last4(raw),
            CreatedAt = now
        });
    }

    private static async Task<IntegrationBotResponse> ToResponseAsync(VibeChatDbContext db, IntegrationBot bot, CancellationToken ct)
    {
        var channelIds = await db.IntegrationBotChannelScopes.AsNoTracking()
            .Where(x => x.BotId == bot.Id)
            .Select(x => x.ChannelId.Value)
            .ToArrayAsync(ct);
        var token = await db.IntegrationBotTokens.AsNoTracking()
            .Where(x => x.BotId == bot.Id && x.RevokedAt == null)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);
        return new IntegrationBotResponse(
            bot.Id,
            bot.Name,
            bot.Enabled,
            bot.AllowDms,
            channelIds,
            token is not null,
            token?.Last4,
            bot.CreatedAt);
    }

    private static IntegrationBotSecretResponse ToSecret(IntegrationBot bot, Guid[] channelIds, string raw, string last4) =>
        new(bot.Id, bot.Name, bot.Enabled, bot.AllowDms, channelIds, raw, last4, bot.CreatedAt);

    private static IResult Disabled() => Results.NotFound(new { error = "IntegrationDisabled" });

    private sealed record BotSession(IntegrationBot Bot, IntegrationBotToken Token);
}
