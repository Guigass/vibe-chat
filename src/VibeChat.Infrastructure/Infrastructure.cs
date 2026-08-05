using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
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
using VibeChat.Search;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using Role = VibeChat.SharedKernel.Role;

namespace VibeChat.Infrastructure;

public static class VibeChatMetrics
{
    public const string MeterName = "VibeChat";
    public static readonly Meter Meter = new(MeterName, "1.0.0");
    public static readonly Counter<long> MessagesSent = Meter.CreateCounter<long>("vibechat.messages.sent");
    public static readonly Counter<long> MessagesRejected = Meter.CreateCounter<long>("vibechat.messages.rejected");
    public static readonly UpDownCounter<long> RealtimeConnections = Meter.CreateUpDownCounter<long>("vibechat.realtime.connections");
    private static long _realtimeConnectionsGauge;
    public static long RealtimeConnectionsGauge => Interlocked.Read(ref _realtimeConnectionsGauge);

    public static void AdjustRealtimeConnections(long delta)
    {
        RealtimeConnections.Add(delta);
        Interlocked.Add(ref _realtimeConnectionsGauge, delta);
    }
}

public sealed class VibeChatDbContext(DbContextOptions<VibeChatDbContext> options, ITenantContext tenantContext) : DbContext(options)
{
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<WorkspaceMember> WorkspaceMembers => Set<WorkspaceMember>();
    public DbSet<Space> Spaces => Set<Space>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<ChannelMember> ChannelMembers => Set<ChannelMember>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<MessageThread> MessageThreads => Set<MessageThread>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<Reaction> Reactions => Set<Reaction>();
    public DbSet<ReadCursor> ReadCursors => Set<ReadCursor>();
    public DbSet<ConversationSequence> ConversationSequences => Set<ConversationSequence>();
    public DbSet<IdempotencyEntry> IdempotencyEntries => Set<IdempotencyEntry>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<AiUsageRecord> AiUsageRecords => Set<AiUsageRecord>();
    public DbSet<AiSettings> AiSettings => Set<AiSettings>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();
    public DbSet<TenantEmailSettings> TenantEmailSettings => Set<TenantEmailSettings>();
    public DbSet<OutboundWebhookEndpoint> OutboundWebhookEndpoints => Set<OutboundWebhookEndpoint>();
    public DbSet<MessageRetentionSettings> MessageRetentionSettings => Set<MessageRetentionSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureShared(modelBuilder);

