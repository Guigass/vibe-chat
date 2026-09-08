using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Identity;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.Realtime;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;

namespace VibeChat.Api;

internal static class GroupDmEndpoints
{
    internal static void Map(RouteGroupBuilder v1)
    {
        v1.MapPost("/workspaces/{workspaceId:guid}/group-dms", CreateGroupDm)
            .AllowPermissionGateExempt("membership-only group DM (B-101)");
        v1.MapPost("/channels/{channelId:guid}/participants", AddParticipants)
            .AllowPermissionGateExempt("membership-only group DM add (B-101)");
        v1.MapDelete("/channels/{channelId:guid}/participants/me", LeaveGroupDm)
            .AllowPermissionGateExempt("membership-only group DM leave (B-101)");
        v1.MapPatch("/channels/{channelId:guid}", RenameGroupDm)
            .AllowPermissionGateExempt("membership-only group DM rename (B-101)");
    }

    private static async Task<IResult> CreateGroupDm(
        Guid workspaceId,
        CreateGroupDmRequest body,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        IOptions<GroupDmOptions> options,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return TypedResults.NotFound();
        }

        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        if (workspace is null)
        {
            return TypedResults.Forbid();
        }

        if (!GroupDmPolicies.TryNormalizeMembers(
                body.UserIds ?? [],
                profile.Id.Value,
                options.Value.MaxParticipants,
                out var members,
                out var error))
        {
            return TypedResults.BadRequest(new { error });
        }

        var peerCheck = await EnsurePeersInWorkspaceAsync(db, workspace.Id, members, ct);
        if (peerCheck is not null)
        {
            return peerCheck;
        }

        var setKey = GroupDmPolicies.ParticipantSetKey(members);
        var existing = await db.Channels.FirstOrDefaultAsync(
            c => c.WorkspaceId == workspace.Id
                 && c.Type == ChannelType.GroupDm
                 && c.ParticipantSetKey == setKey,
            ct);
        if (existing is not null)
        {
            var existingMembers = await LoadActiveMembersAsync(db, existing.Id, ct);
            return TypedResults.Ok(ToChannelResponse(existing, existingMembers, profile.Id));
        }

        var channelId = ChannelId.New();
        var now = clock.UtcNow;
        var channel = new Channel
        {
            Id = channelId,
            TenantId = workspace.TenantId,
            WorkspaceId = workspace.Id,
            Name = $"gdm:{channelId.Value:N}",
            Title = NormalizeTitle(body.Name),
            Type = ChannelType.GroupDm,
            CreatedAt = now,
            CreatedBy = profile.Id,
            ParticipantSetKey = setKey,
        };
        db.Channels.Add(channel);
        foreach (var userId in members)
        {
            db.ChannelMembers.Add(new ChannelMember
            {
                Id = Guid.NewGuid(),
                TenantId = workspace.TenantId,
                ChannelId = channelId,
                UserId = new UserId(userId),
                JoinedAt = now,
                JoinedSeq = 0,
            });
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var raced = await db.Channels.FirstOrDefaultAsync(
                c => c.WorkspaceId == workspace.Id
                     && c.Type == ChannelType.GroupDm
                     && c.ParticipantSetKey == setKey,
                ct);
            if (raced is null)
            {
                throw;
            }

            var racedMembers = await LoadActiveMembersAsync(db, raced.Id, ct);
            return TypedResults.Ok(ToChannelResponse(raced, racedMembers, profile.Id));
        }

