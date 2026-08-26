using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Messaging;
using VibeChat.SharedKernel;

namespace VibeChat.Infrastructure;

public sealed record PollVoterDto(Guid UserId, string DisplayName);

public sealed record PollOptionDto(
    Guid Id,
    string Text,
    int Position,
    int VoteCount,
    int Percent,
    bool VotedByMe,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<PollVoterDto>? Voters);

public sealed record PollDto(
    Guid Id,
    Guid MessageId,
    Guid ChannelId,
    string Question,
    bool AllowMultiple,
    bool Anonymous,
    DateTimeOffset? ClosesAt,
    DateTimeOffset? ClosedAt,
    int TotalVotes,
    bool CanVote,
    IReadOnlyList<PollOptionDto> Options);

public static class PollAggregator
{
    public static PollDto Build(
        Poll poll,
        IReadOnlyList<PollOption> options,
        IReadOnlyList<PollVote> votes,
        IReadOnlyDictionary<Guid, string> displayNames,
        UserId viewerId,
        bool canVote,
        bool includeVoters)
    {
        var total = votes.Count;
        var ordered = options.OrderBy(x => x.Position).ToArray();
        var optionDtos = ordered.Select(option =>
        {
            var optionVotes = votes.Where(v => v.OptionId == option.Id).ToArray();
            var count = optionVotes.Length;
            var percent = total == 0 ? 0 : (int)Math.Round(count * 100d / total, MidpointRounding.AwayFromZero);
            IReadOnlyList<PollVoterDto>? voters = null;
            if (includeVoters && !poll.Anonymous)
            {
                voters = optionVotes
                    .Select(v => new PollVoterDto(
                        v.UserId.Value,
                        displayNames.TryGetValue(v.UserId.Value, out var name) ? name : v.UserId.Value.ToString()))
                    .ToArray();
            }

            return new PollOptionDto(
                option.Id,
                option.Text,
                option.Position,
                count,
                percent,
                optionVotes.Any(v => v.UserId == viewerId),
                voters);
        }).ToArray();

        return new PollDto(
            poll.MessageId.Value,
            poll.MessageId.Value,
            poll.ChannelId.Value,
            poll.Question,
            poll.AllowMultiple,
            poll.Anonymous,
            poll.ClosesAt,
            poll.ClosedAt,
            total,
            canVote && poll.ClosedAt is null,
            optionDtos);
    }
}

