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

public sealed class VibeChatDbContext(DbContextOptions<VibeChatDbContext> options, ITenantContext tenantContext) : DbContext(options)
{
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<UserStatus> UserStatuses => Set<UserStatus>();
    public DbSet<UserVisualPreference> UserVisualPreferences => Set<UserVisualPreference>();
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<WorkspaceMember> WorkspaceMembers => Set<WorkspaceMember>();
    public DbSet<Space> Spaces => Set<Space>();
    public DbSet<ContactGroup> ContactGroups => Set<ContactGroup>();
    public DbSet<ContactGroupMember> ContactGroupMembers => Set<ContactGroupMember>();
    public DbSet<ChannelInvite> ChannelInvites => Set<ChannelInvite>();
    public DbSet<WorkspaceTemplateRecord> WorkspaceTemplateRecords => Set<WorkspaceTemplateRecord>();
    public DbSet<WorkspaceOnboarding> WorkspaceOnboardings => Set<WorkspaceOnboarding>();
    public DbSet<TemplateApplication> TemplateApplications => Set<TemplateApplication>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<ChannelMember> ChannelMembers => Set<ChannelMember>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<MessageThread> MessageThreads => Set<MessageThread>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<Reaction> Reactions => Set<Reaction>();
    public DbSet<MessageMention> MessageMentions => Set<MessageMention>();
    public DbSet<ReadCursor> ReadCursors => Set<ReadCursor>();
    public DbSet<ConversationSequence> ConversationSequences => Set<ConversationSequence>();
    public DbSet<IdempotencyEntry> IdempotencyEntries => Set<IdempotencyEntry>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<AiUsageRecord> AiUsageRecords => Set<AiUsageRecord>();
    public DbSet<AiSettings> AiSettings => Set<AiSettings>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();
    public DbSet<ChannelNotificationPreference> ChannelNotificationPreferences => Set<ChannelNotificationPreference>();
    public DbSet<TenantEmailSettings> TenantEmailSettings => Set<TenantEmailSettings>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();
    public DbSet<OutboundWebhookEndpoint> OutboundWebhookEndpoints => Set<OutboundWebhookEndpoint>();
    public DbSet<IntegrationBot> IntegrationBots => Set<IntegrationBot>();
    public DbSet<IntegrationBotToken> IntegrationBotTokens => Set<IntegrationBotToken>();
    public DbSet<IntegrationBotChannelScope> IntegrationBotChannelScopes => Set<IntegrationBotChannelScope>();
    public DbSet<InstalledPlugin> InstalledPlugins => Set<InstalledPlugin>();
    public DbSet<MessageRetentionSettings> MessageRetentionSettings => Set<MessageRetentionSettings>();
    public DbSet<MessageLifecyclePolicy> MessageLifecyclePolicies => Set<MessageLifecyclePolicy>();
    public DbSet<MessageVersion> MessageVersions => Set<MessageVersion>();
    public DbSet<MessageMove> MessageMoves => Set<MessageMove>();
    public DbSet<TenantFilesSettings> TenantFilesSettings => Set<TenantFilesSettings>();
    public DbSet<TenantRateLimitSettings> TenantRateLimitSettings => Set<TenantRateLimitSettings>();
    public DbSet<ProcessSettings> ProcessSettings => Set<ProcessSettings>();
    public DbSet<LinkPreview> LinkPreviews => Set<LinkPreview>();
    public DbSet<MessageLinkPreview> MessageLinkPreviews => Set<MessageLinkPreview>();
    public DbSet<TenantLinkPreviewSettings> TenantLinkPreviewSettings => Set<TenantLinkPreviewSettings>();
    public DbSet<PinnedMessage> PinnedMessages => Set<PinnedMessage>();
    public DbSet<SavedMessage> SavedMessages => Set<SavedMessage>();
    public DbSet<ScheduledMessage> ScheduledMessages => Set<ScheduledMessage>();
    public DbSet<Reminder> Reminders => Set<Reminder>();
    public DbSet<ThreadSubscription> ThreadSubscriptions => Set<ThreadSubscription>();
    public DbSet<Poll> Polls => Set<Poll>();
    public DbSet<PollOption> PollOptions => Set<PollOption>();
    public DbSet<PollVote> PollVotes => Set<PollVote>();
    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<AnnouncementAcknowledgement> AnnouncementAcknowledgements => Set<AnnouncementAcknowledgement>();