        modelBuilder.Entity<UserProfile>(entity =>
        {
            entity.ToTable("user_profiles", "identity");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.Subject).HasMaxLength(256);
            entity.Property(x => x.Email).HasMaxLength(256);
            entity.HasIndex(x => x.Subject).IsUnique();
            entity.HasIndex(x => x.Email);
        });

        modelBuilder.Entity<Workspace>(entity =>
        {
            entity.ToTable("workspaces", "tenancy");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.Name).HasMaxLength(160);
            entity.Property(x => x.Slug).HasMaxLength(120);
            entity.HasIndex(x => x.Slug).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<WorkspaceMember>(entity =>
        {
            entity.ToTable("workspace_members", "tenancy");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.UserId).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.Role).HasConversion<string>().HasMaxLength(40);
            entity.HasIndex(x => new { x.TenantId, x.UserId });
            entity.HasIndex(x => new { x.WorkspaceId, x.UserId }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<Space>(entity =>
        {
            entity.ToTable("spaces", "directory");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.Name).HasMaxLength(120);
            entity.HasIndex(x => new { x.WorkspaceId, x.Order });
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<Channel>(entity =>
        {
            entity.ToTable("channels", "conversations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasConversion(v => v.Value, v => new ChannelId(v));
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.CreatedBy).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.Name).HasMaxLength(120);
            entity.HasIndex(x => new { x.WorkspaceId, x.Name }).IsUnique();
            entity.HasIndex(x => x.SpaceId);
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<ChannelMember>(entity =>
        {
            entity.ToTable("channel_members", "conversations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.ChannelId).HasConversion(v => v.Value, v => new ChannelId(v));
            entity.Property(x => x.UserId).HasConversion(v => v.Value, v => new UserId(v));
            entity.HasIndex(x => new { x.ChannelId, x.UserId }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<Message>(entity =>
        {
            entity.ToTable("messages", "messaging");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasConversion(v => v.Value, v => new MessageId(v));
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.ConversationId).HasConversion(v => v.Value, v => new ChannelId(v));
            entity.Property(x => x.AuthorId).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.DeletedBy).HasConversion(v => v.HasValue ? v.Value.Value : (Guid?)null, v => v.HasValue ? new UserId(v.Value) : null);
            entity.Property(x => x.ReplyToMessageId).HasConversion(v => v.HasValue ? v.Value.Value : (Guid?)null, v => v.HasValue ? new MessageId(v.Value) : null);
            entity.Property(x => x.Body).HasMaxLength(8000);
            entity.HasIndex(x => new { x.ConversationId, x.Sequence }).IsUnique();
            entity.HasIndex(x => x.ThreadId);
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<MessageThread>(entity =>
        {
            entity.ToTable("threads", "messaging");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.ChannelId).HasConversion(v => v.Value, v => new ChannelId(v));
            entity.Property(x => x.ParentMessageId).HasConversion(v => v.Value, v => new MessageId(v));
            entity.Property(x => x.CreatedBy).HasConversion(v => v.Value, v => new UserId(v));
            entity.HasIndex(x => new { x.TenantId, x.ParentMessageId }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.ChannelId });
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<Attachment>(entity =>
        {
            entity.ToTable("attachments", "files");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.ChannelId).HasConversion(v => v.Value, v => new ChannelId(v));
            entity.Property(x => x.MessageId).HasConversion(v => v.HasValue ? v.Value.Value : (Guid?)null, v => v.HasValue ? new MessageId(v.Value) : null);
            entity.Property(x => x.UploadedBy).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.FileName).HasMaxLength(AttachmentPolicies.MaxFileNameLength);
            entity.Property(x => x.ContentType).HasMaxLength(160);
            entity.Property(x => x.StorageKey).HasMaxLength(512);
            entity.Property(x => x.ChecksumSha256).HasMaxLength(96);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(x => x.StorageKey).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.ChannelId, x.MessageId });
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<Reaction>(entity =>
        {
            entity.ToTable("reactions", "messaging");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.MessageId).HasConversion(v => v.Value, v => new MessageId(v));
            entity.Property(x => x.UserId).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.Emoji).HasMaxLength(32);
            entity.HasIndex(x => new { x.TenantId, x.MessageId, x.UserId, x.Emoji }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<ReadCursor>(entity =>
        {
            entity.ToTable("read_cursors", "messaging");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.ChannelId).HasConversion(v => v.Value, v => new ChannelId(v));
            entity.Property(x => x.UserId).HasConversion(v => v.Value, v => new UserId(v));
            entity.HasIndex(x => new { x.ChannelId, x.UserId }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<ConversationSequence>(entity =>
        {
            entity.ToTable("conversation_sequences", "messaging");
            entity.HasKey(x => new { x.TenantId, x.ConversationId });
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.ConversationId).HasConversion(v => v.Value, v => new ChannelId(v));
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<IdempotencyEntry>(entity =>
        {
            entity.ToTable("idempotency", "messaging");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.Key).HasMaxLength(200);
            entity.Property(x => x.RequestHash).HasMaxLength(96);
            entity.HasIndex(x => new { x.TenantId, x.Key }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("outbox_messages", "building_blocks");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.Type).HasMaxLength(200);
            entity.HasIndex(x => new { x.ProcessedAt, x.OccurredAt });
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.ToTable("audit_events", "audit");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.ActorUserId).HasConversion(v => v.HasValue ? v.Value.Value : (Guid?)null, v => v.HasValue ? new UserId(v.Value) : null);
            entity.Property(x => x.Action).HasMaxLength(100);
            entity.Property(x => x.EntityType).HasMaxLength(100);
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<AiUsageRecord>(entity =>
        {
            entity.ToTable("usage_records", "ai");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.Provider).HasMaxLength(80);
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<AiSettings>(entity =>
        {
            entity.ToTable("settings", "ai");
            entity.HasKey(x => x.WorkspaceId);
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.Provider).HasMaxLength(60);
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<NotificationPreference>(entity =>
        {
            entity.ToTable("preferences", "notifications");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.UserId).HasConversion(v => v.Value, v => new UserId(v));
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<TenantEmailSettings>(entity =>
        {
            entity.ToTable("email_settings", "notifications");
            entity.HasKey(x => x.TenantId);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.Host).HasMaxLength(256);
            entity.Property(x => x.Username).HasMaxLength(256);
            entity.Property(x => x.From).HasMaxLength(320);
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<OutboundWebhookEndpoint>(entity =>
        {
            entity.ToTable("webhook_endpoints", "integrations");
            entity.HasKey(x => x.TenantId);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.Url).HasMaxLength(2048);
            entity.Property(x => x.Secret).HasMaxLength(512);
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<MessageRetentionSettings>(entity =>
        {
            entity.ToTable("message_retention_settings", "messaging");
            entity.HasKey(x => x.TenantId);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });
    }

    private static void ConfigureShared(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;
            if (typeof(Entity).IsAssignableFrom(clrType))
            {
                modelBuilder.Entity(clrType).Ignore(nameof(Entity.DomainEvents));
            }
        }
    }
}

public sealed class VibeChatDbContextFactory : IDesignTimeDbContextFactory<VibeChatDbContext>
{
    public VibeChatDbContext CreateDbContext(string[] args)
    {
        var connection =
            Environment.GetEnvironmentVariable("ConnectionStrings__DatabaseMigrator")
            ?? Environment.GetEnvironmentVariable("DATABASE_MIGRATOR_URL")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__Database")
            ?? "Host=localhost;Port=5432;Database=vibechat;Username=vibechat_migrator;Password=vibechat_migrator_password_change_me";

        var options = new DbContextOptionsBuilder<VibeChatDbContext>()
            .UseNpgsql(connection)
            .Options;

        return new VibeChatDbContext(options, new TenantContext());
    }
}

public sealed class EfOutboxWriter(VibeChatDbContext dbContext, IClock clock) : IOutboxWriter
{
    public void Add(OutboxMessage message)
    {
        message.Id = message.Id == Guid.Empty ? Guid.NewGuid() : message.Id;
        message.OccurredAt = message.OccurredAt == default ? clock.UtcNow : message.OccurredAt;
        dbContext.OutboxMessages.Add(message);
    }
}

public sealed class EfAuditWriter(VibeChatDbContext dbContext, IClock clock) : IAuditWriter
{
    public void Add(AuditEvent auditEvent)
    {
        auditEvent.Id = auditEvent.Id == Guid.Empty ? Guid.NewGuid() : auditEvent.Id;
        auditEvent.OccurredAt = auditEvent.OccurredAt == default ? clock.UtcNow : auditEvent.OccurredAt;
        dbContext.AuditEvents.Add(auditEvent);
    }
}

public sealed class EfIdempotencyStore(VibeChatDbContext dbContext, IClock clock) : IIdempotencyStore
{
    public async Task<IdempotencyRecord?> FindAsync(TenantId tenantId, string key, CancellationToken cancellationToken)
    {
        var entry = await dbContext.IdempotencyEntries.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Key == key, cancellationToken);

        return entry is null
            ? null
            : new IdempotencyRecord(entry.TenantId, entry.Key, entry.RequestHash, entry.ResultJson, entry.CreatedAt);
    }

    public Task StoreAsync(IdempotencyRecord record, CancellationToken cancellationToken)
    {
        dbContext.IdempotencyEntries.Add(new IdempotencyEntry
        {
            Id = Guid.NewGuid(),
            TenantId = record.TenantId,
            Key = record.Key,
            RequestHash = record.RequestHash,
            ResultJson = record.ResultJson,
            CreatedAt = record.CreatedAt == default ? clock.UtcNow : record.CreatedAt
        });

        return Task.CompletedTask;
    }
}

public sealed class PermissionChecker(VibeChatDbContext dbContext) : IPermissionChecker, IWorkspaceMembershipReader, IChannelMembershipReader
{
    public async Task<bool> HasPermissionAsync(TenantId tenantId, UserId userId, string permission, CancellationToken cancellationToken)
    {
        var roles = await GetRolesAsync(tenantId, userId, cancellationToken);
        return roles.Any(role => RolePermissionCatalog.For(role).Contains(permission));
    }

    public async Task<bool> IsMemberAsync(TenantId tenantId, WorkspaceId workspaceId, UserId userId, CancellationToken cancellationToken) =>
        await dbContext.WorkspaceMembers.AnyAsync(x => x.TenantId == tenantId && x.WorkspaceId == workspaceId && x.UserId == userId, cancellationToken);

    public async Task<IReadOnlyCollection<Role>> GetRolesAsync(TenantId tenantId, UserId userId, CancellationToken cancellationToken) =>
        await dbContext.WorkspaceMembers.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.UserId == userId)
            .Select(x => x.Role)
            .Distinct()
            .ToArrayAsync(cancellationToken);

    public async Task<bool> CanAccessAsync(TenantId tenantId, ChannelId channelId, UserId userId, CancellationToken cancellationToken)
    {
        var channel = await dbContext.Channels.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == channelId, cancellationToken);
        if (channel is null)
        {
            return false;
        }

        if (channel.Type == ChannelType.Public || channel.Type == ChannelType.Announcement)
        {
            return await IsMemberAsync(tenantId, channel.WorkspaceId, userId, cancellationToken);
        }

        return await dbContext.ChannelMembers.AnyAsync(x => x.TenantId == tenantId && x.ChannelId == channelId && x.UserId == userId, cancellationToken);
    }

}

public sealed class ConversationSequenceStore(VibeChatDbContext dbContext) : IConversationSequenceStore
{
    public async Task<long> NextAsync(TenantId tenantId, ChannelId conversationId, CancellationToken cancellationToken)
    {
        var sequence = await dbContext.ConversationSequences
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.ConversationId == conversationId, cancellationToken);

        if (sequence is null)
        {
            sequence = new ConversationSequence { TenantId = tenantId, ConversationId = conversationId, LastSequence = 1 };
            dbContext.ConversationSequences.Add(sequence);
            return 1;
        }

        sequence.LastSequence++;
        return sequence.LastSequence;
    }
}

public sealed class MessageWriter(
    VibeChatDbContext dbContext,
    ITenantContext tenantContext,
    IConversationSequenceStore sequences,
    IIdempotencyStore idempotencyStore,
    IOutboxWriter outbox,
    IAuditWriter audit,
    IClock clock,
    IPermissionChecker permissions,
    IChannelMembershipReader channels) : IMessageWriter
{
    public async Task<MessageSendResult> SendAsync(SendMessageCommand command, CancellationToken cancellationToken)
    {
        tenantContext.SetTenant(command.TenantId);
        tenantContext.SetUser(command.UserId);
        await RlsSession.EnsureAppliedAsync(dbContext, tenantContext, cancellationToken);

        var parentChannelId = command.ChannelId;
        var conversationId = command.ChannelId;
        Guid? threadId = command.ThreadId;

        if (threadId is Guid resolvedThreadId)
        {
            var thread = await dbContext.MessageThreads.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == resolvedThreadId && x.TenantId == command.TenantId, cancellationToken);
            if (thread is null)
            {
                VibeChatMetrics.MessagesRejected.Add(1);
                throw new InvalidOperationException("Thread not found.");
            }

            parentChannelId = thread.ChannelId;
            conversationId = new ChannelId(thread.Id);
            if (command.ChannelId != ChannelId.Empty && command.ChannelId != parentChannelId)
            {
                VibeChatMetrics.MessagesRejected.Add(1);
                throw new UnauthorizedAccessException("Thread does not belong to the requested channel.");
            }
        }

        if (!await channels.CanAccessAsync(command.TenantId, parentChannelId, command.UserId, cancellationToken)
            || !await permissions.HasPermissionAsync(command.TenantId, command.UserId, Permissions.Message.Send, cancellationToken))
        {
            VibeChatMetrics.MessagesRejected.Add(1);
            throw new UnauthorizedAccessException("User cannot send messages to this channel.");
        }

        var attachmentIds = (command.AttachmentIds ?? [])
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToArray();
        var body = command.Body?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(body) && attachmentIds.Length == 0)
        {
            throw new ArgumentException("Message body or attachments are required.");
        }

        var normalized = command with
        {
            ChannelId = parentChannelId,
            ThreadId = threadId,
            Body = body,
            AttachmentIds = attachmentIds
        };
        var hash = MessageIdempotency.ComputeRequestHash(normalized);
        var existing = await idempotencyStore.FindAsync(command.TenantId, command.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            var idempotentResult = JsonSerializer.Deserialize<MessageSendResult>(existing.ResultJson)!;
            return idempotentResult with { Idempotent = true };
        }

        Attachment[] attachments = [];
        if (attachmentIds.Length > 0)
        {
            attachments = await dbContext.Attachments
                .Where(x => attachmentIds.Contains(x.Id)
                    && x.TenantId == command.TenantId
                    && x.ChannelId == parentChannelId
                    && x.UploadedBy == command.UserId
                    && x.Status == AttachmentStatus.Ready
                    && x.MessageId == null)
                .ToArrayAsync(cancellationToken);

            if (attachments.Length != attachmentIds.Length)
            {
                throw new InvalidOperationException("One or more attachments are invalid or not ready.");
            }
        }

        var sequence = await sequences.NextAsync(command.TenantId, conversationId, cancellationToken);
        var now = clock.UtcNow;

        var message = new Message
        {
            Id = command.MessageId,
            TenantId = command.TenantId,
            ConversationId = conversationId,
            Sequence = sequence,
            AuthorId = command.UserId,
            Body = body,
            ReplyToMessageId = command.ReplyToMessageId,
            ThreadId = threadId,
            CreatedAt = now
        };

        dbContext.Messages.Add(message);
        foreach (var attachment in attachments)
        {
            attachment.MessageId = message.Id;
        }

        var result = new MessageSendResult(message.Id, message.Sequence, message.CreatedAt, false);

        var authorName = await dbContext.UserProfiles.AsNoTracking()
            .Where(x => x.Id == command.UserId)
            .Select(x => x.DisplayName)
            .FirstOrDefaultAsync(cancellationToken) ?? command.UserId.Value.ToString();

        outbox.Add(new OutboxMessage
        {
            TenantId = command.TenantId,
            Type = nameof(MessageCreatedEvent),
            Payload = JsonSerializer.Serialize(new
            {
                tenantId = command.TenantId.Value,
                channelId = parentChannelId.Value,
                conversationId = conversationId.Value,
                threadId,
                messageId = command.MessageId.Value,
                replyToMessageId = command.ReplyToMessageId?.Value,
                authorId = command.UserId.Value,
                authorName,
                sequence,
                body,
                createdAt = now,
                attachments = attachments.Select(x => new
                {
                    id = x.Id,
                    fileName = x.FileName,
                    contentType = x.ContentType,
                    sizeBytes = x.SizeBytes
                })
            })
        });

        audit.Add(new AuditEvent
        {
            TenantId = command.TenantId,
            ActorUserId = command.UserId,
            Action = AuditActions.MessageSend,
            EntityType = "Message",
            EntityId = command.MessageId.ToString(),
            MetadataJson = JsonSerializer.Serialize(new
            {
                channelId = parentChannelId.Value,
                threadId,
                sequence,
                bodyHash = ComputeHash(body),
                attachmentIds
            })
        });

        await idempotencyStore.StoreAsync(new IdempotencyRecord(command.TenantId, command.IdempotencyKey, hash, JsonSerializer.Serialize(result), now), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        // RLS txn + SET LOCAL are committed by HTTP middleware / hub filter / worker batch Commit.
        VibeChatMetrics.MessagesSent.Add(1);
        return result;
    }

    private static string ComputeHash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public sealed class DashboardQuery(VibeChatDbContext dbContext) : IDashboardQuery
{
    public async Task<DashboardStats> GetStatsAsync(CancellationToken cancellationToken)
    {
        var workspaces = await dbContext.Workspaces.IgnoreQueryFilters().CountAsync(cancellationToken);
        var users = await dbContext.UserProfiles.CountAsync(cancellationToken);
        var channels = await dbContext.Channels.IgnoreQueryFilters().CountAsync(cancellationToken);
        var messages = await dbContext.Messages.IgnoreQueryFilters().CountAsync(cancellationToken);
        var outbox = await dbContext.OutboxMessages.IgnoreQueryFilters().CountAsync(x => x.ProcessedAt == null, cancellationToken);
        return new DashboardStats(workspaces, users, channels, messages, outbox);
    }
}

public sealed class SummarizeChannelFeature(
    VibeChatDbContext dbContext,
    IAiCompletionProvider provider,
    IClock clock,
    IConfiguration configuration) : ISummarizeChannelFeature
{
    public async Task<SummarizeChannelResult> SummarizeAsync(TenantId tenantId, WorkspaceId workspaceId, ChannelId channelId, CancellationToken cancellationToken)
    {
        // D-06 / ADR-012: external AI and summarize stay off unless explicitly enabled.
        if (!configuration.GetValue("Ai:Enabled", false) || provider is NullAiProvider)
        {
            return new SummarizeChannelResult(false, "AI is disabled.", "AiDisabled");
        }

        var settings = await dbContext.AiSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.WorkspaceId == workspaceId, cancellationToken);

        if (settings is null || !settings.Enabled)
        {
            return new SummarizeChannelResult(false, "AI is disabled for this workspace.", "AiDisabled");
        }

        var recent = await dbContext.Messages.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.ConversationId == channelId && x.DeletedAt == null)
            .OrderByDescending(x => x.Sequence)
            .Take(20)
            .OrderBy(x => x.Sequence)
            .Select(x => new { x.Sequence, x.Body })
            .ToArrayAsync(cancellationToken);

        var prompt = string.Join('\n', recent.Select(x => $"#{x.Sequence}: {x.Body}"));
        var response = await provider.CompleteAsync(
            new AiCompletionRequest("Summarize recent channel messages without exposing sensitive details.", prompt),
            cancellationToken);

        if (string.Equals(provider.Name, "OpenRouter", StringComparison.OrdinalIgnoreCase)
            && response.Text.Contains("provider is unavailable", StringComparison.OrdinalIgnoreCase))
        {
            return new SummarizeChannelResult(false, response.Text, "ProviderError");
        }

        dbContext.AiUsageRecords.Add(new AiUsageRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            WorkspaceId = workspaceId,
            Provider = provider.Name,
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

public sealed class SuggestChannelReplyFeature(
    VibeChatDbContext dbContext,
    IAiCompletionProvider provider,
    IClock clock,
    IConfiguration configuration) : ISuggestChannelReplyFeature
{
    public async Task<SuggestChannelReplyResult> SuggestAsync(TenantId tenantId, WorkspaceId workspaceId, ChannelId channelId, CancellationToken cancellationToken)
    {
        // D-06 / ADR-012: suggest-reply stays off unless explicitly enabled; never on SendMessage hot path.
        if (!configuration.GetValue("Ai:Enabled", false) || provider is NullAiProvider)
        {
            return new SuggestChannelReplyResult(false, "AI is disabled.", "AiDisabled");
        }

        var settings = await dbContext.AiSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.WorkspaceId == workspaceId, cancellationToken);

        if (settings is null || !settings.Enabled)
        {
            return new SuggestChannelReplyResult(false, "AI is disabled for this workspace.", "AiDisabled");
        }

        var recent = await dbContext.Messages.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.ConversationId == channelId && x.DeletedAt == null)
            .OrderByDescending(x => x.Sequence)
            .Take(20)
            .OrderBy(x => x.Sequence)
            .Select(x => new { x.Sequence, x.Body })
            .ToArrayAsync(cancellationToken);

        var prompt = string.Join('\n', recent.Select(x => $"#{x.Sequence}: {x.Body}"));
        var response = await provider.CompleteAsync(
            new AiCompletionRequest(
                "Suggest one short, professional reply to the recent channel messages without exposing sensitive details.",
                prompt),
            cancellationToken);

        if (string.Equals(provider.Name, "OpenRouter", StringComparison.OrdinalIgnoreCase)
            && response.Text.Contains("provider is unavailable", StringComparison.OrdinalIgnoreCase))
        {
            return new SuggestChannelReplyResult(false, response.Text, "ProviderError");
        }

        dbContext.AiUsageRecords.Add(new AiUsageRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            WorkspaceId = workspaceId,
            Provider = provider.Name,
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

public sealed class RedisConnection : IAsyncDisposable
{
    private readonly string? _connectionString;
    private readonly Lazy<Task<IConnectionMultiplexer?>> _connection;

    public RedisConnection(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Redis");
        _connection = new Lazy<Task<IConnectionMultiplexer?>>(ConnectAsync);
    }

    public async Task<IDatabase?> GetDatabaseAsync()
    {
        var connection = await _connection.Value;
        return connection?.GetDatabase();
    }

    public async Task<ISubscriber?> GetSubscriberAsync()
    {
        var connection = await _connection.Value;
        return connection?.GetSubscriber();
    }

    private async Task<IConnectionMultiplexer?> ConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            return null;
        }

        return await ConnectionMultiplexer.ConnectAsync(_connectionString);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection.IsValueCreated && await _connection.Value is { } connection)
        {
            await connection.CloseAsync();
            connection.Dispose();
        }
    }
}

public sealed class TypingService(RedisConnection redis, IClock clock) : ITypingService
{
    public async Task SetTypingAsync(TenantId tenantId, ChannelId channelId, UserId userId, string displayName, CancellationToken cancellationToken)
    {
        var db = await redis.GetDatabaseAsync();
        if (db is null)
        {
            return;
        }

        var key = Key(tenantId, channelId);
        var expiresAt = clock.UtcNow.AddSeconds(5);
        await db.HashSetAsync(key, userId.ToString(), JsonSerializer.Serialize(new TypingUser(userId, displayName, expiresAt)));
        await db.KeyExpireAsync(key, TimeSpan.FromSeconds(10));
    }

    public async Task<IReadOnlyCollection<TypingUser>> GetTypingAsync(TenantId tenantId, ChannelId channelId, CancellationToken cancellationToken)
    {
        var db = await redis.GetDatabaseAsync();
        if (db is null)
        {
            return [];
        }

        var entries = await db.HashGetAllAsync(Key(tenantId, channelId));
        var now = clock.UtcNow;
        return entries
            .Select(x => JsonSerializer.Deserialize<TypingUser>(x.Value.ToString()))
            .Where(x => x is not null && x.ExpiresAt > now)
            .Cast<TypingUser>()
            .ToArray();
    }

    private static string Key(TenantId tenantId, ChannelId channelId) => RedisKeys.Typing(tenantId, channelId);
}

/// <summary>
/// Redis key helpers — always tenant-first (<c>t:{tenantId}:…</c>) per multi-tenant.md.
/// </summary>
public static class RedisKeys
{
    public static string Typing(TenantId tenantId, ChannelId channelId) =>
        $"t:{tenantId.Value}:typing:{channelId.Value}";

    public static string PresenceStatus(TenantId tenantId, UserId userId) =>
        $"t:{tenantId.Value}:presence:status:{userId.Value}";

    public static string PresenceConnections(TenantId tenantId, UserId userId) =>
        $"t:{tenantId.Value}:presence:conn:{userId.Value}";

    public static string PresenceUsers(TenantId tenantId) =>
        $"t:{tenantId.Value}:presence:users";
}

public sealed class PresenceService(RedisConnection redis, IClock clock) : IPresenceService
{
    private static readonly TimeSpan PresenceTtl = TimeSpan.FromSeconds(45);

    public Task SetOnlineAsync(TenantId tenantId, UserId userId, string connectionId, CancellationToken cancellationToken) =>
        SetStatusAsync(tenantId, userId, connectionId, PresenceStatus.Online, cancellationToken);

    public Task SetAwayAsync(TenantId tenantId, UserId userId, string connectionId, CancellationToken cancellationToken) =>
        SetStatusAsync(tenantId, userId, connectionId, PresenceStatus.Away, cancellationToken);

    public Task HeartbeatAsync(TenantId tenantId, UserId userId, string connectionId, CancellationToken cancellationToken) =>
        SetStatusAsync(tenantId, userId, connectionId, PresenceStatus.Online, cancellationToken);

    public async Task SetOfflineAsync(TenantId tenantId, UserId userId, string connectionId, CancellationToken cancellationToken)
    {
        var db = await redis.GetDatabaseAsync();
        if (db is null)
        {
            return;
        }

        await db.SetRemoveAsync(ConnectionsKey(tenantId, userId), connectionId);
        var remaining = await db.SetLengthAsync(ConnectionsKey(tenantId, userId));
        if (remaining == 0)
        {
            await db.KeyDeleteAsync(StatusKey(tenantId, userId));
            await db.SetRemoveAsync(UsersKey(tenantId), userId.Value.ToString());
        }
    }

    public async Task<int> CountOnlineAsync(TenantId tenantId, CancellationToken cancellationToken)
    {
        var db = await redis.GetDatabaseAsync();
        if (db is null)
        {
            return 0;
        }

        var userIds = await db.SetMembersAsync(UsersKey(tenantId));
        var count = 0;
        foreach (var entry in userIds)
        {
            if (!Guid.TryParse(entry.ToString(), out var userGuid))
            {
                continue;
            }

            var status = await ReadStatusAsync(db, tenantId, new UserId(userGuid));
            if (status is PresenceStatus.Online or PresenceStatus.Away)
            {
                count++;
            }
        }

        return count;
    }

    public async Task<IReadOnlyDictionary<UserId, PresenceStatus>> GetStatusesAsync(
        TenantId tenantId,
        IReadOnlyCollection<UserId> userIds,
        CancellationToken cancellationToken)
    {
        var db = await redis.GetDatabaseAsync();
        var result = new Dictionary<UserId, PresenceStatus>();
        if (db is null)
        {
            foreach (var userId in userIds)
            {
                result[userId] = PresenceStatus.Offline;
            }

            return result;
        }

        foreach (var userId in userIds)
        {
            result[userId] = await ReadStatusAsync(db, tenantId, userId) ?? PresenceStatus.Offline;
        }

        return result;
    }

    private async Task SetStatusAsync(
        TenantId tenantId,
        UserId userId,
        string connectionId,
        PresenceStatus status,
        CancellationToken cancellationToken)
    {
        var db = await redis.GetDatabaseAsync();
        if (db is null)
        {
            return;
        }

        var expiresAt = clock.UtcNow.Add(PresenceTtl);
        var payload = JsonSerializer.Serialize(new PresenceEntry(userId, status, expiresAt));
        await db.StringSetAsync(StatusKey(tenantId, userId), payload, PresenceTtl);
        await db.SetAddAsync(ConnectionsKey(tenantId, userId), connectionId);
        await db.KeyExpireAsync(ConnectionsKey(tenantId, userId), PresenceTtl);
        await db.SetAddAsync(UsersKey(tenantId), userId.Value.ToString());
        await db.KeyExpireAsync(UsersKey(tenantId), PresenceTtl.Add(TimeSpan.FromMinutes(5)));
    }

    private async Task<PresenceStatus?> ReadStatusAsync(IDatabase db, TenantId tenantId, UserId userId)
    {
        var raw = await db.StringGetAsync(StatusKey(tenantId, userId));
        if (raw.IsNullOrEmpty)
        {
            return null;
        }

        var entry = JsonSerializer.Deserialize<PresenceEntry>(raw.ToString());
        if (entry is null || entry.ExpiresAt <= clock.UtcNow)
        {
            return null;
        }

        return entry.Status;
    }

    private static string StatusKey(TenantId tenantId, UserId userId) => RedisKeys.PresenceStatus(tenantId, userId);
    private static string ConnectionsKey(TenantId tenantId, UserId userId) => RedisKeys.PresenceConnections(tenantId, userId);
    private static string UsersKey(TenantId tenantId) => RedisKeys.PresenceUsers(tenantId);
}

public sealed class MinioObjectStorage(IMinioClient minioClient, IConfiguration configuration) : IObjectStorage
{
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        var bucket = Bucket();
        try
        {
            return await minioClient.BucketExistsAsync(new Minio.DataModel.Args.BucketExistsArgs().WithBucket(bucket), cancellationToken);
        }
        catch
        {
            return false;
        }
    }

    public async Task<PresignedUpload> CreateUploadUrlAsync(string storageKey, string contentType, TimeSpan ttl, CancellationToken cancellationToken)
    {
        _ = contentType;
        var expiry = Math.Clamp((int)ttl.TotalSeconds, 60, 3600);
        var url = await minioClient.PresignedPutObjectAsync(new Minio.DataModel.Args.PresignedPutObjectArgs()
            .WithBucket(Bucket())
            .WithObject(storageKey)
            .WithExpiry(expiry));
        // Keep required headers empty — signed PUT URLs reject unsigned Content-Type headers.
        return new PresignedUpload(
            new Uri(RewritePublicUrl(url)),
            DateTimeOffset.UtcNow.AddSeconds(expiry),
            new Dictionary<string, string>());
    }

    public async Task<PresignedDownload> CreateDownloadUrlAsync(string storageKey, string fileName, TimeSpan ttl, CancellationToken cancellationToken)
    {
        _ = fileName;
        var expiry = Math.Clamp((int)ttl.TotalSeconds, 60, 3600);
        var url = await minioClient.PresignedGetObjectAsync(new Minio.DataModel.Args.PresignedGetObjectArgs()
            .WithBucket(Bucket())
            .WithObject(storageKey)
            .WithExpiry(expiry));
        return new PresignedDownload(new Uri(RewritePublicUrl(url)), DateTimeOffset.UtcNow.AddSeconds(expiry));
    }

    public async Task<ObjectStat?> StatObjectAsync(string storageKey, CancellationToken cancellationToken)
    {
        try
        {
            var stat = await minioClient.StatObjectAsync(new Minio.DataModel.Args.StatObjectArgs()
                .WithBucket(Bucket())
                .WithObject(storageKey), cancellationToken);
            return new ObjectStat(stat.Size, stat.ContentType ?? "application/octet-stream", stat.ETag);
        }
        catch (Minio.Exceptions.ObjectNotFoundException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private string Bucket() => configuration["Minio:Bucket"] ?? "vibechat";

    private string RewritePublicUrl(string url)
    {
        var publicEndpoint = configuration["Minio:PublicEndpoint"];
        if (string.IsNullOrWhiteSpace(publicEndpoint))
        {
            return url;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var original))
        {
            return url;
        }

        var publicBase = publicEndpoint.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? publicEndpoint
            : $"http://{publicEndpoint}";
        if (!Uri.TryCreate(publicBase, UriKind.Absolute, out var publicUri))
        {
            return url;
        }

        var builder = new UriBuilder(original)
        {
            Scheme = publicUri.Scheme,
            Host = publicUri.Host,
            Port = publicUri.IsDefaultPort ? -1 : publicUri.Port
        };
        return builder.Uri.ToString();
    }
}

public sealed class SignalRChatPublisher(IHubContext<ChatHub> hubContext) : IChatPublisher
{
    public Task PublishAsync(RealtimeMessage message, CancellationToken cancellationToken) =>
        hubContext.Clients.Group(ChatHub.ChannelGroup(message.TenantId, message.ChannelId))
            .SendAsync(
                message.EventName,
                RealtimePayloadNormalization.Normalize(message.Payload),
                cancellationToken);
}

internal static class RealtimePayloadNormalization
{
    public static object Normalize(object? payload) => payload switch
    {
        null => new JsonObject(),
        JsonNode node => node,
        JsonElement element => JsonNode.Parse(element.GetRawText()) ?? new JsonObject(),
        string json when !string.IsNullOrWhiteSpace(json) => JsonNode.Parse(json) ?? new JsonObject(),
        _ => payload
    };
}

public sealed class RedisChannelChatPublisher(RedisConnection redis) : IChatPublisher
{
    public const string ChannelName = "vibechat:realtime";

    public async Task PublishAsync(RealtimeMessage message, CancellationToken cancellationToken)
    {
        var subscriber = await redis.GetSubscriberAsync();
        if (subscriber is null)
        {
            return;
        }

        await subscriber.PublishAsync(
            RedisChannel.Literal(ChannelName),
            JsonSerializer.Serialize(new RedisRealtimeEnvelope(
                message.EventName,
                message.TenantId.Value,
                message.ChannelId.Value,
                RealtimePayloadNormalization.Normalize(message.Payload))));
    }
}

public sealed record RedisRealtimeEnvelope(string EventName, Guid TenantId, Guid ChannelId, object Payload);

public sealed class RedisSignalRBridge(RedisConnection redis, IHubContext<ChatHub> hubContext, ILogger<RedisSignalRBridge> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriber = await redis.GetSubscriberAsync();
        if (subscriber is null)
        {
            return;
        }

        await subscriber.SubscribeAsync(RedisChannel.Literal(RedisChannelChatPublisher.ChannelName), async (_, value) =>
        {
            try
            {
                var envelope = JsonSerializer.Deserialize<RedisRealtimeEnvelope>(value.ToString());
                if (envelope is null)
                {
                    return;
                }

                // Normalize payload to JsonNode so the JS client receives an object, not a string.
                var payload = RealtimePayloadNormalization.Normalize(envelope.Payload);
                await hubContext.Clients.Group(ChatHub.ChannelGroup(
                        new TenantId(envelope.TenantId),
                        new ChannelId(envelope.ChannelId)))
                    .SendAsync(envelope.EventName, payload, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Redis realtime bridge failed to publish event");
            }
        });
    }
}

public sealed class OutboxProcessor(IServiceScopeFactory scopeFactory, ILogger<OutboxProcessor> logger)
{
    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VibeChatDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IChatPublisher>();
        var searchIndexer = scope.ServiceProvider.GetRequiredService<ISearchIndexer>();
        var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
        var webhooks = scope.ServiceProvider.GetRequiredService<IOutboundWebhookDispatcher>();
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
                    nameof(MessageDeletedEvent) => "MessageDeleted",
                    nameof(ReactionChangedEvent) => "ReactionChanged",
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
                }

                // B-048: best-effort outbound webhook after realtime (MessageCreated only in this slice).
                if (outbox.Type == nameof(MessageCreatedEvent))
                {
                    await webhooks.TryDispatchAsync(tenantId, eventName, outbox.Id, outbox.Payload, cancellationToken);
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
        return messages.Length;
    }
}

/// <summary>Resolves effective email/SMTP settings: tenant DB overrides non-secrets; password always from env (B-069).</summary>
public sealed class EmailSettingsResolver(VibeChatDbContext dbContext, IConfiguration configuration)
{
    public async Task<bool> IsEnabledAsync(TenantId tenantId, CancellationToken cancellationToken)
    {
        var row = await dbContext.TenantEmailSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken);
        if (row is not null)
        {
            return row.Enabled;
        }

        return configuration.GetValue("Email:Enabled", false);
    }

    public async Task<EffectiveSmtpSettings> ResolveAsync(TenantId tenantId, CancellationToken cancellationToken)
    {
        var row = await dbContext.TenantEmailSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken);

        var envHost = configuration["Email:Smtp:Host"] ?? configuration["SMTP_HOST"] ?? "localhost";
        var envPort = configuration.GetValue("Email:Smtp:Port", configuration.GetValue("SMTP_PORT", 1025));
        var envFrom = configuration["Email:Smtp:From"] ?? configuration["SMTP_FROM"] ?? "noreply@localhost";
        var envUser = configuration["Email:Smtp:Username"] ?? configuration["SMTP_USERNAME"] ?? string.Empty;
        var envPassword = configuration["Email:Smtp:Password"] ?? configuration["SMTP_PASSWORD"] ?? string.Empty;
        var envTls = configuration.GetValue("Email:Smtp:UseStartTls", configuration.GetValue("SMTP_USE_STARTTLS", false));
        var envEnabled = configuration.GetValue("Email:Enabled", false);

        if (row is null)
        {
            return new EffectiveSmtpSettings(envEnabled, envHost, envPort, envUser, envPassword, envFrom, envTls, Source: "env");
        }

        return new EffectiveSmtpSettings(
            row.Enabled,
            string.IsNullOrWhiteSpace(row.Host) ? envHost : row.Host,
            row.Port > 0 ? row.Port : envPort,
            string.IsNullOrWhiteSpace(row.Username) ? envUser : row.Username,
            envPassword,
            string.IsNullOrWhiteSpace(row.From) ? envFrom : row.From,
            row.UseStartTls,
            Source: "tenant");
    }
}

public sealed record EffectiveSmtpSettings(
    bool Enabled,
    string Host,
    int Port,
    string Username,
    string Password,
    string From,
    bool UseStartTls,
    string Source);

public sealed class SmtpEmailSender(
    IConfiguration configuration,
    EmailSettingsResolver settingsResolver,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public string Name => "Smtp";
    public bool IsEnabled => configuration.GetValue("Email:Enabled", false);

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        EffectiveSmtpSettings smtp;
        if (message.TenantId is { } tenantGuid && tenantGuid != Guid.Empty)
        {
            smtp = await settingsResolver.ResolveAsync(new TenantId(tenantGuid), cancellationToken);
        }
        else if (!IsEnabled)
        {
            return;
        }
        else
        {
            smtp = new EffectiveSmtpSettings(
                true,
                configuration["Email:Smtp:Host"] ?? configuration["SMTP_HOST"] ?? "localhost",
                configuration.GetValue("Email:Smtp:Port", configuration.GetValue("SMTP_PORT", 1025)),
                configuration["Email:Smtp:Username"] ?? configuration["SMTP_USERNAME"] ?? string.Empty,
                configuration["Email:Smtp:Password"] ?? configuration["SMTP_PASSWORD"] ?? string.Empty,
                configuration["Email:Smtp:From"] ?? configuration["SMTP_FROM"] ?? "noreply@localhost",
                configuration.GetValue("Email:Smtp:UseStartTls", configuration.GetValue("SMTP_USE_STARTTLS", false)),
                "env");
        }

        if (!smtp.Enabled)
        {
            return;
        }

        var from = message.From ?? smtp.From;
        using var client = new System.Net.Mail.SmtpClient(smtp.Host, smtp.Port)
        {
            EnableSsl = smtp.UseStartTls,
            DeliveryMethod = System.Net.Mail.SmtpDeliveryMethod.Network
        };

        if (SecretMasking.IsConfigured(smtp.Username))
        {
            client.Credentials = new System.Net.NetworkCredential(smtp.Username, smtp.Password);
        }

        using var mail = new System.Net.Mail.MailMessage(from, message.To, message.Subject, message.BodyText)
        {
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8
        };

        try
        {
            await client.SendMailAsync(mail, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SMTP send failed via {Host}:{Port}", smtp.Host, smtp.Port);
            throw;
        }
    }
}

/// <summary>HTTP POST outbound webhooks with HMAC-SHA256 (B-048). Failures are logged, never thrown.</summary>
public sealed class OutboundWebhookDispatcher(
    VibeChatDbContext dbContext,
    IHttpClientFactory httpClientFactory,
    ILogger<OutboundWebhookDispatcher> logger) : IOutboundWebhookDispatcher
{
    public const string HttpClientName = "OutboundWebhooks";

    public async Task TryDispatchAsync(
        TenantId tenantId,
        string eventName,
        Guid deliveryId,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        try
        {
            var endpoint = await dbContext.OutboundWebhookEndpoints.AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken);
            if (endpoint is null
                || !endpoint.Enabled
                || !SecretMasking.IsConfigured(endpoint.Url)
                || !SecretMasking.IsConfigured(endpoint.Secret)
                || !WebhookDelivery.IsValidHttpsUrl(endpoint.Url))
            {
                return;
            }

            var client = httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint.Url.Trim())
            {
                Content = new StringContent(payloadJson, Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation(WebhookDelivery.EventHeader, eventName);
            request.Headers.TryAddWithoutValidation(WebhookDelivery.DeliveryIdHeader, deliveryId.ToString("D"));
            request.Headers.TryAddWithoutValidation(
                WebhookDelivery.SignatureHeader,
                WebhookDelivery.ComputeSignature(endpoint.Secret.Trim(), payloadJson));

            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Outbound webhook delivery {DeliveryId} for tenant {TenantId} returned {StatusCode}",
                    deliveryId,
                    tenantId.Value,
                    (int)response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Outbound webhook delivery {DeliveryId} for tenant {TenantId} failed",
                deliveryId,
                tenantId.Value);
        }
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

/// <summary>Process-level kill switch + batch knobs for B-047 purge (ADR-018).</summary>
public sealed class MessageRetentionOptions
{
    public const string SectionName = "MessageRetention";

    public bool Enabled { get; set; }
    public int DefaultRetentionDays { get; set; } = MessageRetentionSettings.DefaultRetentionDays;
    public int BatchSize { get; set; } = 500;
    public int IntervalMinutes { get; set; } = 60;
}

/// <summary>
/// Hard-deletes soft-deleted messages past tenant retention (B-047).
/// Requires MessageRetention:Enabled=true and tenant MessageRetentionSettings.Enabled.
/// </summary>
public sealed class MessageRetentionPurgeProcessor(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<MessageRetentionPurgeProcessor> logger)
{
    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        var options = configuration.GetSection(MessageRetentionOptions.SectionName).Get<MessageRetentionOptions>()
            ?? new MessageRetentionOptions();
        if (!options.Enabled)
        {
            return 0;
        }

        var batchSize = Math.Clamp(options.BatchSize <= 0 ? 500 : options.BatchSize, 1, 2000);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VibeChatDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var now = clock.UtcNow;

        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenant.SetJobRole("retention");
        await RlsSession.EnsureAppliedAsync(db, tenant, cancellationToken);

        var policies = await db.MessageRetentionSettings.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.Enabled)
            .ToListAsync(cancellationToken);
        if (policies.Count == 0)
        {
            await RlsSession.CommitAsync(db, cancellationToken);
            return 0;
        }

        var purgedTotal = 0;
        foreach (var policy in policies)
        {
            tenant.SetTenant(policy.TenantId);
            await RlsSession.EnsureAppliedAsync(db, tenant, cancellationToken);

            var days = Math.Clamp(
                policy.RetentionDays <= 0 ? MessageRetentionSettings.DefaultRetentionDays : policy.RetentionDays,
                MessageRetentionSettings.MinRetentionDays,
                MessageRetentionSettings.MaxRetentionDays);
            var cutoff = now.AddDays(-days);

            var candidates = await db.Messages.IgnoreQueryFilters()
                .Where(x => x.TenantId == policy.TenantId
                    && x.DeletedAt != null
                    && x.DeletedAt < cutoff)
                .OrderBy(x => x.DeletedAt)
                .Take(batchSize)
                .ToListAsync(cancellationToken);
            if (candidates.Count == 0)
            {
                continue;
            }

            var messageIds = candidates.Select(x => x.Id).ToList();
            var messageIdGuids = candidates.Select(x => x.Id.Value).ToHashSet();
            var reactions = await db.Reactions.IgnoreQueryFilters()
                .Where(x => x.TenantId == policy.TenantId && messageIds.Contains(x.MessageId))
                .ToListAsync(cancellationToken);
            if (reactions.Count > 0)
            {
                db.Reactions.RemoveRange(reactions);
            }

            // Keep attachment metadata for compliance; detach from purged message rows.
            // Avoid EF Contains/EF.Property on nullable MessageId? — filter in memory (B-046 pitfall).
            var attachmentCandidates = await db.Attachments.IgnoreQueryFilters()
                .Where(x => x.TenantId == policy.TenantId && x.MessageId != null)
                .ToListAsync(cancellationToken);
            foreach (var attachment in attachmentCandidates
                .Where(a => a.MessageId is { } mid && messageIdGuids.Contains(mid.Value)))
            {
                attachment.MessageId = null;
            }

            db.Messages.RemoveRange(candidates);
            db.AuditEvents.Add(new AuditEvent
            {
                Id = Guid.NewGuid(),
                TenantId = policy.TenantId,
                ActorUserId = null,
                Action = AuditActions.MessagePurge,
                EntityType = "Message",
                EntityId = null,
                MetadataJson = JsonSerializer.Serialize(new
                {
                    count = candidates.Count,
                    retentionDays = days,
                    cutoff
                }),
                OccurredAt = now
            });

            await db.SaveChangesAsync(cancellationToken);
            purgedTotal += candidates.Count;
            logger.LogInformation(
                "Purged {Count} soft-deleted messages for tenant {TenantId} (retention {Days}d)",
                candidates.Count,
                policy.TenantId.Value,
                days);
        }

        await RlsSession.CommitAsync(db, cancellationToken);
        return purgedTotal;
    }
}

public sealed class MessageRetentionPurgeDispatcher(
    MessageRetentionPurgeProcessor processor,
    IConfiguration configuration,
    ILogger<MessageRetentionPurgeDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = configuration.GetSection(MessageRetentionOptions.SectionName).Get<MessageRetentionOptions>()
            ?? new MessageRetentionOptions();
        var intervalMinutes = Math.Clamp(options.IntervalMinutes <= 0 ? 60 : options.IntervalMinutes, 1, 24 * 60);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await processor.ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Message retention purge loop failed");
            }

            await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
        }
    }
}