public sealed class PollWriter(
    VibeChatDbContext dbContext,
    ITenantContext tenantContext,
    IConversationSequenceStore sequences,
    IIdempotencyStore idempotencyStore,
    IOutboxWriter outbox,
    IAuditWriter audit,
    IClock clock,
    IPermissionChecker permissions,
    IChannelMembershipReader channels) : IPollWriter
{
    public async Task<MessageSendResult> CreateAsync(CreatePollCommand command, CancellationToken cancellationToken)
    {
        tenantContext.SetTenant(command.TenantId);
        tenantContext.SetUser(command.UserId);
        await RlsSession.EnsureAppliedAsync(dbContext, tenantContext, cancellationToken);

        var channel = await dbContext.Channels.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == command.ChannelId && x.TenantId == command.TenantId, cancellationToken);
        if (channel is null
            || channel.Type is ChannelType.Direct or ChannelType.Group
            || !await channels.CanAccessAsync(command.TenantId, command.ChannelId, command.UserId, cancellationToken)
            || !await permissions.HasPermissionAsync(command.TenantId, command.UserId, Permissions.Message.Send, cancellationToken))
        {
            throw new UnauthorizedAccessException("User cannot create a poll in this channel.");
        }

        var question = PollPolicies.Normalize(command.Question);
        if (!PollPolicies.IsValidQuestion(question))
        {
            throw new ArgumentException("PollQuestionInvalid");
        }

        var options = (command.Options ?? [])
            .Select(PollPolicies.Normalize)
            .ToArray();
        if (!PollPolicies.IsValidOptionCount(options.Length) || options.Any(o => !PollPolicies.IsValidOption(o)))
        {
            throw new ArgumentException("PollOptionsInvalid");
        }

        if (command.ClosesAt is DateTimeOffset closesAt && closesAt <= clock.UtcNow)
        {
            throw new ArgumentException("PollClosesAtInPast");
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{command.MessageId}:{command.ChannelId}:{question}:{string.Join('|', options)}:{command.AllowMultiple}:{command.Anonymous}:{command.ClosesAt:O}")));
        var existing = await idempotencyStore.FindAsync(command.TenantId, command.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            var idempotentResult = JsonSerializer.Deserialize<MessageSendResult>(existing.ResultJson)!;
            return idempotentResult with { Idempotent = true };
        }

        var sequence = await sequences.NextAsync(command.TenantId, command.ChannelId, cancellationToken);
        var now = clock.UtcNow;
        var message = new Message
        {
            Id = command.MessageId,
            TenantId = command.TenantId,
            ConversationId = command.ChannelId,
            Sequence = sequence,
            AuthorId = command.UserId,
            Body = question,
            CreatedAt = now
        };
        dbContext.Messages.Add(message);

        var poll = new Poll
        {
            MessageId = command.MessageId,
            TenantId = command.TenantId,
            ChannelId = command.ChannelId,
            CreatedByUserId = command.UserId,
            Question = question,
            AllowMultiple = command.AllowMultiple,
            Anonymous = command.Anonymous,
            ClosesAt = command.ClosesAt
        };
        dbContext.Polls.Add(poll);

        var optionEntities = options.Select((text, index) => new PollOption
        {
            Id = Guid.NewGuid(),
            TenantId = command.TenantId,
            PollId = command.MessageId,
            Text = text,
            Position = index
        }).ToArray();
        dbContext.PollOptions.AddRange(optionEntities);

        var result = new MessageSendResult(message.Id, message.Sequence, message.CreatedAt, false);
        var authorName = await dbContext.UserProfiles.AsNoTracking()
            .Where(x => x.Id == command.UserId)
            .Select(x => x.DisplayName)
            .FirstOrDefaultAsync(cancellationToken) ?? command.UserId.Value.ToString();

        var snapshot = PollAggregator.Build(
            poll,
            optionEntities,
            [],
            new Dictionary<Guid, string>(),
            command.UserId,
            canVote: true,
            includeVoters: !poll.Anonymous);

        outbox.Add(new OutboxMessage
        {
            TenantId = command.TenantId,
            Type = nameof(MessageCreatedEvent),
            Payload = JsonSerializer.Serialize(new
            {
                tenantId = command.TenantId.Value,
                channelId = command.ChannelId.Value,
                conversationId = command.ChannelId.Value,
                messageId = command.MessageId.Value,
                clientMessageId = command.MessageId.Value,
                authorId = command.UserId.Value,
                authorName,
                sequence,
                body = question,
                createdAt = now,
                poll = snapshot
            })
        });

        audit.Add(new AuditEvent
        {
            TenantId = command.TenantId,
            ActorUserId = command.UserId,
            Action = AuditActions.PollCreate,
            EntityType = "Poll",
            EntityId = command.MessageId.ToString(),
            MetadataJson = JsonSerializer.Serialize(new
            {
                channelId = command.ChannelId.Value,
                optionCount = optionEntities.Length,
                allowMultiple = command.AllowMultiple,
                anonymous = command.Anonymous
            }),
            OccurredAt = now
        });

        await idempotencyStore.StoreAsync(
            new IdempotencyRecord(command.TenantId, command.IdempotencyKey, hash, JsonSerializer.Serialize(result), now),
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task VoteAsync(CastPollVoteCommand command, CancellationToken cancellationToken)
    {
        tenantContext.SetTenant(command.TenantId);
        tenantContext.SetUser(command.UserId);
        await RlsSession.EnsureAppliedAsync(dbContext, tenantContext, cancellationToken);

        var poll = await dbContext.Polls
            .FirstOrDefaultAsync(x => x.MessageId == command.PollId && x.TenantId == command.TenantId, cancellationToken)
            ?? throw new PollNotFoundException();

        if (poll.ClosedAt is not null)
        {
            throw new PollClosedException();
        }

        if (!await channels.CanAccessAsync(command.TenantId, poll.ChannelId, command.UserId, cancellationToken)
            || !await permissions.HasPermissionAsync(command.TenantId, command.UserId, Permissions.Message.Send, cancellationToken))
        {
            throw new UnauthorizedAccessException("User cannot vote on this poll.");
        }

        var optionIds = (command.OptionIds ?? []).Where(x => x != Guid.Empty).Distinct().ToArray();
        if (optionIds.Length == 0)
        {
            throw new ArgumentException("PollVoteEmpty");
        }

        if (!poll.AllowMultiple && optionIds.Length != 1)
        {
            throw new ArgumentException("PollVoteSingleRequired");
        }

        var options = await dbContext.PollOptions
            .Where(x => x.PollId == poll.MessageId && x.TenantId == command.TenantId)
            .ToListAsync(cancellationToken);
        if (optionIds.Any(id => options.All(o => o.Id != id)))
        {
            throw new ArgumentException("PollVoteUnknownOption");
        }

        var existing = await dbContext.PollVotes
            .Where(x => x.PollId == poll.MessageId && x.TenantId == command.TenantId && x.UserId == command.UserId)
            .ToListAsync(cancellationToken);
        if (existing.Count > 0)
        {
            dbContext.PollVotes.RemoveRange(existing);
        }

        var now = clock.UtcNow;
        dbContext.PollVotes.AddRange(optionIds.Select(optionId => new PollVote
        {
            Id = Guid.NewGuid(),
            TenantId = command.TenantId,
            PollId = poll.MessageId,
            OptionId = optionId,
            UserId = command.UserId,
            CreatedAt = now
        }));

        await PublishChangedAsync(poll, command.UserId, AuditActions.PollVote, now, new { optionCount = optionIds.Length }, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UnvoteAsync(TenantId tenantId, UserId userId, MessageId pollId, CancellationToken cancellationToken)
    {
        tenantContext.SetTenant(tenantId);
        tenantContext.SetUser(userId);
        await RlsSession.EnsureAppliedAsync(dbContext, tenantContext, cancellationToken);

        var poll = await dbContext.Polls
            .FirstOrDefaultAsync(x => x.MessageId == pollId && x.TenantId == tenantId, cancellationToken)
            ?? throw new PollNotFoundException();

        if (poll.ClosedAt is not null)
        {
            throw new PollClosedException();
        }

        if (!await channels.CanAccessAsync(tenantId, poll.ChannelId, userId, cancellationToken)
            || !await permissions.HasPermissionAsync(tenantId, userId, Permissions.Message.Send, cancellationToken))
        {
            throw new UnauthorizedAccessException("User cannot vote on this poll.");
        }

        var existing = await dbContext.PollVotes
            .Where(x => x.PollId == poll.MessageId && x.TenantId == tenantId && x.UserId == userId)
            .ToListAsync(cancellationToken);
        if (existing.Count > 0)
        {
            dbContext.PollVotes.RemoveRange(existing);
        }

        await PublishChangedAsync(poll, userId, AuditActions.PollUnvote, clock.UtcNow, new { }, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task CloseAsync(TenantId tenantId, UserId userId, MessageId pollId, bool asAdmin, CancellationToken cancellationToken)
    {
        tenantContext.SetTenant(tenantId);
        tenantContext.SetUser(userId);
        await RlsSession.EnsureAppliedAsync(dbContext, tenantContext, cancellationToken);

        var poll = await dbContext.Polls
            .FirstOrDefaultAsync(x => x.MessageId == pollId && x.TenantId == tenantId, cancellationToken)
            ?? throw new PollNotFoundException();

        if (poll.ClosedAt is not null)
        {
            return;
        }

        if (poll.CreatedByUserId != userId && !asAdmin)
        {
            throw new UnauthorizedAccessException("User cannot close this poll.");
        }

        if (!await channels.CanAccessAsync(tenantId, poll.ChannelId, userId, cancellationToken))
        {
            throw new UnauthorizedAccessException("User cannot close this poll.");
        }

        poll.ClosedAt = clock.UtcNow;
        await PublishChangedAsync(poll, userId, AuditActions.PollClose, poll.ClosedAt.Value, new { }, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task PublishChangedAsync(
        Poll poll,
        UserId actor,
        string auditAction,
        DateTimeOffset now,
        object metadata,
        CancellationToken cancellationToken)
    {
        var snapshot = await PollQuery.LoadAsync(dbContext, poll.MessageId, actor, includeVoters: !poll.Anonymous, canVote: true, cancellationToken);
        outbox.Add(new OutboxMessage
        {
            TenantId = poll.TenantId,
            Type = nameof(PollChangedEvent),
            Payload = JsonSerializer.Serialize(new
            {
                tenantId = poll.TenantId.Value,
                channelId = poll.ChannelId.Value,
                pollId = poll.MessageId.Value,
                messageId = poll.MessageId.Value,
                poll = snapshot
            })
        });
        audit.Add(new AuditEvent
        {
            TenantId = poll.TenantId,
            ActorUserId = actor,
            Action = auditAction,
            EntityType = "Poll",
            EntityId = poll.MessageId.ToString(),
            MetadataJson = JsonSerializer.Serialize(metadata),
            OccurredAt = now
        });
    }
}

public static class PollQuery
{
    public static async Task<Dictionary<Guid, PollDto>> LoadByMessageIdsAsync(
        VibeChatDbContext db,
        IReadOnlyCollection<MessageId> messageIds,
        UserId viewerId,
        bool canVote,
        bool includeVoters,
        CancellationToken cancellationToken)
    {
        if (messageIds.Count == 0)
        {
            return new Dictionary<Guid, PollDto>();
        }

        var wanted = messageIds.ToList();
        var polls = await db.Polls.AsNoTracking()
            .Where(x => wanted.Contains(x.MessageId))
            .ToListAsync(cancellationToken);
        if (polls.Count == 0)
        {
            return new Dictionary<Guid, PollDto>();
        }

        var pollIds = polls.Select(x => x.MessageId).ToList();
        var options = await db.PollOptions.AsNoTracking()
            .Where(x => pollIds.Contains(x.PollId))
            .ToListAsync(cancellationToken);
        var votes = await db.PollVotes.AsNoTracking()
            .Where(x => pollIds.Contains(x.PollId))
            .ToListAsync(cancellationToken);
        var voterIds = votes.Select(x => x.UserId).Distinct().ToList();
        var names = voterIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.UserProfiles.AsNoTracking()
                .Where(x => voterIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id.Value, x => x.DisplayName, cancellationToken);

        return polls.ToDictionary(
            poll => poll.MessageId.Value,
            poll => PollAggregator.Build(
                poll,
                options.Where(o => o.PollId == poll.MessageId).ToArray(),
                votes.Where(v => v.PollId == poll.MessageId).ToArray(),
                names,
                viewerId,
                canVote,
                includeVoters && !poll.Anonymous));
    }

    public static async Task<PollDto?> LoadAsync(
        VibeChatDbContext db,
        MessageId pollId,
        UserId viewerId,
        bool includeVoters,
        bool canVote,
        CancellationToken cancellationToken)
    {
        var map = await LoadByMessageIdsAsync(db, [pollId], viewerId, canVote, includeVoters, cancellationToken);
        return map.TryGetValue(pollId.Value, out var dto) ? dto : null;
    }
}

public sealed class PollCloseProcessor(
    IServiceScopeFactory scopeFactory,
    ILogger<PollCloseProcessor> logger)
{
    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VibeChatDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxWriter>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditWriter>();

        tenant.SetJobRole("polls");
        await RlsSession.EnsureAppliedAsync(db, tenant, cancellationToken);

        var now = clock.UtcNow;
        var due = await db.Polls.IgnoreQueryFilters()
            .Where(x => x.ClosedAt == null && x.ClosesAt != null && x.ClosesAt <= now)
            .OrderBy(x => x.ClosesAt)
            .Take(50)
            .ToListAsync(cancellationToken);
        if (due.Count == 0)
        {
            await RlsSession.CommitAsync(db, cancellationToken);
            return 0;
        }

        var closed = 0;
        foreach (var group in due.GroupBy(x => x.TenantId))
        {
            tenant.SetTenant(group.Key);
            await RlsSession.EnsureAppliedAsync(db, tenant, cancellationToken);

            foreach (var poll in group)
            {
                poll.ClosedAt = now;
                var snapshot = await PollQuery.LoadAsync(db, poll.MessageId, poll.CreatedByUserId, includeVoters: !poll.Anonymous, canVote: false, cancellationToken);
                outbox.Add(new OutboxMessage
                {
                    TenantId = poll.TenantId,
                    Type = nameof(PollChangedEvent),
                    Payload = JsonSerializer.Serialize(new
                    {
                        tenantId = poll.TenantId.Value,
                        channelId = poll.ChannelId.Value,
                        pollId = poll.MessageId.Value,
                        messageId = poll.MessageId.Value,
                        poll = snapshot
                    })
                });
                audit.Add(new AuditEvent
                {
                    TenantId = poll.TenantId,
                    ActorUserId = null,
                    Action = AuditActions.PollClose,
                    EntityType = "Poll",
                    EntityId = poll.MessageId.ToString(),
                    MetadataJson = JsonSerializer.Serialize(new { pollId = poll.MessageId.Value, actor = "system" }),
                    OccurredAt = now
                });
                closed++;
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        await RlsSession.CommitAsync(db, cancellationToken);
        logger.LogInformation("Closed {Count} expired polls", closed);
        return closed;
    }
}

public sealed class PollCloseDispatcher(
    PollCloseProcessor processor,
    ILogger<PollCloseDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await processor.ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Poll close batch failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