        var createdMembers = await LoadActiveMembersAsync(db, channelId, ct);
        return TypedResults.Created(
            $"/api/v1/channels/{channelId.Value}",
            ToChannelResponse(channel, createdMembers, profile.Id));
    }

    private static async Task<IResult> AddParticipants(
        Guid channelId,
        AddGroupDmParticipantsRequest body,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        IMessageWriter messages,
        IOptions<GroupDmOptions> options,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return TypedResults.NotFound();
        }

        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var channel = await RequestAuth.ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
        if (channel is null)
        {
            return TypedResults.Forbid();
        }

        var added = (body.UserIds ?? [])
            .Where(id => id != Guid.Empty && id != profile.Id.Value)
            .Distinct()
            .ToArray();
        if (added.Length == 0)
        {
            return TypedResults.BadRequest(new { error = "GroupDmAddRequiresUsers" });
        }

        if (channel.Type == ChannelType.Direct)
        {
            var current = await LoadActiveMembersAsync(db, channel.Id, ct);
            var peers = current.Select(m => m.UserId.Value)
                .Concat(added)
                .Where(id => id != profile.Id.Value)
                .Distinct()
                .ToArray();
            return await CreateGroupDm(
                channel.WorkspaceId.Value,
                new CreateGroupDmRequest(peers, null),
                http,
                db,
                tenant,
                clock,
                options,
                ct);
        }

        if (channel.Type != ChannelType.GroupDm)
        {
            return TypedResults.BadRequest(new { error = "ChannelIsNotGroupDm" });
        }

        var active = await LoadActiveMembersAsync(db, channel.Id, ct);
        var activeIds = active.Select(m => m.UserId.Value).ToHashSet();
        var newcomers = added.Where(id => !activeIds.Contains(id)).ToArray();
        if (newcomers.Length == 0)
        {
            return TypedResults.Ok(ToChannelResponse(channel, active, profile.Id));
        }

        if (activeIds.Count + newcomers.Length > options.Value.MaxParticipants)
        {
            return TypedResults.BadRequest(new { error = "GroupDmTooManyParticipants" });
        }

        var peerCheck = await EnsurePeersInWorkspaceAsync(db, channel.WorkspaceId, newcomers, ct);
        if (peerCheck is not null)
        {
            return peerCheck;
        }

        var lastSeq = await db.Messages.AsNoTracking()
            .Where(m => m.ConversationId == channel.Id)
            .Select(m => (long?)m.Sequence)
            .MaxAsync(ct) ?? 0;
        var now = clock.UtcNow;

        foreach (var userId in newcomers)
        {
            var typedId = new UserId(userId);
            var prior = await db.ChannelMembers.IgnoreQueryFilters()
                .FirstOrDefaultAsync(m => m.ChannelId == channel.Id && m.UserId == typedId, ct);
            if (prior is null)
            {
                db.ChannelMembers.Add(new ChannelMember
                {
                    Id = Guid.NewGuid(),
                    TenantId = channel.TenantId,
                    ChannelId = channel.Id,
                    UserId = typedId,
                    JoinedAt = now,
                    JoinedSeq = lastSeq,
                });
            }
            else
            {
                prior.LeftAt = null;
                prior.LeftSeq = null;
                prior.JoinedAt = now;
                prior.JoinedSeq = lastSeq;
            }
        }

        var nextIds = activeIds.Concat(newcomers).Distinct().ToArray();
        channel.ParticipantSetKey = await UniqueSetKeyAsync(db, channel, nextIds, ct);
        await db.SaveChangesAsync(ct);

        await messages.SendAsync(
            new SendMessageCommand(
                channel.TenantId,
                profile.Id,
                channel.Id,
                MessageId.New(),
                $"gdm-join:{channel.Id.Value:N}:{lastSeq}:{now.ToUnixTimeMilliseconds()}",
                BuildJoinBody(newcomers.Length),
                null,
                null),
            ct);

        var updated = await LoadActiveMembersAsync(db, channel.Id, ct);
        return TypedResults.Ok(ToChannelResponse(channel, updated, profile.Id));
    }

    private static async Task<IResult> LeaveGroupDm(
        Guid channelId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        IMessageWriter messages,
        IPresenceService presence,
        IHubContext<ChatHub> hub,
        IOptions<GroupDmOptions> options,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return TypedResults.NotFound();
        }

        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var channel = await RequestAuth.ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
        if (channel is null || channel.Type != ChannelType.GroupDm)
        {
            return TypedResults.NotFound();
        }

        await messages.SendAsync(
            new SendMessageCommand(
                channel.TenantId,
                profile.Id,
                channel.Id,
                MessageId.New(),
                $"gdm-leave:{channel.Id.Value:N}:{profile.Id.Value:N}:{clock.UtcNow.ToUnixTimeMilliseconds()}",
                "Um participante saiu do grupo.",
                null,
                null),
            ct);

        var membership = await db.ChannelMembers
            .FirstOrDefaultAsync(m => m.ChannelId == channel.Id && m.UserId == profile.Id, ct);
        if (membership is null)
        {
            return TypedResults.NoContent();
        }

        var leaveSeq = await db.Messages.AsNoTracking()
            .Where(m => m.ConversationId == channel.Id)
            .Select(m => (long?)m.Sequence)
            .MaxAsync(ct) ?? 0;
        membership.LeftAt = clock.UtcNow;
        membership.LeftSeq = leaveSeq;

        var remaining = await db.ChannelMembers
            .Where(m => m.ChannelId == channel.Id && m.UserId != profile.Id)
            .Select(m => m.UserId.Value)
            .ToListAsync(ct);
        channel.ParticipantSetKey = remaining.Count == 0
            ? $"gdm:{channel.Id.Value:N}"
            : await UniqueSetKeyAsync(db, channel, remaining, ct);

        await db.SaveChangesAsync(ct);

        foreach (var connectionId in await presence.GetConnectionIdsAsync(channel.TenantId, profile.Id, ct))
        {
            await hub.Groups.RemoveFromGroupAsync(
                connectionId,
                ChatHub.ChannelGroup(channel.TenantId, channel.Id),
                ct);
        }

        return TypedResults.NoContent();
    }

    private static async Task<IResult> RenameGroupDm(
        Guid channelId,
        RenameGroupDmRequest body,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        IOptions<GroupDmOptions> options,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return TypedResults.NotFound();
        }

        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var channel = await RequestAuth.ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
        if (channel is null || channel.Type != ChannelType.GroupDm)
        {
            return TypedResults.NotFound();
        }

        channel.Title = NormalizeTitle(body.Name);
        await db.SaveChangesAsync(ct);
        var members = await LoadActiveMembersAsync(db, channel.Id, ct);
        return TypedResults.Ok(ToChannelResponse(channel, members, profile.Id));
    }

    private static async Task<IResult?> EnsurePeersInWorkspaceAsync(
        VibeChatDbContext db,
        WorkspaceId workspaceId,
        IReadOnlyCollection<Guid> userIds,
        CancellationToken ct)
    {
        var typed = userIds.Select(id => new UserId(id)).ToArray();
        var found = await db.UserProfiles.AsNoTracking()
            .CountAsync(u => typed.Contains(u.Id), ct);
        if (found != typed.Length)
        {
            return TypedResults.Forbid();
        }

        var memberships = await db.WorkspaceMembers.AsNoTracking()
            .CountAsync(m => m.WorkspaceId == workspaceId && typed.Contains(m.UserId), ct);
        return memberships == typed.Length ? null : TypedResults.Forbid();
    }

    internal static async Task<List<MemberRow>> LoadActiveMembersAsync(
        VibeChatDbContext db,
        ChannelId channelId,
        CancellationToken ct)
    {
        return await (
            from m in db.ChannelMembers.AsNoTracking()
            join u in db.UserProfiles.AsNoTracking() on m.UserId equals u.Id
            where m.ChannelId == channelId
            orderby u.DisplayName
            select new MemberRow(m.UserId, u.DisplayName, m.JoinedSeq)).ToListAsync(ct);
    }

    internal static async Task<Dictionary<ChannelId, GroupDmInfo>> ResolveInfosAsync(
        IReadOnlyCollection<Channel> channels,
        UserId currentUserId,
        VibeChatDbContext db,
        CancellationToken ct)
    {
        var groupIds = channels.Where(c => c.Type == ChannelType.GroupDm).Select(c => c.Id).ToArray();
        if (groupIds.Length == 0)
        {
            return new Dictionary<ChannelId, GroupDmInfo>();
        }

        var rows = await (
            from m in db.ChannelMembers.AsNoTracking()
            join u in db.UserProfiles.AsNoTracking() on m.UserId equals u.Id
            where groupIds.Contains(m.ChannelId)
            select new { m.ChannelId, m.UserId, u.DisplayName }).ToListAsync(ct);

        return channels
            .Where(c => c.Type == ChannelType.GroupDm)
            .ToDictionary(
                c => c.Id,
                c =>
                {
                    var members = rows.Where(r => r.ChannelId == c.Id).ToArray();
                    var names = members.Select(r => r.DisplayName).ToArray();
                    var others = members
                        .Where(r => r.UserId != currentUserId)
                        .Select(r => r.DisplayName)
                        .ToArray();
                    var display = !string.IsNullOrWhiteSpace(c.Title)
                        ? c.Title!
                        : others.Length > 0
                            ? string.Join(", ", others)
                            : string.Join(", ", names);
                    return new GroupDmInfo(
                        members.Length,
                        names,
                        display,
                        members.Select(r => r.UserId.Value).ToArray());
                });
    }

    private static async Task<string> UniqueSetKeyAsync(
        VibeChatDbContext db,
        Channel channel,
        IEnumerable<Guid> userIds,
        CancellationToken ct)
    {
        var key = GroupDmPolicies.ParticipantSetKey(userIds);
        if (string.IsNullOrEmpty(key))
        {
            return $"gdm:{channel.Id.Value:N}";
        }

        var clash = await db.Channels.AsNoTracking().AnyAsync(
            c => c.WorkspaceId == channel.WorkspaceId
                 && c.Id != channel.Id
                 && c.ParticipantSetKey == key,
            ct);
        return clash ? $"{key}:{channel.Id.Value:N}" : key;
    }

    private static string? NormalizeTitle(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= GroupDmPolicies.TitleMaxLength
            ? trimmed
            : trimmed[..GroupDmPolicies.TitleMaxLength];
    }

    private static string BuildJoinBody(int count) =>
        count == 1 ? "Um participante entrou no grupo." : $"{count} participantes entraram no grupo.";

    internal static ChannelResponse ToChannelResponse(
        Channel channel,
        IReadOnlyList<MemberRow> members,
        UserId currentUserId)
    {
        var names = members.Select(m => m.DisplayName).ToArray();
        var others = members
            .Where(m => m.UserId != currentUserId)
            .Select(m => m.DisplayName)
            .ToArray();
        var display = !string.IsNullOrWhiteSpace(channel.Title)
            ? channel.Title!
            : others.Length > 0
                ? string.Join(", ", others)
                : names.Length > 0
                    ? string.Join(", ", names)
                    : channel.Name;
        return new ChannelResponse(
            channel.Id.Value,
            channel.WorkspaceId.Value,
            display,
            channel.Type.ToString(),
            SpaceId: channel.SpaceId,
            Topic: channel.Topic,
            ParticipantCount: members.Count,
            ParticipantNames: names,
            ParticipantUserIds: members.Select(m => m.UserId.Value).ToArray());
    }

    internal readonly record struct MemberRow(UserId UserId, string DisplayName, long JoinedSeq);

    internal readonly record struct GroupDmInfo(int Count, string[] Names, string DisplayName, Guid[] UserIds);
}

internal sealed record CreateGroupDmRequest(Guid[]? UserIds, string? Name);

internal sealed record AddGroupDmParticipantsRequest(Guid[]? UserIds);

internal sealed record RenameGroupDmRequest(string? Name);
