using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using VibeChat.Api;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Files;
using VibeChat.Identity;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using static VibeChat.Api.Endpoints.IdentityEndpointHelpers;

namespace VibeChat.Api.Endpoints;

internal static class AdministrationEndpointHelpers
{
    internal static async Task<(UserProfile Profile, TenantId TenantId)?> ResolveAdminDashboardAccessAsync(
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPermissionChecker permissions,
        IClock clock,
        CancellationToken ct)
    {
        var profile = await EnsureProfileAsync(http.User, db, clock, ct);
        await BeginRlsUserAsync(db, tenant, profile.Id, ct);
        var membership = await db.WorkspaceMembers.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.UserId == profile.Id)
            .OrderBy(x => x.JoinedAt)
            .FirstOrDefaultAsync(ct);
        if (membership is null
            || !await permissions.HasPermissionAsync(membership.TenantId, profile.Id, Permissions.Admin.Dashboard, ct))
        {
            return null;
        }

        tenant.SetTenant(membership.TenantId);
        await RlsSession.EnsureAppliedAsync(db, tenant, ct);
        return (profile, membership.TenantId);
    }

    internal static async Task<(UserProfile Profile, Workspace Workspace)?> ResolveSensitiveSettingsAccessAsync(
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPermissionChecker permissions,
        Guid? workspaceId,
        IClock clock,
        CancellationToken ct)
    {
        var profile = await EnsureProfileAsync(http.User, db, clock, ct);

        Workspace? workspace = null;
        if (workspaceId is { } id && id != Guid.Empty)
        {
            workspace = await ResolveWorkspaceAsync(new WorkspaceId(id), profile.Id, db, tenant, ct);
            if (workspace is null)
            {
                return null;
            }
        }
        else
        {
            await BeginRlsUserAsync(db, tenant, profile.Id, ct);
            var membership = await db.WorkspaceMembers.IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => x.UserId == profile.Id)
                .OrderBy(x => x.JoinedAt)
                .FirstOrDefaultAsync(ct);
            if (membership is null)
            {
                return null;
            }

            workspace = await db.Workspaces.IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.Id == membership.WorkspaceId, ct);
            if (workspace is null)
            {
                return null;
            }

            tenant.SetTenant(workspace.TenantId);
            await RlsSession.EnsureAppliedAsync(db, tenant, ct);
        }

        // B-069 / B-046: sensitive settings and workspace export are workspace-admin only —
        // Auditor (admin.dashboard) may view conversation audit (B-067) but must not export
        // or read/alter AI/SMTP integration flags.
        var canWorkspaceAdmin = await permissions.HasPermissionAsync(
            workspace.TenantId, profile.Id, Permissions.Workspace.Admin, ct);
        if (!canWorkspaceAdmin)
        {
            return null;
        }

        return (profile, workspace);
    }

    internal static async Task<byte[]> BuildWorkspaceExportZipAsync(
        Workspace workspace,
        VibeChatDbContext db,
        UserId actorUserId,
        DateTimeOffset exportedAt,
        CancellationToken ct)
    {
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true
        };

        var members = await (
            from m in db.WorkspaceMembers.AsNoTracking()
            join u in db.UserProfiles.AsNoTracking() on m.UserId equals u.Id
            where m.TenantId == workspace.TenantId && m.WorkspaceId == workspace.Id
            orderby m.JoinedAt
            select new
            {
                userId = m.UserId.Value,
                displayName = u.DisplayName,
                email = u.Email,
                role = m.Role.ToString(),
                joinedAt = m.JoinedAt
            }).ToListAsync(ct);

        var spaces = await db.Spaces.AsNoTracking()
            .Where(x => x.TenantId == workspace.TenantId && x.WorkspaceId == workspace.Id)
            .OrderBy(x => x.Order)
            .Select(x => new
            {
                id = x.Id,
                name = x.Name,
                order = x.Order,
                createdAt = x.CreatedAt
            })
            .ToListAsync(ct);

        var channels = await db.Channels.AsNoTracking()
            .Where(x => x.TenantId == workspace.TenantId && x.WorkspaceId == workspace.Id)
            .OrderBy(x => x.CreatedAt)
            .Select(x => new
            {
                id = x.Id.Value,
                name = x.Name,
                type = x.Type.ToString(),
                spaceId = x.SpaceId,
                createdAt = x.CreatedAt,
                createdBy = x.CreatedBy.Value
            })
            .ToListAsync(ct);

        var channelIds = channels.Select(c => new ChannelId(c.id)).ToList();

        var threads = await db.MessageThreads.AsNoTracking()
            .Where(x => x.TenantId == workspace.TenantId && channelIds.Contains(x.ChannelId))
            .OrderBy(x => x.CreatedAt)
            .Select(x => new
            {
                id = x.Id,
                channelId = x.ChannelId.Value,
                parentMessageId = x.ParentMessageId.Value,
                createdBy = x.CreatedBy.Value,
                createdAt = x.CreatedAt
            })
            .ToListAsync(ct);

        // Messages in root use ConversationId = channel; replies use ConversationId = threadId.
        // Include soft-deleted bodies (compliance parity with B-067). Attachment binaries omitted.
        var threadConversationIds = threads.Select(t => new ChannelId(t.id)).ToList();
        var messageEntities = await db.Messages.AsNoTracking()
            .Where(x => x.TenantId == workspace.TenantId
                && (channelIds.Contains(x.ConversationId) || threadConversationIds.Contains(x.ConversationId)))
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => x.Sequence)
            .ToListAsync(ct);

        var allMessages = messageEntities
            .Select(x => new
            {
                id = x.Id.Value,
                conversationId = x.ConversationId.Value,
                sequence = x.Sequence,
                authorId = x.AuthorId.Value,
                body = x.Body,
                replyToMessageId = x.ReplyToMessageId?.Value,
                threadId = x.ThreadId,
                createdAt = x.CreatedAt,
                editedAt = x.EditedAt,
                deletedAt = x.DeletedAt,
                deletedBy = x.DeletedBy?.Value
            })
            .ToList();

        var messageIdSet = allMessages.Select(m => m.id).ToHashSet();
        var attachmentEntities = await db.Attachments.AsNoTracking()
            .Where(x => x.TenantId == workspace.TenantId && channelIds.Contains(x.ChannelId))
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);

        var attachments = attachmentEntities
            .Where(x => x.MessageId is { } mid && messageIdSet.Contains(mid.Value))
            .Select(x => new
            {
                id = x.Id,
                messageId = x.MessageId!.Value.Value,
                channelId = x.ChannelId.Value,
                fileName = x.FileName,
                contentType = x.ContentType,
                sizeBytes = x.SizeBytes,
                status = x.Status.ToString(),
                checksumSha256 = x.ChecksumSha256,
                createdAt = x.CreatedAt
            })
            .ToList();

        var manifest = new
        {
            format = "vibechat.workspace.export.v1",
            tenantId = workspace.TenantId.Value,
            workspaceId = workspace.Id.Value,
            workspaceSlug = workspace.Slug,
            exportedAt,
            actorUserId = actorUserId.Value,
            counts = new
            {
                members = members.Count,
                spaces = spaces.Count,
                channels = channels.Count,
                threads = threads.Count,
                messages = allMessages.Count,
                attachments = attachments.Count
            }
        };

        var workspacePayload = new
        {
            id = workspace.Id.Value,
            tenantId = workspace.TenantId.Value,
            name = workspace.Name,
            slug = workspace.Slug,
            aiEnabled = workspace.AiEnabled,
            createdAt = workspace.CreatedAt
        };

        await using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            await WriteZipJsonEntryAsync(zip, "manifest.json", manifest, jsonOptions, ct);
            await WriteZipJsonEntryAsync(zip, "workspace.json", workspacePayload, jsonOptions, ct);
            await WriteZipJsonEntryAsync(zip, "members.json", members, jsonOptions, ct);
            await WriteZipJsonEntryAsync(zip, "spaces.json", spaces, jsonOptions, ct);
            await WriteZipJsonEntryAsync(zip, "channels.json", channels, jsonOptions, ct);
            await WriteZipJsonEntryAsync(zip, "threads.json", threads, jsonOptions, ct);
            await WriteZipJsonEntryAsync(zip, "messages.json", allMessages, jsonOptions, ct);
            await WriteZipJsonEntryAsync(zip, "attachments.json", attachments, jsonOptions, ct);
        }

        return memory.ToArray();
    }

    internal static async Task WriteZipJsonEntryAsync<T>(
        ZipArchive zip,
        string entryName,
        T payload,
        JsonSerializerOptions jsonOptions,
        CancellationToken ct)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await JsonSerializer.SerializeAsync(stream, payload, jsonOptions, ct);
    }
}