public sealed class ChatHub(
    ITypingService typing,
    IPresenceService presence,
    IChannelMembershipReader channels,
    IWorkspaceMembershipReader workspaces,
    IRateLimiter rateLimiter,
    IConfiguration configuration,
    ITenantContext tenantContext,
    VibeChatDbContext dbContext) : Hub
{
    public static string ChannelGroup(TenantId tenantId, ChannelId channelId) =>
        $"t:{tenantId.Value}:c:{channelId.Value}";

    public static string TenantGroup(TenantId tenantId) => $"t:{tenantId.Value}";

    public override async Task OnConnectedAsync()
    {
        VibeChatMetrics.AdjustRealtimeConnections(1);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        VibeChatMetrics.AdjustRealtimeConnections(-1);
        if (Context.Items.TryGetValue("tenantId", out var tenantObj)
            && tenantObj is Guid tenantGuid
            && !CurrentUserId().Equals(UserId.Empty))
        {
            var tenant = new TenantId(tenantGuid);
            var userId = CurrentUserId();
            await presence.SetOfflineAsync(tenant, userId, Context.ConnectionId, Context.ConnectionAborted);
            await Clients.Group(TenantGroup(tenant)).SendAsync(
                "PresenceChanged",
                new { tenantId = tenant.Value, userId = userId.Value, status = PresenceStatus.Offline.ToString().ToLowerInvariant() },
                Context.ConnectionAborted);
        }

        await base.OnDisconnectedAsync(exception);
    }

    public Task JoinChannel(Guid tenantId, Guid channelId) =>
        WithRlsAsync(new TenantId(tenantId), CurrentUserId(), async (tenant, userId) =>
        {
            var channel = new ChannelId(channelId);
            await EnsureHubRateLimitAsync(tenant, userId);
            if (!await channels.CanAccessAsync(tenant, channel, userId, Context.ConnectionAborted))
            {
                throw new HubException("Not authorized for channel.");
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, ChannelGroup(tenant, channel), Context.ConnectionAborted);
            await EnsurePresenceGroupAsync(tenant, userId);
            await presence.HeartbeatAsync(tenant, userId, Context.ConnectionId, Context.ConnectionAborted);
            await BroadcastPresenceAsync(tenant, userId, PresenceStatus.Online);
        });

    public async Task LeaveChannel(Guid tenantId, Guid channelId)
    {
        var tenant = new TenantId(tenantId);
        var channel = new ChannelId(channelId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, ChannelGroup(tenant, channel), Context.ConnectionAborted);
    }

    public Task Heartbeat(Guid tenantId) =>
        WithRlsAsync(new TenantId(tenantId), CurrentUserId(), async (tenant, userId) =>
        {
            await EnsureHubRateLimitAsync(tenant, userId);
            await EnsureTenantMembershipAsync(tenant, userId);
            await EnsurePresenceGroupAsync(tenant, userId);
            await presence.HeartbeatAsync(tenant, userId, Context.ConnectionId, Context.ConnectionAborted);
            await BroadcastPresenceAsync(tenant, userId, PresenceStatus.Online);
        });

    public Task SetAway(Guid tenantId) =>
        WithRlsAsync(new TenantId(tenantId), CurrentUserId(), async (tenant, userId) =>
        {
            await EnsureHubRateLimitAsync(tenant, userId);
            await EnsureTenantMembershipAsync(tenant, userId);
            await EnsurePresenceGroupAsync(tenant, userId);
            await presence.SetAwayAsync(tenant, userId, Context.ConnectionId, Context.ConnectionAborted);
            await BroadcastPresenceAsync(tenant, userId, PresenceStatus.Away);
        });

    public Task SendTyping(Guid tenantId, Guid channelId, string displayName) =>
        WithRlsAsync(new TenantId(tenantId), CurrentUserId(), async (tenant, userId) =>
        {
            var channel = new ChannelId(channelId);
            await EnsureHubRateLimitAsync(tenant, userId);
            if (!await channels.CanAccessAsync(tenant, channel, userId, Context.ConnectionAborted))
            {
                throw new HubException("Not authorized for channel.");
            }

            await typing.SetTypingAsync(tenant, channel, userId, displayName, Context.ConnectionAborted);
            // B-071 / W6-2: never fan out typing to the author (OthersInGroup).
            await Clients.OthersInGroup(ChannelGroup(tenant, channel)).SendAsync(
                "Typing",
                new { tenantId, channelId, userId = userId.Value, displayName },
                Context.ConnectionAborted);
        });

    private async Task WithRlsAsync(TenantId tenantId, UserId userId, Func<TenantId, UserId, Task> action)
    {
        tenantContext.SetUser(userId);
        tenantContext.SetTenant(tenantId);
        await RlsSession.EnsureAppliedAsync(dbContext, tenantContext, Context.ConnectionAborted);
        try
        {
            await action(tenantId, userId);
            await RlsSession.CommitAsync(dbContext, Context.ConnectionAborted);
        }
        catch
        {
            await RlsSession.RollbackAsync(dbContext, CancellationToken.None);
            throw;
        }
    }

    private async Task EnsureTenantMembershipAsync(TenantId tenantId, UserId userId)
    {
        var roles = await workspaces.GetRolesAsync(tenantId, userId, Context.ConnectionAborted);
        if (roles.Count == 0)
        {
            throw new HubException("Not authorized for tenant.");
        }
    }

    private async Task EnsurePresenceGroupAsync(TenantId tenantId, UserId userId)
    {
        Context.Items["tenantId"] = tenantId.Value;
        Context.Items["userId"] = userId.Value;
        await Groups.AddToGroupAsync(Context.ConnectionId, TenantGroup(tenantId), Context.ConnectionAborted);
    }

    private Task BroadcastPresenceAsync(TenantId tenantId, UserId userId, PresenceStatus status) =>
        Clients.Group(TenantGroup(tenantId)).SendAsync(
            "PresenceChanged",
            new { tenantId = tenantId.Value, userId = userId.Value, status = status.ToString().ToLowerInvariant() },
            Context.ConnectionAborted);

    private async Task EnsureHubRateLimitAsync(TenantId tenantId, UserId userId)
    {
        var limit = configuration.GetValue("RateLimit:HubPerMinute", RateLimitPolicies.DefaultHubPerMinute);
        var allowed = await rateLimiter.TryAcquireAsync(
            RateLimitKeys.Hub(tenantId, userId),
            limit,
            TimeSpan.FromMinutes(1),
            Context.ConnectionAborted);
        if (!allowed)
        {
            throw new HubException("Rate limit exceeded.");
        }
    }

    private UserId CurrentUserId()
    {
        var value = Context.User?.FindFirst("vibechat_user_id")?.Value ?? Context.User?.FindFirst("user_id")?.Value;
        return Guid.TryParse(value, out var id) ? new UserId(id) : UserId.Empty;
    }
}

