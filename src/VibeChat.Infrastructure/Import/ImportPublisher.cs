using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VibeChat.Administration;
using VibeChat.Audit;
using VibeChat.Conversations;
using VibeChat.Directory;
using VibeChat.Files;
using VibeChat.Identity;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;

namespace VibeChat.Infrastructure;

internal sealed class ImportPublisher(
    VibeChatDbContext db,
    IObjectStorage storage,
    IClock clock)
{
    private readonly Dictionary<Guid, ConversationSequence> sequencesSeen = [];
    public async Task<ImportCommandResult> PublishAsync(
        Workspace workspace,
        UserId actor,
        Guid importId,
        Action<TenantId, UserId, string, ImportJobRecord, ImportReport> audit,
        CancellationToken cancellationToken)
    {
        var job = await db.ImportJobs.FirstOrDefaultAsync(x => x.Id == importId && x.WorkspaceId == workspace.Id, cancellationToken);
        if (job is null)
        {
            return new ImportCommandResult(404, ImportErrors.NotFound, null, false);
        }

        if (job.Status == ImportStatus.Published)
        {
            return new ImportCommandResult(200, null, job, true);
        }

        if (!ImportCommands.Allowed(job.Status, ImportCommands.Publish))
        {
            return new ImportCommandResult(409, ImportErrors.State, null, false);
        }

        var document = WorkspaceImportService.Deserialize(job);
        var report = WorkspaceImportService.DeserializeReport(job);
        if (report.Blocking)
        {
            return new ImportCommandResult(409, ImportErrors.Conflict, null, false);
        }

        var maps = await db.ImportIdMaps.Where(x => x.ImportJobId == job.Id).ToListAsync(cancellationToken);
        var uploaded = new List<string>();
        try
        {
            var authors = await PublishPrincipalsAsync(job, document, maps, cancellationToken);
            var spaces = await PublishSpacesAsync(workspace, job, document, maps, cancellationToken);
            var channels = await PublishChannelsAsync(workspace, actor, job, document, maps, spaces, cancellationToken);
            var messages = await PublishMessagesAsync(workspace.TenantId, job, document, maps, authors, channels, cancellationToken);
            await PublishAttachmentsAsync(workspace.TenantId, job, document, maps, messages, uploaded, cancellationToken);
            ImportPolicies.StripPayloads(document);
            job.CanonicalJson = JsonSerializer.Serialize(document, WorkspaceImportService.Json);
            job.Status = ImportStatus.Published;
            job.UpdatedAt = clock.UtcNow;
            audit(workspace.TenantId, actor, AuditActions.ImportPublish, job, report);
            await db.SaveChangesAsync(cancellationToken);
            return new ImportCommandResult(200, null, job, false);
        }
        catch
        {
            foreach (var key in uploaded)
            {
                try
                {
                    await storage.DeleteObjectAsync(key, CancellationToken.None);
                }
                catch
                {
                    // The database transaction is the source of truth; leftover objects are unnamed until commit.
                }
            }

            throw;
        }
    }

    public async Task<ImportCommandResult> RollbackAsync(
        Workspace workspace,
        UserId actor,
        Guid importId,
        bool confirm,
        Action<TenantId, UserId, string, ImportJobRecord, ImportReport> audit,
        CancellationToken cancellationToken)
    {
        var job = await db.ImportJobs.FirstOrDefaultAsync(x => x.Id == importId && x.WorkspaceId == workspace.Id, cancellationToken);
        if (job is null)
        {
            return new ImportCommandResult(404, ImportErrors.NotFound, null, false);
        }

        if (job.Status == ImportStatus.RolledBack)
        {
            return new ImportCommandResult(200, null, job, true);
        }

        if (!ImportCommands.Allowed(job.Status, ImportCommands.Rollback))
        {
            return new ImportCommandResult(409, ImportErrors.State, null, false);
        }

        if (job.Status == ImportStatus.Published && !confirm)
        {
            return new ImportCommandResult(409, ImportErrors.ConfirmRequired, null, false);
        }

        var maps = await db.ImportIdMaps.Where(x => x.ImportJobId == job.Id).ToListAsync(cancellationToken);
        if (job.Status == ImportStatus.Published)
        {
            await RemovePublishedAsync(workspace.TenantId, maps, cancellationToken);
        }

        db.ImportIdMaps.RemoveRange(maps);
        var historical = await db.ImportHistoricalPrincipals.Where(x => x.ImportJobId == job.Id).ToListAsync(cancellationToken);
        if (job.Status != ImportStatus.Published)
        {
            db.ImportHistoricalPrincipals.RemoveRange(historical);
        }

        var report = WorkspaceImportService.DeserializeReport(job);
        job.CanonicalJson = "{}";
        job.Status = ImportStatus.RolledBack;
        job.PauseFrom = null;
        job.UpdatedAt = clock.UtcNow;
        audit(workspace.TenantId, actor, AuditActions.ImportRollback, job, report);
        await db.SaveChangesAsync(cancellationToken);
        return new ImportCommandResult(200, null, job, false);
    }

    private async Task<Dictionary<string, UserId>> PublishPrincipalsAsync(
        ImportJobRecord job,
        CanonicalImport document,
        List<ImportIdMapRecord> maps,
        CancellationToken cancellationToken)
    {
        var authors = new Dictionary<string, UserId>(StringComparer.Ordinal);
        foreach (var principal in document.Principals)
        {
            var map = Map(maps, ImportResourceTypes.Principal, principal.ExternalId);
            if (principal.MappedUserId is Guid mapped)
            {
                var userId = new UserId(mapped);
                authors[principal.ExternalId] = userId;
                if (map is not null)
                {
                    map.CanonicalId = mapped;
                    map.Disposition = ImportDispositions.Reused;
                }

                continue;
            }

            var userIdCreated = map?.CanonicalId is Guid existing ? new UserId(existing) : new UserId(Guid.NewGuid());
            var subject = HistoricalSubject(job.Id, principal.ExternalId);
            var profile = await db.UserProfiles.FirstOrDefaultAsync(x => x.Subject == subject, cancellationToken);
            if (profile is null)
            {
                profile = new UserProfile
                {
                    Id = userIdCreated,
                    Subject = subject,
                    Email = $"{userIdCreated.Value:N}@import.invalid",
                    DisplayName = principal.DisplayName,
                    CreatedAt = clock.UtcNow,
                    UpdatedAt = clock.UtcNow
                };
                db.UserProfiles.Add(profile);
            }

            authors[principal.ExternalId] = profile.Id;
            if (map is not null)
            {
                map.CanonicalId = profile.Id.Value;
                map.Disposition = ImportDispositions.Historical;
            }

            if (!await db.ImportHistoricalPrincipals.AnyAsync(
                    x => x.ImportJobId == job.Id && x.ExternalId == principal.ExternalId,
                    cancellationToken))
            {
                db.ImportHistoricalPrincipals.Add(new ImportHistoricalPrincipalRecord
                {
                    Id = Guid.NewGuid(),
                    TenantId = job.TenantId,
                    ImportJobId = job.Id,
                    UserId = profile.Id,
                    ExternalId = principal.ExternalId
                });
            }
        }

        return authors;
    }

    private async Task<Dictionary<string, Guid>> PublishSpacesAsync(
        Workspace workspace,
        ImportJobRecord job,
        CanonicalImport document,
        List<ImportIdMapRecord> maps,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var order = await db.Spaces.Where(x => x.WorkspaceId == workspace.Id).Select(x => (int?)x.Order).MaxAsync(cancellationToken) ?? 0;
        foreach (var space in document.Spaces)
        {
            var map = Map(maps, ImportResourceTypes.Space, space.ExternalId);
            var existing = db.Spaces.Local.FirstOrDefault(x => x.WorkspaceId == workspace.Id && x.Name == space.Name)
                ?? await db.Spaces.FirstOrDefaultAsync(
                    x => x.WorkspaceId == workspace.Id && x.Name == space.Name,
                    cancellationToken);
            if (existing is not null)
            {
                result[space.ExternalId] = existing.Id;
                if (map is not null)
                {
                    map.CanonicalId = existing.Id;
                    map.Disposition = ImportDispositions.Reused;
                }

                continue;
            }

            var id = map?.CanonicalId ?? Guid.NewGuid();
            db.Spaces.Add(new Space
            {
                Id = id,
                TenantId = workspace.TenantId,
                WorkspaceId = workspace.Id,
                Name = space.Name,
                Order = ++order,
                CreatedAt = clock.UtcNow
            });
            result[space.ExternalId] = id;
            if (map is not null)
            {
                map.CanonicalId = id;
                map.Disposition = ImportDispositions.Published;
            }
        }

        return result;
    }

    private async Task<Dictionary<string, ChannelId>> PublishChannelsAsync(
        Workspace workspace,
        UserId actor,
        ImportJobRecord job,
        CanonicalImport document,
        List<ImportIdMapRecord> maps,
        Dictionary<string, Guid> spaces,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, ChannelId>(StringComparer.Ordinal);
        foreach (var channel in document.Channels.Where(x => !x.Ignored))
        {
            var map = Map(maps, ImportResourceTypes.Channel, channel.ExternalId);
            var type = channel.Kind == "private" ? ChannelType.Private : ChannelType.Public;
            var existing = db.Channels.Local.FirstOrDefault(x => x.WorkspaceId == workspace.Id && x.Name == channel.Name)
                ?? await db.Channels.FirstOrDefaultAsync(
                    x => x.WorkspaceId == workspace.Id && x.Name == channel.Name,
                    cancellationToken);
            if (existing is not null && existing.Type == type)
            {
                result[channel.ExternalId] = existing.Id;
                if (map is not null)
                {
                    map.CanonicalId = existing.Id.Value;
                    map.Disposition = ImportDispositions.Reused;
                }

                continue;
            }

            var id = new ChannelId(map?.CanonicalId ?? Guid.NewGuid());
            Guid? spaceId = channel.SpaceExternalId is not null && spaces.TryGetValue(channel.SpaceExternalId, out var resolved)
                ? resolved
                : null;
            db.Channels.Add(new Channel
            {
                Id = id,
                TenantId = workspace.TenantId,
                WorkspaceId = workspace.Id,
                SpaceId = spaceId,
                Name = channel.Name,
                Type = type,
                CreatedAt = clock.UtcNow,
                CreatedBy = actor
            });
            if (type == ChannelType.Private)
            {
                db.ChannelMembers.Add(new ChannelMember
                {
                    Id = Guid.NewGuid(),
                    TenantId = workspace.TenantId,
                    ChannelId = id,
                    UserId = actor,
                    JoinedAt = clock.UtcNow
                });
            }

            result[channel.ExternalId] = id;
            if (map is not null)
            {
                map.CanonicalId = id.Value;
                map.Disposition = ImportDispositions.Published;
            }
        }

        return result;
    }

    private async Task<Dictionary<string, MessageId>> PublishMessagesAsync(
        TenantId tenantId,
        ImportJobRecord job,
        CanonicalImport document,
        List<ImportIdMapRecord> maps,
        Dictionary<string, UserId> authors,
        Dictionary<string, ChannelId> channels,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, MessageId>(StringComparer.Ordinal);
        var roots = document.Messages
            .Where(x => !x.Ignored && string.IsNullOrEmpty(x.ThreadExternalId))
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => x.ExternalId, StringComparer.Ordinal);
        foreach (var message in roots)
        {
            if (!channels.TryGetValue(message.ChannelExternalId, out var channelId) || !authors.TryGetValue(message.AuthorExternalId, out var author))
            {
                continue;
            }

            var id = await AddMessageAsync(tenantId, job, maps, message, channelId, channelId, null, author, cancellationToken);
            result[message.ExternalId] = id;
        }

        foreach (var thread in document.Threads)
        {
            if (!result.TryGetValue(thread.RootMessageExternalId, out var rootId) || !channels.TryGetValue(thread.ChannelExternalId, out var channelId))
            {
                continue;
            }

            var map = Map(maps, ImportResourceTypes.Thread, thread.ExternalId);
            if (map?.CanonicalId is Guid existingThread)
            {
                continue;
            }

            var root = document.Messages.First(x => x.ExternalId == thread.RootMessageExternalId);
            var threadId = Guid.NewGuid();
            db.MessageThreads.Add(new MessageThread
            {
                Id = threadId,
                TenantId = tenantId,
                ChannelId = channelId,
                ParentMessageId = rootId,
                CreatedBy = authors.TryGetValue(root.AuthorExternalId, out var author) ? author : job.CreatedBy,
                CreatedAt = root.CreatedAt
            });
            if (map is not null)
            {
                map.CanonicalId = threadId;
                map.Disposition = ImportDispositions.Published;
            }

            var replies = document.Messages
                .Where(x => !x.Ignored && x.ThreadExternalId == thread.ExternalId)
                .OrderBy(x => x.CreatedAt)
                .ThenBy(x => x.ExternalId, StringComparer.Ordinal);
            foreach (var reply in replies)
            {
                if (!authors.TryGetValue(reply.AuthorExternalId, out var replyAuthor))
                {
                    continue;
                }

                var replyId = await AddMessageAsync(
                    tenantId,
                    job,
                    maps,
                    reply,
                    channelId,
                    new ChannelId(threadId),
                    threadId,
                    replyAuthor,
                    cancellationToken);
                result[reply.ExternalId] = replyId;
            }
        }

        return result;
    }

    private async Task<MessageId> AddMessageAsync(
        TenantId tenantId,
        ImportJobRecord job,
        List<ImportIdMapRecord> maps,
        CanonicalMessage message,
        ChannelId parentChannelId,
        ChannelId conversationId,
        Guid? threadId,
        UserId author,
        CancellationToken cancellationToken)
    {
        var map = Map(maps, ImportResourceTypes.Message, message.ExternalId);
        if (map?.CanonicalId is Guid existing)
        {
            return new MessageId(existing);
        }

            var sequence = await NextSequenceAsync(tenantId, conversationId, cancellationToken);
        var id = new MessageId(Guid.NewGuid());
        db.Messages.Add(new Message
        {
            Id = id,
            TenantId = tenantId,
            ConversationId = conversationId,
            AuthorId = author,
            Sequence = sequence,
            Body = message.Body,
            ThreadId = threadId,
            CreatedAt = message.CreatedAt
        });
        _ = parentChannelId;
        if (map is not null)
        {
            map.CanonicalId = id.Value;
            map.Disposition = ImportDispositions.Published;
        }

        return id;
    }

    private async Task PublishAttachmentsAsync(
        TenantId tenantId,
        ImportJobRecord job,
        CanonicalImport document,
        List<ImportIdMapRecord> maps,
        Dictionary<string, MessageId> messages,
        List<string> uploaded,
        CancellationToken cancellationToken)
    {
        foreach (var attachment in document.Attachments)
        {
            var map = Map(maps, ImportResourceTypes.Attachment, attachment.ExternalId);
            if (attachment.Quarantined || string.IsNullOrEmpty(attachment.PayloadBase64))
            {
                if (map is not null)
                {
                    map.Disposition = attachment.Quarantined ? ImportDispositions.Quarantined : ImportDispositions.Ignored;
                }

                continue;
            }

            if (!messages.TryGetValue(attachment.MessageExternalId, out var messageId))
            {
                continue;
            }

            if (map?.CanonicalId is not null)
            {
                continue;
            }

            var bytes = Convert.FromBase64String(attachment.PayloadBase64);
            var id = Guid.NewGuid();
            var key = $"import/{tenantId.Value:N}/{job.Id:N}/{id:N}";
            await using var stream = new MemoryStream(bytes);
            await storage.PutObjectAsync(key, stream, attachment.ContentType, cancellationToken);
            uploaded.Add(key);
            var message = db.Messages.Local.First(x => x.Id == messageId);
            db.Attachments.Add(new Attachment
            {
                Id = id,
                TenantId = tenantId,
                ChannelId = message.ConversationId,
                MessageId = messageId,
                UploadedBy = message.AuthorId,
                FileName = attachment.FileName,
                ContentType = attachment.ContentType,
                SizeBytes = bytes.LongLength,
                StorageKey = key,
                ChecksumSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(),
                Status = AttachmentStatus.Ready,
                Kind = AttachmentKind.File,
                ReferenceCount = 1,
                CreatedAt = message.CreatedAt,
                ReadyAt = clock.UtcNow
            });
            if (map is not null)
            {
                map.CanonicalId = id;
                map.Disposition = ImportDispositions.Published;
            }
        }
    }

    private async Task RemovePublishedAsync(TenantId tenantId, List<ImportIdMapRecord> maps, CancellationToken cancellationToken)
    {
        var attachmentIds = Ids(maps, ImportResourceTypes.Attachment, ImportDispositions.Published);
        var attachments = await db.Attachments.Where(x => attachmentIds.Contains(x.Id)).ToListAsync(cancellationToken);
        foreach (var attachment in attachments)
        {
            try
            {
                await storage.DeleteObjectAsync(attachment.StorageKey, cancellationToken);
            }
            catch
            {
                // Object cleanup is best-effort; the row removal is authoritative.
            }
        }

        db.Attachments.RemoveRange(attachments);

        var messageIds = Ids(maps, ImportResourceTypes.Message, ImportDispositions.Published).Select(x => new MessageId(x)).ToArray();
        var messages = await db.Messages.Where(x => messageIds.Contains(x.Id)).ToListAsync(cancellationToken);
        db.Messages.RemoveRange(messages);

        var threadIds = Ids(maps, ImportResourceTypes.Thread, ImportDispositions.Published);
        var threads = await db.MessageThreads.Where(x => threadIds.Contains(x.Id)).ToListAsync(cancellationToken);
        db.MessageThreads.RemoveRange(threads);

        var channelIds = Ids(maps, ImportResourceTypes.Channel, ImportDispositions.Published).Select(x => new ChannelId(x)).ToArray();
        var members = await db.ChannelMembers.Where(x => channelIds.Contains(x.ChannelId)).ToListAsync(cancellationToken);
        db.ChannelMembers.RemoveRange(members);
        var channels = await db.Channels.Where(x => channelIds.Contains(x.Id)).ToListAsync(cancellationToken);
        db.Channels.RemoveRange(channels);

        var spaceIds = Ids(maps, ImportResourceTypes.Space, ImportDispositions.Published);
        var spaces = await db.Spaces.Where(x => spaceIds.Contains(x.Id)).ToListAsync(cancellationToken);
        foreach (var space in spaces)
        {
            var stillUsed = await db.Channels.AnyAsync(x => x.SpaceId == space.Id && !channelIds.Contains(x.Id), cancellationToken);
            if (!stillUsed)
            {
                db.Spaces.Remove(space);
            }
        }

        var historical = await db.ImportHistoricalPrincipals.Where(x => maps.Select(m => m.ImportJobId).Contains(x.ImportJobId)).ToListAsync(cancellationToken);
        foreach (var principal in historical)
        {
            var member = await db.WorkspaceMembers.AnyAsync(x => x.UserId == principal.UserId, cancellationToken);
            if (member)
            {
                continue;
            }

            var profile = await db.UserProfiles.FirstOrDefaultAsync(x => x.Id == principal.UserId, cancellationToken);
            if (profile is not null && profile.Subject.StartsWith("import:", StringComparison.Ordinal))
            {
                db.UserProfiles.Remove(profile);
            }
        }

        db.ImportHistoricalPrincipals.RemoveRange(historical);
        _ = tenantId;
    }

    private static ImportIdMapRecord? Map(List<ImportIdMapRecord> maps, string type, string externalId) =>
        maps.FirstOrDefault(x => x.ResourceType == type && x.ExternalId == externalId);

    private static Guid[] Ids(List<ImportIdMapRecord> maps, string type, string disposition) =>
        maps.Where(x => x.ResourceType == type && x.Disposition == disposition && x.CanonicalId is not null)
            .Select(x => x.CanonicalId!.Value)
            .ToArray();

    private async Task<long> NextSequenceAsync(TenantId tenantId, ChannelId conversationId, CancellationToken cancellationToken)
    {
        if (sequencesSeen.TryGetValue(conversationId.Value, out var tracked))
        {
            tracked.LastSequence++;
            return tracked.LastSequence;
        }

        var sequence = db.ConversationSequences.Local.FirstOrDefault(x => x.TenantId == tenantId && x.ConversationId == conversationId)
            ?? await db.ConversationSequences.FirstOrDefaultAsync(
                x => x.TenantId == tenantId && x.ConversationId == conversationId,
                cancellationToken);
        if (sequence is null)
        {
            sequence = new ConversationSequence { TenantId = tenantId, ConversationId = conversationId, LastSequence = 0 };
            db.ConversationSequences.Add(sequence);
        }

        sequence.LastSequence++;
        sequencesSeen[conversationId.Value] = sequence;
        return sequence.LastSequence;
    }

    private static string HistoricalSubject(Guid jobId, string externalId)
    {
        var subject = $"import:{jobId:N}:{externalId}";
        return subject.Length <= 256 ? subject : $"import:{jobId:N}:{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(externalId))).ToLowerInvariant()}";
    }
}