    public override int SaveChanges()
    {
        DetachContactGroupsForRemovedMembers();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        await DetachContactGroupsForRemovedMembersAsync(cancellationToken);
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// B-166: leaving a workspace drops contact-group assignments. The row trigger is the
    /// database backstop; this runs in the same unit of work for the EF path.
    /// </summary>
    private void DetachContactGroupsForRemovedMembers()
    {
        var removed = RemovedWorkspaceMembers();
        if (removed.Count == 0)
        {
            return;
        }

        foreach (var member in removed)
        {
            var assignments = ContactGroupMembers.IgnoreQueryFilters()
                .Where(x => x.TenantId == member.TenantId
                    && x.WorkspaceId == member.WorkspaceId
                    && x.UserId == member.UserId)
                .ToList();
            ContactGroupMembers.RemoveRange(assignments);
        }
    }

    private async Task DetachContactGroupsForRemovedMembersAsync(CancellationToken cancellationToken)
    {
        var removed = RemovedWorkspaceMembers();
        if (removed.Count == 0)
        {
            return;
        }

        foreach (var member in removed)
        {
            var assignments = await ContactGroupMembers.IgnoreQueryFilters()
                .Where(x => x.TenantId == member.TenantId
                    && x.WorkspaceId == member.WorkspaceId
                    && x.UserId == member.UserId)
                .ToListAsync(cancellationToken);
            ContactGroupMembers.RemoveRange(assignments);
        }
    }

    private List<WorkspaceMember> RemovedWorkspaceMembers() =>
        ChangeTracker.Entries<WorkspaceMember>()
            .Where(entry => entry.State == EntityState.Deleted)
            .Select(entry => entry.Entity)
            .ToList();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureShared(modelBuilder);
        SearchSql.Configure(modelBuilder);

        modelBuilder.Entity<UserProfile>(entity =>
        {
            entity.ToTable("user_profiles", "identity");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.Subject).HasMaxLength(256);
            entity.Property(x => x.Email).HasMaxLength(256);
            entity.Property(x => x.Locale).HasMaxLength(16);
            entity.HasIndex(x => x.Subject).IsUnique();
            entity.HasIndex(x => x.Email);
        });