public sealed class SeedData(VibeChatDbContext dbContext, IClock clock, ILogger<SeedData> logger)
{
    public static readonly WorkspaceId DemoWorkspaceId = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    public static readonly TenantId DemoTenantId = new(DemoWorkspaceId.Value);
    public static readonly ChannelId DemoChannelId = new(Guid.Parse("22222222-2222-2222-2222-222222222222"));
    public static readonly UserId DemoUserId = new(Guid.Parse("33333333-3333-3333-3333-333333333333"));
    public static readonly UserId AliceUserId = new(Guid.Parse("44444444-4444-4444-4444-444444444444"));
    public static readonly UserId BobUserId = new(Guid.Parse("55555555-5555-5555-5555-555555555555"));
    public static readonly Guid DemoSpaceGeralId = Guid.Parse("88888888-8888-8888-8888-888888888888");
    public static readonly Guid DemoSpaceEngenhariaId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        if (!await dbContext.Workspaces.IgnoreQueryFilters().AnyAsync(x => x.Id == DemoWorkspaceId, cancellationToken))
        {
            dbContext.Workspaces.Add(new Workspace { Id = DemoWorkspaceId, TenantId = DemoTenantId, Name = "VibeChat Demo", Slug = "vibechat-demo", AiEnabled = true, CreatedAt = now });
            dbContext.AiSettings.Add(new AiSettings { WorkspaceId = DemoWorkspaceId, TenantId = DemoTenantId, Enabled = true, Provider = "Mock" });
        }

        var users = new[]
        {
            new UserProfile { Id = DemoUserId, Subject = "dev:demo", Email = "demo@vibechat.local", DisplayName = "Demo", CreatedAt = now, UpdatedAt = now },
            new UserProfile { Id = AliceUserId, Subject = "dev:alice", Email = "alice@vibechat.local", DisplayName = "Alice", CreatedAt = now, UpdatedAt = now },
            new UserProfile { Id = BobUserId, Subject = "dev:bob", Email = "bob@vibechat.local", DisplayName = "Bob", CreatedAt = now, UpdatedAt = now }
        };

        foreach (var user in users)
        {
            if (!await dbContext.UserProfiles.AnyAsync(x => x.Id == user.Id, cancellationToken))
            {
                dbContext.UserProfiles.Add(user);
            }
        }

        if (!await dbContext.Spaces.IgnoreQueryFilters().AnyAsync(x => x.Id == DemoSpaceGeralId, cancellationToken))
        {
            dbContext.Spaces.Add(new Space
            {
                Id = DemoSpaceGeralId,
                TenantId = DemoTenantId,
                WorkspaceId = DemoWorkspaceId,
                Name = "Geral",
                Order = 0,
                CreatedAt = now
            });
        }

        if (!await dbContext.Spaces.IgnoreQueryFilters().AnyAsync(x => x.Id == DemoSpaceEngenhariaId, cancellationToken))
        {
            dbContext.Spaces.Add(new Space
            {
                Id = DemoSpaceEngenhariaId,
                TenantId = DemoTenantId,
                WorkspaceId = DemoWorkspaceId,
                Name = "Engenharia",
                Order = 1,
                CreatedAt = now
            });
        }