        modelBuilder.Entity<UserStatus>(entity =>
        {
            entity.ToTable("user_statuses", "identity");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.UserId).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.State).HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.Emoji).HasMaxLength(UserStatusRules.MaxEmojiLength);
            entity.Property(x => x.Text).HasMaxLength(UserStatusRules.MaxTextLength);
            entity.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<UserVisualPreference>(entity =>
        {
            entity.ToTable("visual_preferences", "identity");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.UserId).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.ChatWallpaperId).HasMaxLength(32);
            entity.Property(x => x.AccentColorId).HasMaxLength(32);
            entity.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
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

        modelBuilder.Entity<ContactGroup>(entity =>
        {
            entity.ToTable("contact_groups", "directory");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.Kind)
                .HasConversion(v => ContactGroupPolicies.ToWire(v), v => ContactGroupPolicies.ParseWire(v))
                .HasMaxLength(16);
            entity.Property(x => x.Name).HasMaxLength(ContactGroupPolicies.MaxNameLength);
            entity.Property(x => x.OwnerUserId).HasConversion(
                v => v.HasValue ? v.Value.Value : (Guid?)null,
                v => v.HasValue ? new UserId(v.Value) : null);
            entity.HasIndex(x => new { x.WorkspaceId, x.Kind, x.Order });
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<ContactGroupMember>(entity =>
        {
            entity.ToTable("contact_group_members", "directory");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.UserId).HasConversion(v => v.Value, v => new UserId(v));
            entity.HasIndex(x => new { x.GroupId, x.UserId }).IsUnique();
            entity.HasIndex(x => new { x.WorkspaceId, x.UserId });
            entity.HasOne<ContactGroup>()
                .WithMany()
                .HasForeignKey(x => x.GroupId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<ChannelInvite>(entity =>
        {
            entity.ToTable("channel_invites", "directory");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.ChannelId).HasConversion(v => v.Value, v => new ChannelId(v));
            entity.Property(x => x.CreatedByUserId).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.AcceptedByUserId).HasConversion(v => v.HasValue ? v.Value.Value : (Guid?)null, v => v.HasValue ? new UserId(v.Value) : null);
            entity.Property(x => x.RevokedByUserId).HasConversion(v => v.HasValue ? v.Value.Value : (Guid?)null, v => v.HasValue ? new UserId(v.Value) : null);
            entity.Property(x => x.TokenHash).HasMaxLength(64);
            entity.Property(x => x.Email).HasMaxLength(256);
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.ChannelId, x.CreatedAt });
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<WorkspaceTemplateRecord>(entity =>
        {
            entity.ToTable("workspace_templates", "directory");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.CreatedBy).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.TemplateId).HasMaxLength(WorkspaceTemplateRules.MaxIdLength).IsRequired();
            entity.Property(x => x.ManifestJson).HasMaxLength(WorkspaceTemplateRules.MaxManifestBytes).IsRequired();
            entity.HasIndex(x => new { x.TenantId, x.WorkspaceId, x.TemplateId }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<WorkspaceOnboarding>(entity =>
        {
            entity.ToTable("workspace_onboarding", "directory");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.Property(x => x.TemplateId).HasMaxLength(WorkspaceTemplateRules.MaxIdLength);
            entity.Property(x => x.ItemsJson).HasMaxLength(8000).IsRequired();
            entity.HasIndex(x => x.WorkspaceId).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<TemplateApplication>(entity =>
        {
            entity.ToTable("template_applications", "directory");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.ActorUserId).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.IdempotencyKey).HasMaxLength(WorkspaceTemplateRules.MaxIdempotencyKeyLength).IsRequired();
            entity.Property(x => x.TemplateId).HasMaxLength(WorkspaceTemplateRules.MaxIdLength).IsRequired();
            entity.Property(x => x.ResultJson).HasMaxLength(262_144).IsRequired();
            entity.HasIndex(x => new { x.TenantId, x.WorkspaceId, x.IdempotencyKey }).IsUnique();
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
            entity.Property(x => x.Topic).HasMaxLength(250);
            entity.Property(x => x.Title).HasMaxLength(80);
            entity.Property(x => x.ParticipantSetKey).HasMaxLength(400);
            entity.HasIndex(x => new { x.WorkspaceId, x.Name }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.WorkspaceId, x.ParticipantSetKey })
                .IsUnique()
                .HasFilter("\"ParticipantSetKey\" IS NOT NULL");
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
            entity.HasQueryFilter(x =>
                (!tenantContext.HasTenant || x.TenantId == tenantContext.TenantId)
                && x.LeftAt == null);
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
            entity.Property(x => x.ForwardedFromMessageId).HasConversion(v => v.HasValue ? v.Value.Value : (Guid?)null, v => v.HasValue ? new MessageId(v.Value) : null);
            entity.Property(x => x.ForwardedFromChannelId).HasConversion(v => v.HasValue ? v.Value.Value : (Guid?)null, v => v.HasValue ? new ChannelId(v.Value) : null);
            entity.Property(x => x.MovedFromMessageId).HasConversion(v => v.HasValue ? v.Value.Value : (Guid?)null, v => v.HasValue ? new MessageId(v.Value) : null);
            entity.Property(x => x.MovedFromChannelId).HasConversion(v => v.HasValue ? v.Value.Value : (Guid?)null, v => v.HasValue ? new ChannelId(v.Value) : null);
            entity.Property(x => x.MovedToMessageId).HasConversion(v => v.HasValue ? v.Value.Value : (Guid?)null, v => v.HasValue ? new MessageId(v.Value) : null);
            entity.Property(x => x.MovedToChannelId).HasConversion(v => v.HasValue ? v.Value.Value : (Guid?)null, v => v.HasValue ? new ChannelId(v.Value) : null);
            entity.Property(x => x.Body).HasMaxLength(8000);
            var searchVector = entity.Property<NpgsqlTsVector>("SearchVector")
                .HasColumnName("search_vector")
                .HasColumnType("tsvector");
            searchVector.Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
            searchVector.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
            entity.HasIndex(x => new { x.ConversationId, x.Sequence }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.ConversationId, x.CreatedAt })
                .HasDatabaseName("ix_messages_tenant_channel_created");
            entity.HasIndex(x => x.ThreadId);
            entity.HasIndex(x => x.ForwardedFromMessageId);
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
            entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16).HasDefaultValue(AttachmentKind.File);
            entity.Property(x => x.ReferenceCount).HasDefaultValue(1);
            entity.Property(x => x.DurationMs);
            entity.Property(x => x.Waveform).HasColumnType("jsonb");
            entity.Property(x => x.ThumbnailKey).HasMaxLength(512);
            entity.Property(x => x.Width);
            entity.Property(x => x.Height);
            entity.Property(x => x.ThumbnailStatus).HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.PageCount);
            entity.HasIndex(x => x.StorageKey);
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

        modelBuilder.Entity<PinnedMessage>(entity =>
        {
            entity.ToTable("pinned_messages", "messaging");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.ChannelId).HasConversion(v => v.Value, v => new ChannelId(v));
            entity.Property(x => x.MessageId).HasConversion(v => v.Value, v => new MessageId(v));
            entity.Property(x => x.PinnedByUserId).HasConversion(v => v.Value, v => new UserId(v));
            entity.HasIndex(x => new { x.TenantId, x.ChannelId, x.MessageId }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.ChannelId });
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<SavedMessage>(entity =>
        {
            entity.ToTable("saved_messages", "messaging");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.UserId).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.MessageId).HasConversion(v => v.Value, v => new MessageId(v));
            entity.Property(x => x.ChannelId).HasConversion(v => v.Value, v => new ChannelId(v));
            entity.Property(x => x.Note).HasMaxLength(SavedMessagePolicies.MaxNoteLength);
            entity.HasIndex(x => new { x.TenantId, x.UserId, x.MessageId }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.UserId, x.CompletedAt, x.CreatedAt });
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<ScheduledMessage>(entity =>
        {
            entity.ToTable("scheduled_messages", "messaging");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.AuthorId).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.ChannelId).HasConversion(v => v.Value, v => new ChannelId(v));
            entity.Property(x => x.ReplyToMessageId).HasConversion(
                v => v.HasValue ? v.Value.Value : (Guid?)null,
                v => v.HasValue ? new MessageId(v.Value) : null);
            entity.Property(x => x.PlannedMessageId).HasConversion(v => v.Value, v => new MessageId(v));
            entity.Property(x => x.SentMessageId).HasConversion(
                v => v.HasValue ? v.Value.Value : (Guid?)null,
                v => v.HasValue ? new MessageId(v.Value) : null);
            entity.Property(x => x.Body).HasMaxLength(MessageBodyPolicies.MaxLength);
            entity.Property(x => x.TimeZone).HasMaxLength(SchedulePolicies.MaxTimeZoneLength);
            entity.Property(x => x.Status).HasMaxLength(32);
            entity.Property(x => x.ClientIdempotencyKey).HasMaxLength(SchedulePolicies.MaxIdempotencyKeyLength);
            entity.Property(x => x.SendIdempotencyKey).HasMaxLength(SchedulePolicies.MaxIdempotencyKeyLength);
            entity.Property(x => x.FailureCode).HasMaxLength(64);
            entity.HasIndex(x => new { x.TenantId, x.AuthorId, x.ClientIdempotencyKey }).IsUnique();
            entity.HasIndex(x => new { x.Status, x.SendAtUtc });
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<Reminder>(entity =>
        {
            entity.ToTable("reminders", "messaging");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.UserId).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.ChannelId).HasConversion(
                v => v.HasValue ? v.Value.Value : (Guid?)null,
                v => v.HasValue ? new ChannelId(v.Value) : null);
            entity.Property(x => x.MessageId).HasConversion(
                v => v.HasValue ? v.Value.Value : (Guid?)null,
                v => v.HasValue ? new MessageId(v.Value) : null);
            entity.Property(x => x.Note).HasMaxLength(SchedulePolicies.MaxNoteLength);
            entity.Property(x => x.TimeZone).HasMaxLength(SchedulePolicies.MaxTimeZoneLength);
            entity.Property(x => x.TargetKind).HasMaxLength(16);
            entity.Property(x => x.Status).HasMaxLength(32);
            entity.Property(x => x.ClientIdempotencyKey).HasMaxLength(SchedulePolicies.MaxIdempotencyKeyLength);
            entity.Property(x => x.FailureCode).HasMaxLength(64);
            entity.HasIndex(x => new { x.TenantId, x.UserId, x.ClientIdempotencyKey }).IsUnique();
            entity.HasIndex(x => new { x.Status, x.RemindAtUtc });
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<Poll>(entity =>
        {
            entity.ToTable("polls", "messaging");
            entity.HasKey(x => x.MessageId);
            entity.Property(x => x.MessageId).HasConversion(v => v.Value, v => new MessageId(v));
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.ChannelId).HasConversion(v => v.Value, v => new ChannelId(v));
            entity.Property(x => x.CreatedByUserId).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.Question).HasMaxLength(PollPolicies.MaxQuestionLength);
            entity.HasIndex(x => new { x.TenantId, x.ChannelId });
            entity.HasIndex(x => new { x.TenantId, x.ClosedAt, x.ClosesAt });
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<PollOption>(entity =>
        {
            entity.ToTable("poll_options", "messaging");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.PollId).HasConversion(v => v.Value, v => new MessageId(v));
            entity.Property(x => x.Text).HasMaxLength(PollPolicies.MaxOptionLength);
            entity.HasIndex(x => new { x.TenantId, x.PollId, x.Position }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<PollVote>(entity =>
        {
            entity.ToTable("poll_votes", "messaging");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.PollId).HasConversion(v => v.Value, v => new MessageId(v));
            entity.Property(x => x.UserId).HasConversion(v => v.Value, v => new UserId(v));
            entity.HasIndex(x => new { x.TenantId, x.PollId, x.OptionId, x.UserId }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<Announcement>(entity =>
        {
            entity.ToTable("announcements", "messaging");
            entity.HasKey(x => x.MessageId);
            entity.Property(x => x.MessageId).HasConversion(v => v.Value, v => new MessageId(v));
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.ChannelId).HasConversion(v => v.Value, v => new ChannelId(v));
            entity.Property(x => x.CreatedByUserId).HasConversion(v => v.Value, v => new UserId(v));
            entity.HasIndex(x => new { x.TenantId, x.ChannelId });
            entity.HasIndex(x => new { x.TenantId, x.RequiresAcknowledgement, x.ClosedAt, x.AcknowledgeBy });
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<AnnouncementAcknowledgement>(entity =>
        {
            entity.ToTable("announcement_acknowledgements", "messaging");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.MessageId).HasConversion(v => v.Value, v => new MessageId(v));
            entity.Property(x => x.ChannelId).HasConversion(v => v.Value, v => new ChannelId(v));
            entity.Property(x => x.UserId).HasConversion(v => v.Value, v => new UserId(v));
            entity.HasIndex(x => new { x.TenantId, x.MessageId, x.UserId }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.ChannelId, x.MessageId });
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<MessageMention>(entity =>
        {
            entity.ToTable("message_mentions", "messaging");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.MessageId).HasConversion(v => v.Value, v => new MessageId(v));
            entity.Property(x => x.ChannelId).HasConversion(v => v.Value, v => new ChannelId(v));
            entity.Property(x => x.MentionedUserId).HasConversion(
                v => v.HasValue ? v.Value.Value : (Guid?)null,
                v => v.HasValue ? new UserId(v.Value) : null);
            entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
            entity.HasIndex(x => new { x.TenantId, x.MessageId });
            entity.HasIndex(x => new { x.TenantId, x.ChannelId, x.MentionedUserId });
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
            MapEncryptedSecret(entity.OwnsOne(x => x.OpenRouterApiKey), "OpenRouterApiKey");
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<NotificationPreference>(entity =>
        {
            entity.ToTable("preferences", "notifications");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.UserId).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.Level).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.TimeZone).HasMaxLength(64);
            entity.Property(x => x.PriorityContactUserIds).HasColumnType("uuid[]");
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<ChannelNotificationPreference>(entity =>
        {
            entity.ToTable("channel_preferences", "notifications");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.UserId).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.ChannelId).HasConversion(v => v.Value, v => new ChannelId(v));
            entity.Property(x => x.Level).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(x => new { x.ChannelId, x.UserId }).IsUnique();
            entity.HasIndex(x => new { x.ChannelId, x.FollowAllThreads });
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<ThreadSubscription>(entity =>
        {
            entity.ToTable("thread_subscriptions", "messaging");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.UserId).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.ChannelId).HasConversion(v => v.Value, v => new ChannelId(v));
            entity.Property(x => x.Source).HasConversion<string>().HasMaxLength(16);
            entity.HasIndex(x => new { x.TenantId, x.UserId, x.ThreadId }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.UserId, x.CreatedAt });
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
            MapEncryptedSecret(entity.OwnsOne(x => x.SmtpPassword), "SmtpPassword");
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<PushSubscription>(entity =>
        {
            entity.ToTable("push_subscriptions", "notifications");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.UserId).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.Endpoint).HasMaxLength(2048);
            entity.Property(x => x.P256dh).HasMaxLength(256);
            entity.Property(x => x.Auth).HasMaxLength(256);
            entity.Property(x => x.UserAgent).HasMaxLength(512);
            entity.HasIndex(x => new { x.TenantId, x.UserId, x.Endpoint }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<OutboundWebhookEndpoint>(entity =>
        {
            entity.ToTable("webhook_endpoints", "integrations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.Name).HasMaxLength(WebhookPolicies.MaxNameLength).IsRequired();
            entity.Property(x => x.Url).HasMaxLength(2048);
            entity.Property(x => x.Secret).HasMaxLength(512).IsRequired(false);
            entity.Property(x => x.SubscribedEvents).HasColumnType("text[]").IsRequired();
            entity.Property(x => x.ChannelFilter).HasColumnType("uuid[]").IsRequired();
            entity.Property(x => x.LastError).HasMaxLength(WebhookPolicies.MaxLastErrorLength);
            entity.HasIndex(x => x.TenantId);
            MapEncryptedSecret(entity.OwnsOne(x => x.SigningSecret), "SigningSecret");
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<IntegrationBot>(entity =>
        {
            entity.ToTable("bots", "integrations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.UserId).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.Name).HasMaxLength(BotIntegrationPolicies.MaxNameLength).IsRequired();
            entity.HasIndex(x => new { x.TenantId, x.WorkspaceId });
            entity.HasIndex(x => x.UserId).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<IntegrationBotToken>(entity =>
        {
            entity.ToTable("bot_tokens", "integrations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Last4).HasMaxLength(8).IsRequired();
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => x.BotId);
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<InstalledPlugin>(entity =>
        {
            entity.ToTable("plugins", "integrations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.PluginId).HasMaxLength(PluginManifestRules.MaxPluginIdLength).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(BotIntegrationPolicies.MaxNameLength).IsRequired();
            entity.Property(x => x.Version).HasMaxLength(PluginManifestRules.MaxVersionLength).IsRequired();
            entity.Property(x => x.ManifestJson).HasMaxLength(PluginManifestRules.MaxManifestBytes).IsRequired();
            entity.Property(x => x.Capabilities).HasColumnType("text[]").IsRequired();
            entity.HasIndex(x => x.BotId).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.WorkspaceId, x.PluginId }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<IntegrationBotChannelScope>(entity =>
        {
            entity.ToTable("bot_channel_scopes", "integrations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.ChannelId).HasConversion(v => v.Value, v => new ChannelId(v));
            entity.HasIndex(x => new { x.BotId, x.ChannelId }).IsUnique();
            entity.HasIndex(x => x.TenantId);
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<MessageRetentionSettings>(entity =>
        {
            entity.ToTable("message_retention_settings", "messaging");
            entity.HasKey(x => x.TenantId);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<MessageLifecyclePolicy>(entity =>
        {
            entity.ToTable("message_lifecycle_policies", "messaging");
            entity.HasKey(x => x.TenantId);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.EditRoles).HasColumnType("text[]");
            entity.Property(x => x.DeleteRoles).HasColumnType("text[]");
            entity.Property(x => x.HistoryEnabled).HasDefaultValue(true);
            entity.Property(x => x.LeaveTombstone).HasDefaultValue(true);
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<MessageVersion>(entity =>
        {
            entity.ToTable("message_versions", "messaging");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.MessageId).HasConversion(v => v.Value, v => new MessageId(v));
            entity.Property(x => x.ActorUserId).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.Body).HasMaxLength(8000);
            entity.HasIndex(x => new { x.TenantId, x.MessageId, x.VersionNumber }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<MessageMove>(entity =>
        {
            entity.ToTable("message_moves", "messaging");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.SourceMessageId).HasConversion(v => v.Value, v => new MessageId(v));
            entity.Property(x => x.SourceChannelId).HasConversion(v => v.Value, v => new ChannelId(v));
            entity.Property(x => x.DestinationMessageId).HasConversion(v => v.Value, v => new MessageId(v));
            entity.Property(x => x.DestinationChannelId).HasConversion(v => v.Value, v => new ChannelId(v));
            entity.Property(x => x.ActorUserId).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.IdempotencyKey).HasMaxLength(MessageHistoryPolicies.MaxIdempotencyKeyLength);
            entity.Property(x => x.Scope).HasMaxLength(16);
            entity.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.SourceMessageId }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<TenantFilesSettings>(entity =>
        {
            entity.ToTable("settings", "files");
            entity.HasKey(x => x.TenantId).HasName("PK_files_settings");
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.AllowedContentTypes).HasColumnType("text[]");
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<TenantRateLimitSettings>(entity =>
        {
            entity.ToTable("rate_limit_settings", "building_blocks");
            entity.HasKey(x => x.TenantId);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<ProcessSettings>(entity =>
        {
            entity.ToTable("process_settings", "administration");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.OpenRouterBaseUrl).HasMaxLength(2048);
            entity.Property(x => x.VapidPublicKey).HasMaxLength(256);
            entity.Property(x => x.VapidSubject).HasMaxLength(256);
            MapEncryptedSecret(entity.OwnsOne(x => x.VapidPrivateKey), "VapidPrivateKey");
        });

        modelBuilder.Entity<LinkPreview>(entity =>
        {
            entity.ToTable("link_previews", "messaging");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.UrlHash).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Url).HasMaxLength(2048).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(LinkPreviewPolicies.MaxTitleLength);
            entity.Property(x => x.Description).HasMaxLength(LinkPreviewPolicies.MaxDescriptionLength);
            entity.Property(x => x.SiteName).HasMaxLength(LinkPreviewPolicies.MaxSiteNameLength);
            entity.Property(x => x.ImageKey).HasMaxLength(512);
            entity.Property(x => x.ImageContentType).HasMaxLength(128);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            entity.HasIndex(x => new { x.TenantId, x.UrlHash }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<MessageLinkPreview>(entity =>
        {
            entity.ToTable("message_link_previews", "messaging");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.MessageId).HasConversion(v => v.Value, v => new MessageId(v));
            entity.Property(x => x.ChannelId).HasConversion(v => v.Value, v => new ChannelId(v));
            entity.HasIndex(x => new { x.TenantId, x.MessageId });
            entity.HasIndex(x => x.LinkPreviewId);
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<TenantLinkPreviewSettings>(entity =>
        {
            entity.ToTable("link_preview_settings", "messaging");
            entity.HasKey(x => x.TenantId);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });
    }

    private static void MapEncryptedSecret<TEntity>(
        OwnedNavigationBuilder<TEntity, EncryptedSecretEnvelope> owned,
        string prefix)
        where TEntity : class
    {
        owned.Property(x => x.Ciphertext).HasColumnName($"{prefix}Ciphertext");
        owned.Property(x => x.Nonce).HasColumnName($"{prefix}Nonce");
        owned.Property(x => x.Tag).HasColumnName($"{prefix}Tag");
        owned.Property(x => x.KeyVersion).HasColumnName($"{prefix}KeyVersion");
        owned.Property(x => x.FormatVersion).HasColumnName($"{prefix}FormatVersion");
        owned.Property(x => x.MaskSuffix).HasColumnName($"{prefix}MaskSuffix").HasMaxLength(8);
        owned.Property(x => x.RotatedAt).HasColumnName($"{prefix}RotatedAt");
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