        if (!await dbContext.Channels.IgnoreQueryFilters().AnyAsync(x => x.Id == DemoChannelId, cancellationToken))
        {
            dbContext.Channels.Add(new Channel
            {
                Id = DemoChannelId,
                TenantId = DemoTenantId,
                WorkspaceId = DemoWorkspaceId,
                SpaceId = DemoSpaceGeralId,
                Name = "geral",
                Type = ChannelType.Public,
                CreatedAt = now,
                CreatedBy = DemoUserId
            });
        }
        else
        {
            var demoChannel = await dbContext.Channels.IgnoreQueryFilters().FirstAsync(x => x.Id == DemoChannelId, cancellationToken);
            if (demoChannel.SpaceId is null)
            {
                demoChannel.SpaceId = DemoSpaceGeralId;
            }
        }

        foreach (var (userId, role) in new[] { (DemoUserId, Role.WorkspaceOwner), (AliceUserId, Role.Member), (BobUserId, Role.Member) })
        {
            if (!await dbContext.WorkspaceMembers.IgnoreQueryFilters().AnyAsync(x => x.WorkspaceId == DemoWorkspaceId && x.UserId == userId, cancellationToken))
            {
                dbContext.WorkspaceMembers.Add(new WorkspaceMember { Id = Guid.NewGuid(), TenantId = DemoTenantId, WorkspaceId = DemoWorkspaceId, UserId = userId, Role = role, JoinedAt = now });
            }

            if (!await dbContext.ChannelMembers.IgnoreQueryFilters().AnyAsync(x => x.ChannelId == DemoChannelId && x.UserId == userId, cancellationToken))
            {
                dbContext.ChannelMembers.Add(new ChannelMember { Id = Guid.NewGuid(), TenantId = DemoTenantId, ChannelId = DemoChannelId, UserId = userId, JoinedAt = now });
            }
        }

        if (!await dbContext.Messages.IgnoreQueryFilters().AnyAsync(x => x.ConversationId == DemoChannelId, cancellationToken))
        {
            dbContext.ConversationSequences.Add(new ConversationSequence { TenantId = DemoTenantId, ConversationId = DemoChannelId, LastSequence = 2 });
            dbContext.Messages.AddRange(
                new Message { Id = new MessageId(Guid.Parse("66666666-6666-6666-6666-666666666666")), TenantId = DemoTenantId, ConversationId = DemoChannelId, AuthorId = DemoUserId, Sequence = 1, Body = "Bem-vindo ao VibeChat Demo.", CreatedAt = now },
                new Message { Id = new MessageId(Guid.Parse("77777777-7777-7777-7777-777777777777")), TenantId = DemoTenantId, ConversationId = DemoChannelId, AuthorId = AliceUserId, Sequence = 2, Body = "AI summaries are enabled with the mock provider.", CreatedAt = now });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Demo seed data ensured");
    }
}

public sealed class DatabaseHealthCheck(VibeChatDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await dbContext.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy("PostgreSQL reachable")
            : HealthCheckResult.Unhealthy("PostgreSQL unreachable");
}

public sealed class RedisHealthCheck(RedisConnection redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var db = await redis.GetDatabaseAsync();
        if (db is null)
        {
            return HealthCheckResult.Degraded("Redis is not configured");
        }

        return await db.PingAsync() >= TimeSpan.Zero
            ? HealthCheckResult.Healthy("Redis reachable")
            : HealthCheckResult.Unhealthy("Redis unreachable");
    }
}

public sealed class MinioHealthCheck(IObjectStorage storage) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await storage.IsHealthyAsync(cancellationToken)
            ? HealthCheckResult.Healthy("MinIO reachable")
            : HealthCheckResult.Degraded("MinIO bucket unavailable");
}

public static class DependencyInjection
{
    public static IServiceCollection AddVibeChatInfrastructure(this IServiceCollection services, IConfiguration configuration, bool useSignalRPublisher = true)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<RlsConnectionInterceptor>();
        services.AddDbContext<VibeChatDbContext>((sp, options) =>
        {
            options.UseNpgsql(DatabaseBootstrap.ResolveRuntimeConnectionString(configuration));
            options.AddInterceptors(sp.GetRequiredService<RlsConnectionInterceptor>());
        });
        services.AddScoped<IOutboxWriter, EfOutboxWriter>();
        services.AddScoped<IAuditWriter, EfAuditWriter>();
        services.AddScoped<IIdempotencyStore, EfIdempotencyStore>();
        services.AddScoped<IConversationSequenceStore, ConversationSequenceStore>();
        services.AddScoped<IMessageWriter, MessageWriter>();
        services.AddScoped<ISearchIndexer, PostgresSearchIndexer>();
        services.AddScoped<ISearchQuery, PostgresSearchQuery>();
        services.AddSingleton<IRateLimiter, RedisRateLimiter>();
        services.AddScoped<PermissionChecker>();
        services.AddScoped<IPermissionChecker>(sp => sp.GetRequiredService<PermissionChecker>());
        services.AddScoped<IWorkspaceMembershipReader>(sp => sp.GetRequiredService<PermissionChecker>());
        services.AddScoped<IChannelMembershipReader>(sp => sp.GetRequiredService<PermissionChecker>());
        services.AddScoped<IDashboardQuery, DashboardQuery>();
        services.AddScoped<ISummarizeChannelFeature, SummarizeChannelFeature>();
        services.AddScoped<ISuggestChannelReplyFeature, SuggestChannelReplyFeature>();
        services.AddSingleton<RedisConnection>();
        services.AddScoped<ITypingService, TypingService>();
        services.AddScoped<IPresenceService, PresenceService>();
        if (useSignalRPublisher)
        {
            services.AddScoped<IChatPublisher, SignalRChatPublisher>();
            services.AddHostedService<RedisSignalRBridge>();
        }
        else
        {
            services.AddScoped<IChatPublisher, RedisChannelChatPublisher>();
        }
        services.AddSingleton<OutboxProcessor>();
        services.AddHostedService<OutboxDispatcher>();
        // B-047: processor shared; hosted purge loop is registered only in apps/worker.
        services.Configure<MessageRetentionOptions>(configuration.GetSection(MessageRetentionOptions.SectionName));
        services.AddSingleton<MessageRetentionPurgeProcessor>();
        services.AddScoped<SeedData>();

        // Resolve MinIO from IConfiguration at runtime so WebApplicationFactory overrides apply.
        services.AddSingleton<IMinioClient>(sp =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            var endpoint = cfg["Minio:Endpoint"] ?? "localhost:9000";
            var parts = endpoint.Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Split(':', 2);
            var host = parts[0];
            var port = parts.Length > 1 && int.TryParse(parts[1], out var parsedPort) ? parsedPort : 9000;
            return new MinioClient()
                .WithEndpoint(host, port)
                .WithCredentials(cfg["Minio:AccessKey"] ?? "minioadmin", cfg["Minio:SecretKey"] ?? "minioadmin_dev_password_change_me")
                .WithSSL(bool.TryParse(cfg["Minio:UseSsl"], out var ssl) && ssl)
                .Build();
        });
        services.AddScoped<IObjectStorage, MinioObjectStorage>();

        services.AddHttpClient<OpenRouterAiProvider>((sp, client) =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            client.BaseAddress = new Uri(cfg["Ai:OpenRouter:BaseUrl"] ?? "https://openrouter.ai/api/v1");
            var apiKey = cfg["Ai:OpenRouter:ApiKey"];
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            }
        });

        services.AddScoped<IAiCompletionProvider>(sp =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            // D-06: AI off by default; OpenRouter only when Enabled + Provider=OpenRouter + key.
            if (!cfg.GetValue("Ai:Enabled", false))
            {
                return new NullAiProvider();
            }

            if (string.Equals(cfg["Ai:Provider"], "OpenRouter", StringComparison.OrdinalIgnoreCase))
            {
                var apiKey = cfg["Ai:OpenRouter:ApiKey"];
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    return new NullAiProvider();
                }

                return sp.GetRequiredService<OpenRouterAiProvider>();
            }

            return new MockAiProvider();
        });

        // D-10 / B-043 / B-069: email off by default; runtime tenant overrides via EmailSettingsResolver.
        // Always register SmtpEmailSender — it no-ops when effectively disabled (env or tenant).
        services.AddScoped<EmailSettingsResolver>();
        services.AddScoped<IEmailSender, SmtpEmailSender>();

        // B-048: outbound webhooks — tenant URL+HMAC secret; best-effort after MessageCreated outbox.
        services.AddHttpClient(OutboundWebhookDispatcher.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
        });
        services.AddScoped<IOutboundWebhookDispatcher, OutboundWebhookDispatcher>();

        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("postgres")
            .AddCheck<RedisHealthCheck>("redis")
            .AddCheck<MinioHealthCheck>("minio");

        return services;
    }
}
