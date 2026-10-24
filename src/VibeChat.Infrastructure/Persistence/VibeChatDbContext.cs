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
    public DbSet<ExternalIdentity> ExternalIdentities => Set<ExternalIdentity>();
    public DbSet<ScimToken> ScimTokens => Set<ScimToken>();
    public DbSet<ScimPrincipal> ScimPrincipals => Set<ScimPrincipal>();
    public DbSet<ScimGroup> ScimGroups => Set<ScimGroup>();
    public DbSet<ScimGroupMember> ScimGroupMembers => Set<ScimGroupMember>();
    public DbSet<ScimGroupMapping> ScimGroupMappings => Set<ScimGroupMapping>();
    public DbSet<ScimMembershipGrant> ScimMembershipGrants => Set<ScimMembershipGrant>();
    public DbSet<AccessRevocation> AccessRevocations => Set<AccessRevocation>();
    public DbSet<MemberPublicProfile> MemberPublicProfiles => Set<MemberPublicProfile>();
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
    public DbSet<ImportJobRecord> ImportJobs => Set<ImportJobRecord>();
    public DbSet<ImportIdMapRecord> ImportIdMaps => Set<ImportIdMapRecord>();
    public DbSet<ImportHistoricalPrincipalRecord> ImportHistoricalPrincipals => Set<ImportHistoricalPrincipalRecord>();
    public DbSet<SupportBundleRecord> SupportBundles => Set<SupportBundleRecord>();
    public DbSet<SupportRepairRecord> SupportRepairs => Set<SupportRepairRecord>();
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
    public DbSet<ContentAuditSettings> ContentAuditSettings => Set<ContentAuditSettings>();
    public DbSet<BrandSettings> BrandSettings => Set<BrandSettings>();
    public DbSet<DesktopDevice> DesktopDevices => Set<DesktopDevice>();
    public DbSet<AiUsageRecord> AiUsageRecords => Set<AiUsageRecord>();
    public DbSet<AiSettings> AiSettings => Set<AiSettings>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();
    public DbSet<DigestSchedule> DigestSchedules => Set<DigestSchedule>();
    public DbSet<DigestRun> DigestRuns => Set<DigestRun>();
    public DbSet<DigestItem> DigestItems => Set<DigestItem>();
    public DbSet<ChannelNotificationPreference> ChannelNotificationPreferences => Set<ChannelNotificationPreference>();
    public DbSet<TenantEmailSettings> TenantEmailSettings => Set<TenantEmailSettings>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();
    public DbSet<OutboundWebhookEndpoint> OutboundWebhookEndpoints => Set<OutboundWebhookEndpoint>();
    public DbSet<IntegrationBot> IntegrationBots => Set<IntegrationBot>();
    public DbSet<IntegrationBotToken> IntegrationBotTokens => Set<IntegrationBotToken>();
    public DbSet<IntegrationBotChannelScope> IntegrationBotChannelScopes => Set<IntegrationBotChannelScope>();
    public DbSet<InstalledPlugin> InstalledPlugins => Set<InstalledPlugin>();
    public DbSet<PluginHistoryGrant> PluginHistoryGrants => Set<PluginHistoryGrant>();
    public DbSet<PluginInteractiveOffer> PluginInteractiveOffers => Set<PluginInteractiveOffer>();
    public DbSet<PluginActionReceipt> PluginActionReceipts => Set<PluginActionReceipt>();
    public DbSet<PluginQuotaCounter> PluginQuotaCounters => Set<PluginQuotaCounter>();
    public DbSet<PluginRegistryInstall> PluginRegistryInstalls => Set<PluginRegistryInstall>();
    public DbSet<PluginRegistryQuarantine> PluginRegistryQuarantines => Set<PluginRegistryQuarantine>();
    public DbSet<Bridge> Bridges => Set<Bridge>();
    public DbSet<BridgeIdentity> BridgeIdentities => Set<BridgeIdentity>();
    public DbSet<BridgeEvent> BridgeEvents => Set<BridgeEvent>();
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
    public DbSet<Commitment> Commitments => Set<Commitment>();
    public DbSet<CommitmentChange> CommitmentChanges => Set<CommitmentChange>();
    public DbSet<MessageTask> MessageTasks => Set<MessageTask>();
    public DbSet<MessageTaskAssignee> MessageTaskAssignees => Set<MessageTaskAssignee>();
    public DbSet<MessageTaskChecklistItem> MessageTaskChecklistItems => Set<MessageTaskChecklistItem>();
    public DbSet<FormDefinition> FormDefinitions => Set<FormDefinition>();
    public DbSet<FormSchemaVersion> FormSchemaVersions => Set<FormSchemaVersion>();
    public DbSet<FormSubmission> FormSubmissions => Set<FormSubmission>();
    public DbSet<FormDecision> FormDecisions => Set<FormDecision>();
    public DbSet<AutomationDefinition> AutomationDefinitions => Set<AutomationDefinition>();
    public DbSet<AutomationVersion> AutomationVersions => Set<AutomationVersion>();
    public DbSet<AutomationRun> AutomationRuns => Set<AutomationRun>();
    public DbSet<AutomationStep> AutomationSteps => Set<AutomationStep>();
    public DbSet<IncidentRoom> IncidentRooms => Set<IncidentRoom>();
    public DbSet<IncidentResponder> IncidentResponders => Set<IncidentResponder>();
    public DbSet<IncidentPlaybookStep> IncidentSteps => Set<IncidentPlaybookStep>();
    public DbSet<IncidentTimelineEntry> IncidentTimeline => Set<IncidentTimelineEntry>();
    public DbSet<ConnectorInstallation> ConnectorInstallations => Set<ConnectorInstallation>();
    public DbSet<ConnectorEffect> ConnectorEffects => Set<ConnectorEffect>();
    public DbSet<ConnectorDelivery> ConnectorDeliveries => Set<ConnectorDelivery>();
    public DbSet<ConnectorCheckpoint> ConnectorCheckpoints => Set<ConnectorCheckpoint>();
    public DbSet<KnowledgeCollection> KnowledgeCollections => Set<KnowledgeCollection>();
    public DbSet<KnowledgePage> KnowledgePages => Set<KnowledgePage>();
    public DbSet<KnowledgePageVersion> KnowledgePageVersions => Set<KnowledgePageVersion>();
    public DbSet<SemanticChunk> SemanticChunks => Set<SemanticChunk>();
    public DbSet<SemanticSettings> SemanticSettings => Set<SemanticSettings>();
    public DbSet<SemanticUsage> SemanticUsage => Set<SemanticUsage>();
    public DbSet<InboxSnooze> InboxSnoozes => Set<InboxSnooze>();
    public DbSet<ScheduledMessage> ScheduledMessages => Set<ScheduledMessage>();
    public DbSet<Reminder> Reminders => Set<Reminder>();
    public DbSet<ThreadSubscription> ThreadSubscriptions => Set<ThreadSubscription>();
    public DbSet<ChannelTopic> ChannelTopics => Set<ChannelTopic>();
    public DbSet<TopicSubscription> TopicSubscriptions => Set<TopicSubscription>();
    public DbSet<Poll> Polls => Set<Poll>();
    public DbSet<PollOption> PollOptions => Set<PollOption>();
    public DbSet<PollVote> PollVotes => Set<PollVote>();
    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<AnnouncementAcknowledgement> AnnouncementAcknowledgements => Set<AnnouncementAcknowledgement>();
    public DbSet<ComplianceGrant> ComplianceGrants => Set<ComplianceGrant>();
    public DbSet<LegalHold> LegalHolds => Set<LegalHold>();
    public DbSet<DiscoveryCase> DiscoveryCases => Set<DiscoveryCase>();
    public DbSet<DiscoveryExportRecord> DiscoveryExports => Set<DiscoveryExportRecord>();
    public DbSet<CustodyEvent> CustodyEvents => Set<CustodyEvent>();
    public DbSet<DlpPolicy> DlpPolicies => Set<DlpPolicy>();
    public DbSet<DataLabel> DataLabels => Set<DataLabel>();
    public DbSet<LabelAssignment> LabelAssignments => Set<LabelAssignment>();
    public DbSet<DlpRule> DlpRules => Set<DlpRule>();
    public DbSet<DlpDetector> DlpDetectors => Set<DlpDetector>();
    public DbSet<DlpException> DlpExceptions => Set<DlpException>();
    public DbSet<DlpFalsePositive> DlpFalsePositives => Set<DlpFalsePositive>();
    public DbSet<DlpDecisionRecord> DlpDecisions => Set<DlpDecisionRecord>();
    public DbSet<SiemDestination> SiemDestinations => Set<SiemDestination>();
    public DbSet<SiemDelivery> SiemDeliveries => Set<SiemDelivery>();
    public DbSet<PolicyPackAssignment> PolicyPackAssignments => Set<PolicyPackAssignment>();
    public DbSet<ScopedRoleAssignment> ScopedRoleAssignments => Set<ScopedRoleAssignment>();
    public DbSet<ResourceQuota> ResourceQuotas => Set<ResourceQuota>();
    public DbSet<QuotaCounter> QuotaCounters => Set<QuotaCounter>();
    public DbSet<QuotaAlert> QuotaAlerts => Set<QuotaAlert>();

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
        ImportModelConfiguration.Configure(modelBuilder, tenantContext);
        SupportDiagnosticsConfiguration.Configure(modelBuilder, tenantContext);
        ScimModelConfiguration.Configure(modelBuilder, tenantContext);

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

        modelBuilder.Entity<ExternalIdentity>(entity =>
        {
            entity.ToTable("external_identities", "identity");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.UserId).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.Issuer).HasMaxLength(SsoBrokerClaimsReader.MaxIssuerLength);
            entity.Property(x => x.Subject).HasMaxLength(SsoBrokerClaimsReader.MaxSubjectLength);
            entity.Property(x => x.ProviderAlias).HasMaxLength(64);
            entity.Property(x => x.Reason).HasMaxLength(32);
            entity.HasIndex(x => new { x.Issuer, x.Subject }).IsUnique();
            entity.HasIndex(x => x.UserId);
        });

        modelBuilder.Entity<MemberPublicProfile>(entity =>
        {
            entity.ToTable("member_profiles", "identity");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.UserId).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.JobTitle).HasMaxLength(MemberProfileRules.MaxJobTitleLength);
            entity.Property(x => x.About).HasMaxLength(MemberProfileRules.MaxAboutLength);
            entity.Property(x => x.HighlightMessage).HasMaxLength(MemberProfileRules.MaxHighlightLength);
            entity.Property(x => x.AvatarObjectKey).HasMaxLength(512);
            entity.Property(x => x.AvatarContentType).HasMaxLength(64);
            entity.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
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

        modelBuilder.Entity<DesktopDevice>(entity =>
        {
            entity.ToTable("desktop_devices", "identity");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.UserId).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.Platform).HasMaxLength(32);
            entity.Property(x => x.AppVersion).HasMaxLength(32);
            entity.HasIndex(x => new { x.TenantId, x.UserId });
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
            entity.Property(x => x.Layout).HasConversion<string>().HasMaxLength(16).HasDefaultValue(ChannelLayout.Stream);
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

        modelBuilder.Entity<ChannelTopic>(entity =>
        {
            entity.ToTable("channel_topics", "conversations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.ChannelId).HasConversion(v => v.Value, v => new ChannelId(v));
            entity.Property(x => x.ConversationId).HasConversion(v => v.Value, v => new ChannelId(v));
            entity.Property(x => x.CreatedBy).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.ClosedBy).HasConversion(v => v.HasValue ? v.Value.Value : (Guid?)null, v => v.HasValue ? new UserId(v.Value) : null);
            entity.Property(x => x.MovedFromChannelId).HasConversion(v => v.HasValue ? v.Value.Value : (Guid?)null, v => v.HasValue ? new ChannelId(v.Value) : null);
            entity.Property(x => x.Title).HasMaxLength(TopicPolicies.MaxTitleLength);
            entity.Property(x => x.Slug).HasMaxLength(TopicPolicies.MaxSlugLength);
            entity.Property(x => x.Tags).HasMaxLength(320);
            entity.Property(x => x.ParticipantIds).HasMaxLength(400);
            entity.Property(x => x.State).HasConversion<string>().HasMaxLength(16);
            entity.HasIndex(x => new { x.TenantId, x.ChannelId, x.Slug }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.ChannelId, x.LastActivityAt });
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<TopicSubscription>(entity =>
        {
            entity.ToTable("topic_subscriptions", "messaging");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.UserId).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.ChannelId).HasConversion(v => v.Value, v => new ChannelId(v));
            entity.HasIndex(x => new { x.TenantId, x.UserId, x.TopicId }).IsUnique();
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
            entity.Property(x => x.ScanStatus).HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.ScanEngine).HasMaxLength(32);
            entity.Property(x => x.ScanSignatureVersion).HasMaxLength(32);
            entity.Property(x => x.ScanReason).HasMaxLength(64);
            entity.Property(x => x.ScanAttempts).HasDefaultValue(0);
            entity.HasIndex(x => x.StorageKey);
            entity.HasIndex(x => new { x.TenantId, x.ChannelId, x.MessageId });
            entity.HasIndex(x => new { x.TenantId, x.ScanStatus });
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

        modelBuilder.Entity<Commitment>(entity =>
        {
            entity.ToTable("commitments", "messaging");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.ChannelId).HasConversion(v => v.Value, v => new ChannelId(v));
            entity.Property(x => x.SourceMessageId).HasConversion(v => v.Value, v => new MessageId(v));
            entity.Property(x => x.OwnerUserId).HasConversion(
                v => v.HasValue ? v.Value.Value : (Guid?)null,
                v => v.HasValue ? new UserId(v.Value) : null);
            entity.Property(x => x.CreatedBy).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.ClosedBy).HasConversion(
                v => v.HasValue ? v.Value.Value : (Guid?)null,
                v => v.HasValue ? new UserId(v.Value) : null);
            entity.Property(x => x.Title).HasMaxLength(CommitmentPolicies.MaxTitleLength);
            entity.Property(x => x.Description).HasMaxLength(CommitmentPolicies.MaxDescriptionLength);
            entity.Property(x => x.ExcerptHash).HasMaxLength(64);
            entity.Property(x => x.Method).HasMaxLength(32);
            entity.Property(x => x.AuthzSnapshot).HasMaxLength(CommitmentPolicies.MaxAuthzSnapshotLength);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(CommitmentPolicies.MaxIdempotencyKeyLength);
            entity.HasIndex(x => new { x.TenantId, x.CreatedBy, x.IdempotencyKey }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.WorkspaceId, x.State, x.CreatedAt });
            entity.HasIndex(x => new { x.TenantId, x.OwnerUserId, x.State });
            entity.HasIndex(x => new { x.TenantId, x.SourceMessageId });
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<CommitmentChange>(entity =>
        {
            entity.ToTable("commitment_changes", "messaging");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.ActorUserId).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.Action).HasMaxLength(32);
            entity.HasIndex(x => new { x.TenantId, x.CommitmentId, x.At });
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<MessageTask>(entity =>
        {
            entity.ToTable("tasks", "messaging");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.ChannelId).HasConversion(v => v.Value, v => new ChannelId(v));
            entity.Property(x => x.SourceMessageId).HasConversion(v => v.Value, v => new MessageId(v));
            entity.Property(x => x.CreatedBy).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.CompletedBy).HasConversion(
                v => v.HasValue ? v.Value.Value : (Guid?)null,
                v => v.HasValue ? new UserId(v.Value) : null);
            entity.Property(x => x.Title).HasMaxLength(TaskPolicies.MaxTitleLength);
            entity.Property(x => x.Description).HasMaxLength(TaskPolicies.MaxDescriptionLength);
            entity.Property(x => x.ExcerptHash).HasMaxLength(64);
            entity.Property(x => x.Method).HasMaxLength(32);
            entity.Property(x => x.AuthzSnapshot).HasMaxLength(TaskPolicies.MaxAuthzSnapshotLength);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(TaskPolicies.MaxIdempotencyKeyLength);
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.HasIndex(x => new { x.TenantId, x.CreatedBy, x.IdempotencyKey }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.WorkspaceId, x.State, x.CreatedAt });
            entity.HasIndex(x => new { x.TenantId, x.SourceMessageId });
            entity.HasIndex(x => new { x.DueAt, x.OverdueNotifiedAt });
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<MessageTaskAssignee>(entity =>
        {
            entity.ToTable("task_assignees", "messaging");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.UserId).HasConversion(v => v.Value, v => new UserId(v));
            entity.HasIndex(x => new { x.TenantId, x.TaskId, x.UserId }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.UserId });
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<MessageTaskChecklistItem>(entity =>
        {
            entity.ToTable("task_checklist_items", "messaging");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.Text).HasMaxLength(TaskPolicies.MaxChecklistTextLength);
            entity.HasIndex(x => new { x.TenantId, x.TaskId, x.Position });
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<FormDefinition>(entity =>
        {
            entity.ToTable("forms", "messaging");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.ChannelId).HasConversion(v => v.Value, v => new ChannelId(v));
            entity.Property(x => x.CreatedBy).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.Title).HasMaxLength(FormPolicies.MaxTitleLength);
            entity.Property(x => x.DraftSchemaJson).HasColumnType("jsonb");
            entity.Property(x => x.IdempotencyKey).HasMaxLength(FormPolicies.MaxIdempotencyKeyLength);
            entity.HasIndex(x => new { x.TenantId, x.CreatedBy, x.IdempotencyKey }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.ChannelId, x.Status, x.UpdatedAt });
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<FormSchemaVersion>(entity =>
        {
            entity.ToTable("form_versions", "messaging");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.PublishedBy).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.SchemaJson).HasColumnType("jsonb");
            entity.HasIndex(x => new { x.TenantId, x.FormId, x.Version }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<FormSubmission>(entity =>
        {
            entity.ToTable("form_submissions", "messaging");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.SubmittedBy).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.AnswersJson).HasColumnType("jsonb");
            entity.Property(x => x.IdempotencyKey).HasMaxLength(FormPolicies.MaxIdempotencyKeyLength);
            entity.Property(x => x.TitleSnapshot).HasMaxLength(FormPolicies.MaxTitleLength);
            entity.HasIndex(x => new { x.TenantId, x.SubmittedBy, x.IdempotencyKey }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.FormId, x.SubmittedAt });
            entity.HasIndex(x => new { x.TenantId, x.RetainUntil, x.AnswersPurged });
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<FormDecision>(entity =>
        {
            entity.ToTable("form_decisions", "messaging");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.ActorUserId).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.Decision).HasMaxLength(16);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(FormPolicies.MaxIdempotencyKeyLength);
            entity.HasIndex(x => new { x.TenantId, x.ActorUserId, x.IdempotencyKey }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.SubmissionId, x.ActorUserId }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.SubmissionId, x.Version });
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<AutomationDefinition>(entity =>
        {
            entity.ToTable("definitions", "automation");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.CreatedBy).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.IdentityUserId).HasConversion(
                v => v.HasValue ? v.Value.Value : (Guid?)null,
                v => v.HasValue ? new UserId(v.Value) : null);
            entity.Property(x => x.Name).HasMaxLength(AutomationPolicies.MaxNameLength);
            entity.Property(x => x.DraftJson).HasColumnType("jsonb");
            entity.Property(x => x.TriggerType).HasMaxLength(32);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(AutomationPolicies.MaxIdempotencyKeyLength);
            entity.Property(x => x.WebhookTokenHash).HasMaxLength(64);
            entity.HasIndex(x => new { x.TenantId, x.CreatedBy, x.IdempotencyKey }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.WorkspaceId, x.Enabled, x.UpdatedAt });
            entity.HasIndex(x => new { x.TriggerType, x.NextRunAt });
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<AutomationVersion>(entity =>
        {
            entity.ToTable("versions", "automation");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.PublishedBy).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.DefinitionJson).HasColumnType("jsonb");
            entity.Property(x => x.CapabilitiesJson).HasColumnType("jsonb");
            entity.HasIndex(x => new { x.TenantId, x.AutomationId, x.Version }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<AutomationRun>(entity =>
        {
            entity.ToTable("runs", "automation");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.EventId).HasMaxLength(80);
            entity.Property(x => x.EventJson).HasColumnType("jsonb");
            entity.Property(x => x.IdempotencyKey).HasMaxLength(64);
            entity.Property(x => x.ErrorCode).HasMaxLength(40);
            entity.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.AutomationId, x.CreatedAt });
            entity.HasIndex(x => new { x.Status, x.CreatedAt });
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<AutomationStep>(entity =>
        {
            entity.ToTable("steps", "automation");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.ActionType).HasMaxLength(32);
            entity.Property(x => x.ErrorCode).HasMaxLength(40);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(64);
            entity.Property(x => x.LogJson).HasColumnType("jsonb");
            entity.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.RunId, x.ActionIndex });
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<IncidentRoom>(entity =>
        {
            entity.ToTable("rooms", "incident");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.ChannelId).HasConversion(v => v.Value, v => new ChannelId(v));
            entity.Property(x => x.CommanderUserId).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.CreatedBy).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.TemplateKey).HasMaxLength(16);
            entity.Property(x => x.Title).HasMaxLength(IncidentPolicies.MaxTitleLength);
            entity.Property(x => x.Severity).HasMaxLength(8);
            entity.Property(x => x.Status).HasMaxLength(16);
            entity.Property(x => x.CorrelationId).HasMaxLength(IncidentPolicies.MaxCorrelationLength);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(IncidentPolicies.MaxIdempotencyKeyLength);
            entity.Property(x => x.PostmortemJson).HasColumnType("jsonb");
            entity.HasIndex(x => new { x.TenantId, x.CreatedBy, x.IdempotencyKey }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.ChannelId }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.WorkspaceId, x.Status });
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<IncidentResponder>(entity =>
        {
            entity.ToTable("responders", "incident");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.UserId).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.Role).HasMaxLength(16);
            entity.HasIndex(x => new { x.TenantId, x.IncidentId, x.UserId }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<IncidentPlaybookStep>(entity =>
        {
            entity.ToTable("steps", "incident");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.OwnerUserId).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.Code).HasMaxLength(32);
            entity.Property(x => x.Status).HasMaxLength(16);
            entity.Property(x => x.EvidenceKind).HasMaxLength(16);
            entity.Property(x => x.ErrorCode).HasMaxLength(40);
            entity.HasIndex(x => new { x.TenantId, x.IncidentId, x.StepIndex }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<IncidentTimelineEntry>(entity =>
        {
            entity.ToTable("timeline", "incident");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.Kind).HasMaxLength(16);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(IncidentPolicies.MaxIdempotencyKeyLength);
            entity.HasIndex(x => new { x.TenantId, x.IncidentId, x.IdempotencyKey }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.IncidentId, x.Kind, x.SourceId });
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<ConnectorInstallation>(entity =>
        {
            entity.ToTable("installations", "connectors");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.CreatedBy).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.IdentityUserId).HasConversion(
                v => v.HasValue ? v.Value.Value : (Guid?)null,
                v => v.HasValue ? new UserId(v.Value) : null);
            entity.Property(x => x.Provider).HasMaxLength(16);
            entity.Property(x => x.OpaqueKey).HasMaxLength(64);
            entity.Property(x => x.Status).HasMaxLength(16);
            entity.Property(x => x.DestinationKind).HasMaxLength(16);
            entity.Property(x => x.Scopes).HasColumnType("text[]").IsRequired();
            entity.Property(x => x.AllowlistHosts).HasColumnType("text[]").IsRequired();
            entity.Property(x => x.BaseUrl).HasMaxLength(200);
            entity.Property(x => x.ClientId).HasMaxLength(80);
            entity.Property(x => x.OauthStateHash).HasMaxLength(64);
            entity.Property(x => x.Circuit).HasMaxLength(16);
            entity.Property(x => x.LastErrorCode).HasMaxLength(40);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(ConnectorPolicies.MaxIdempotencyKeyLength);
            MapEncryptedSecret(entity.OwnsOne(x => x.AccessToken), "AccessToken");
            MapEncryptedSecret(entity.OwnsOne(x => x.RefreshToken), "RefreshToken");
            MapEncryptedSecret(entity.OwnsOne(x => x.OauthVerifier), "OauthVerifier");
            entity.HasIndex(x => x.OpaqueKey).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.CreatedBy, x.IdempotencyKey }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.WorkspaceId, x.Status });
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<ConnectorEffect>(entity =>
        {
            entity.ToTable("effects", "connectors");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.ExternalEventId).HasMaxLength(ConnectorPolicies.MaxExternalIdLength);
            entity.Property(x => x.DestinationKind).HasMaxLength(16);
            entity.HasIndex(x => new { x.InstallationId, x.ExternalEventId }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<ConnectorDelivery>(entity =>
        {
            entity.ToTable("deliveries", "connectors");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.Direction).HasMaxLength(16);
            entity.Property(x => x.Status).HasMaxLength(16);
            entity.Property(x => x.ErrorCode).HasMaxLength(40);
            entity.HasIndex(x => new { x.TenantId, x.InstallationId, x.CreatedAt });
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<ConnectorCheckpoint>(entity =>
        {
            entity.ToTable("checkpoints", "connectors");
            entity.HasKey(x => x.InstallationId);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.Cursor).HasMaxLength(80);
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<KnowledgeCollection>(entity =>
        {
            entity.ToTable("collections", "knowledge");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.CreatedBy).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.Title).HasMaxLength(KnowledgePolicies.MaxTitleLength);
            entity.Property(x => x.Slug).HasMaxLength(KnowledgePolicies.MaxSlugLength);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(KnowledgePolicies.MaxIdempotencyKeyLength);
            entity.HasIndex(x => new { x.TenantId, x.CreatedBy, x.IdempotencyKey }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.WorkspaceId, x.ScopeKind, x.ScopeId, x.Slug })
                .IsUnique()
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<KnowledgePage>(entity =>
        {
            entity.ToTable("pages", "knowledge");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.CreatedBy).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.UpdatedBy).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.Title).HasMaxLength(KnowledgePolicies.MaxTitleLength);
            entity.Property(x => x.Slug).HasMaxLength(KnowledgePolicies.MaxSlugLength);
            entity.Property(x => x.Body).HasMaxLength(KnowledgePolicies.MaxBodyLength);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(KnowledgePolicies.MaxIdempotencyKeyLength);
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.HasIndex(x => new { x.TenantId, x.CreatedBy, x.IdempotencyKey }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.CollectionId, x.Slug })
                .IsUnique()
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(x => new { x.TenantId, x.WorkspaceId, x.DeletedAt });
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<KnowledgePageVersion>(entity =>
        {
            entity.ToTable("page_versions", "knowledge");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.ActorUserId).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.Title).HasMaxLength(KnowledgePolicies.MaxTitleLength);
            entity.Property(x => x.Body).HasMaxLength(KnowledgePolicies.MaxBodyLength);
            entity.Property(x => x.Action).HasMaxLength(32);
            entity.HasIndex(x => new { x.TenantId, x.PageId, x.Version }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<SemanticChunk>(entity =>
        {
            entity.ToTable("semantic_chunks", "search");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.Text).HasMaxLength(SemanticPolicies.ChunkLength);
            entity.Property(x => x.ContentHash).HasMaxLength(64);
            entity.Property(x => x.Embedding).HasMaxLength(400);
            entity.HasIndex(x => new { x.TenantId, x.SourceKind, x.SourceId, x.ChunkIndex }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.WorkspaceId, x.DeletedAt });
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<SemanticSettings>(entity =>
        {
            entity.ToTable("semantic_settings", "search");
            entity.HasKey(x => x.WorkspaceId);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.HasIndex(x => x.TenantId);
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<SemanticUsage>(entity =>
        {
            entity.ToTable("semantic_usage", "search");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.UserId).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.Outcome).HasMaxLength(32);
            entity.HasIndex(x => new { x.TenantId, x.WorkspaceId, x.UserId, x.CreatedAt });
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<InboxSnooze>(entity =>
        {
            entity.ToTable("inbox_snoozes", "messaging");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.UserId).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.ItemKey).HasMaxLength(InboxPolicies.MaxItemKeyLength);
            entity.HasIndex(x => new { x.TenantId, x.UserId, x.ItemKey }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.UserId, x.WorkspaceId, x.SnoozeUntil });
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

        modelBuilder.Entity<ContentAuditSettings>(entity =>
        {
            entity.ToTable("content_audit_settings", "audit");
            entity.HasKey(x => x.TenantId);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.Enabled).HasDefaultValue(true);
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<BrandSettings>(entity =>
        {
            entity.ToTable("brand_settings", "directory");
            entity.HasKey(x => x.TenantId);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.DisplayName).HasMaxLength(BrandingRules.MaxNameLength);
            entity.Property(x => x.BrandColor).HasMaxLength(7);
            entity.Property(x => x.AccentColor).HasMaxLength(7);
            entity.Property(x => x.LogoLightKey).HasMaxLength(200);
            entity.Property(x => x.LogoDarkKey).HasMaxLength(200);
            entity.Property(x => x.FaviconKey).HasMaxLength(200);
            entity.Property(x => x.LogoLightContentType).HasMaxLength(80);
            entity.Property(x => x.LogoDarkContentType).HasMaxLength(80);
            entity.Property(x => x.FaviconContentType).HasMaxLength(80);
            entity.Property(x => x.PreviousJson).HasColumnType("jsonb");
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

        modelBuilder.Entity<DigestSchedule>(entity =>
        {
            entity.ToTable("digest_schedules", "notifications");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.UserId).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.Cadence).HasMaxLength(16);
            entity.Property(x => x.TimeZone).HasMaxLength(64);
            entity.Property(x => x.Delivery).HasMaxLength(16);
            entity.Property(x => x.ChannelIds).HasColumnType("uuid[]");
            entity.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<DigestRun>(entity =>
        {
            entity.ToTable("digest_runs", "notifications");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.UserId).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.PeriodKey).HasMaxLength(80);
            entity.Property(x => x.Cadence).HasMaxLength(16);
            entity.Property(x => x.Status).HasMaxLength(16);
            entity.Property(x => x.AiStatus).HasMaxLength(16);
            entity.Property(x => x.Summary).HasMaxLength(500);
            entity.Property(x => x.Delivery).HasMaxLength(16);
            entity.HasIndex(x => new { x.TenantId, x.UserId, x.WorkspaceId, x.PeriodKey }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<DigestItem>(entity =>
        {
            entity.ToTable("digest_items", "notifications");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.Kind).HasMaxLength(24);
            entity.HasIndex(x => new { x.RunId, x.Kind, x.SourceId }).IsUnique();
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
            entity.Property(x => x.CallbackUrl).HasMaxLength(PluginManifestRules.MaxCallbackUrlLength);
            entity.Property(x => x.SubscribedEvents).HasColumnType("text[]").IsRequired();
            entity.Property(x => x.Hooks).HasColumnType("text[]").IsRequired();
            MapEncryptedSecret(entity.OwnsOne(x => x.CallbackSecret), "CallbackSecret");
            entity.HasIndex(x => x.BotId).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.WorkspaceId, x.PluginId }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<PluginHistoryGrant>(entity =>
        {
            entity.ToTable("plugin_history_grants", "integrations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.ChannelId).HasConversion(v => v.Value, v => new ChannelId(v));
            entity.Property(x => x.GrantedBy).HasConversion(v => v.Value, v => new UserId(v));
            entity.HasIndex(x => new { x.InstalledPluginId, x.ChannelId });
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<PluginInteractiveOffer>(entity =>
        {
            entity.ToTable("plugin_interactive_offers", "integrations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.ChannelId).HasConversion(v => v.Value, v => new ChannelId(v));
            entity.Property(x => x.MessageId).HasConversion(v => v.Value, v => new MessageId(v));
            entity.Property(x => x.PluginSlug).HasMaxLength(PluginManifestRules.MaxPluginIdLength).IsRequired();
            entity.Property(x => x.PluginName).HasMaxLength(BotIntegrationPolicies.MaxNameLength).IsRequired();
            entity.Property(x => x.Actions).HasColumnType("text[]").IsRequired();
            entity.Property(x => x.IdempotencyKey).HasMaxLength(80).IsRequired();
            entity.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => new { x.TenantId, x.InstalledPluginId, x.IdempotencyKey }).IsUnique();
            entity.HasIndex(x => x.MessageId);
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<PluginActionReceipt>(entity =>
        {
            entity.ToTable("plugin_action_receipts", "integrations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.MessageId).HasConversion(v => v.Value, v => new MessageId(v));
            entity.Property(x => x.ChannelId).HasConversion(v => v.Value, v => new ChannelId(v));
            entity.Property(x => x.ActorUserId).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.IdempotencyKey).HasMaxLength(80).IsRequired();
            entity.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Action).HasMaxLength(96).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(16).IsRequired();
            entity.HasIndex(x => new { x.TenantId, x.InstalledPluginId, x.IdempotencyKey }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<PluginQuotaCounter>(entity =>
        {
            entity.ToTable("plugin_quota_counters", "integrations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.Capability).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => new { x.TenantId, x.InstalledPluginId, x.Capability, x.Day }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<PluginRegistryInstall>(entity =>
        {
            entity.ToTable("plugin_registry_installs", "integrations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.PluginId).HasMaxLength(PluginManifestRules.MaxPluginIdLength).IsRequired();
            entity.Property(x => x.Version).HasMaxLength(PluginManifestRules.MaxVersionLength).IsRequired();
            entity.Property(x => x.PreviousVersion).HasMaxLength(PluginManifestRules.MaxVersionLength);
            entity.Property(x => x.Payload).HasMaxLength(PluginRegistryRules.MaxIndexBytes).IsRequired();
            entity.Property(x => x.Signature).HasMaxLength(512).IsRequired();
            entity.Property(x => x.PreviousPayload).HasMaxLength(PluginRegistryRules.MaxIndexBytes);
            entity.Property(x => x.PreviousSignature).HasMaxLength(512);
            entity.Property(x => x.Publisher).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Source).HasMaxLength(16).IsRequired();
            entity.Property(x => x.CatalogId).HasMaxLength(64).IsRequired();
            entity.Property(x => x.KeyId).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Checksum).HasMaxLength(80).IsRequired();
            entity.Property(x => x.ArtifactRef).HasMaxLength(512).IsRequired();
            entity.Property(x => x.Provenance).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Risk).HasMaxLength(16).IsRequired();
            entity.Property(x => x.Advisory).HasMaxLength(200);
            entity.Property(x => x.Capabilities).HasColumnType("text[]").IsRequired();
            entity.Property(x => x.GrantedCapabilities).HasColumnType("text[]").IsRequired();
            entity.Property(x => x.DependsOn).HasColumnType("text[]").IsRequired();
            entity.Property(x => x.Status).HasMaxLength(16).IsRequired();
            entity.Property(x => x.IdempotencyKey).HasMaxLength(PluginSdkPolicies.MaxIdempotencyLength);
            entity.Property(x => x.RequestHash).HasMaxLength(64);
            entity.HasIndex(x => new { x.TenantId, x.WorkspaceId, x.PluginId }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<PluginRegistryQuarantine>(entity =>
        {
            entity.ToTable("plugin_registry_quarantine", "integrations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.PluginId).HasMaxLength(PluginManifestRules.MaxPluginIdLength).IsRequired();
            entity.Property(x => x.Version).HasMaxLength(PluginManifestRules.MaxVersionLength).IsRequired();
            entity.HasIndex(x => new { x.TenantId, x.WorkspaceId, x.PluginId, x.Version }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<Bridge>(entity =>
        {
            entity.ToTable("bridges", "integrations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.ChannelId).HasConversion(v => v.Value, v => new ChannelId(v));
            entity.Property(x => x.Network).HasMaxLength(40).IsRequired();
            entity.Property(x => x.RemoteRoom).HasMaxLength(80).IsRequired();
            entity.Property(x => x.DestinationHost).HasMaxLength(253).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(16).IsRequired();
            entity.Property(x => x.Capabilities).HasColumnType("text[]").IsRequired();
            entity.Property(x => x.IdempotencyKey).HasMaxLength(120);
            entity.Property(x => x.RequestHash).HasMaxLength(64);
            entity.HasIndex(x => new { x.TenantId, x.WorkspaceId, x.ChannelId }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.WorkspaceId, x.IdempotencyKey });
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<BridgeIdentity>(entity =>
        {
            entity.ToTable("bridge_identities", "integrations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.RemoteSubject).HasMaxLength(120).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(80).IsRequired();
            entity.HasIndex(x => new { x.TenantId, x.BridgeId, x.RemoteSubject }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<BridgeEvent>(entity =>
        {
            entity.ToTable("bridge_events", "integrations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.Direction).HasMaxLength(16).IsRequired();
            entity.Property(x => x.EventType).HasMaxLength(16).IsRequired();
            entity.Property(x => x.RemoteEventId).HasMaxLength(120).IsRequired();
            entity.Property(x => x.RemoteSubject).HasMaxLength(120).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Link).HasMaxLength(300);
            entity.Property(x => x.Outcome).HasMaxLength(16).IsRequired();
            entity.HasIndex(x => new { x.TenantId, x.BridgeId, x.RemoteEventId }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.LocalMessageId });
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

        modelBuilder.Entity<ComplianceGrant>(entity =>
        {
            entity.ToTable("grants", "compliance");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.UserId).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.GrantedBy).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.Capability).HasMaxLength(32);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(LegalHoldLimits.MaxIdempotencyKeyLength);
            entity.HasIndex(x => new { x.TenantId, x.WorkspaceId, x.UserId, x.Capability }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.GrantedBy, x.IdempotencyKey }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<LegalHold>(entity =>
        {
            entity.ToTable("legal_holds", "compliance");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.RequestedBy).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.ApprovedBy).HasConversion(
                v => v.HasValue ? v.Value.Value : (Guid?)null,
                v => v.HasValue ? new UserId(v.Value) : null);
            entity.Property(x => x.ReleaseRequestedBy).HasConversion(
                v => v.HasValue ? v.Value.Value : (Guid?)null,
                v => v.HasValue ? new UserId(v.Value) : null);
            entity.Property(x => x.ReleaseApprovedBy).HasConversion(
                v => v.HasValue ? v.Value.Value : (Guid?)null,
                v => v.HasValue ? new UserId(v.Value) : null);
            entity.Property(x => x.ScopeKind).HasMaxLength(32);
            entity.Property(x => x.Status).HasMaxLength(32);
            entity.Property(x => x.Reason).HasMaxLength(LegalHoldLimits.MaxReasonLength);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(LegalHoldLimits.MaxIdempotencyKeyLength);
            entity.HasIndex(x => new { x.TenantId, x.WorkspaceId, x.Status });
            entity.HasIndex(x => new { x.TenantId, x.RequestedBy, x.IdempotencyKey }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<DiscoveryCase>(entity =>
        {
            entity.ToTable("discovery_cases", "compliance");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.CreatedBy).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.Title).HasMaxLength(LegalHoldLimits.MaxTitleLength);
            entity.Property(x => x.Reason).HasMaxLength(LegalHoldLimits.MaxReasonLength);
            entity.Property(x => x.Status).HasMaxLength(16);
            entity.Property(x => x.QueryJson).HasColumnType("jsonb");
            entity.Property(x => x.IdempotencyKey).HasMaxLength(LegalHoldLimits.MaxIdempotencyKeyLength);
            entity.HasIndex(x => new { x.TenantId, x.CreatedBy, x.IdempotencyKey }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.WorkspaceId, x.CreatedAt });
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<DiscoveryExportRecord>(entity =>
        {
            entity.ToTable("discovery_exports", "compliance");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.CreatedBy).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.Status).HasMaxLength(16);
            entity.Property(x => x.ManifestJson).HasColumnType("text");
            entity.Property(x => x.Checksum).HasMaxLength(64);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(LegalHoldLimits.MaxIdempotencyKeyLength);
            entity.HasIndex(x => new { x.TenantId, x.CreatedBy, x.IdempotencyKey }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.CaseId, x.CreatedAt });
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<CustodyEvent>(entity =>
        {
            entity.ToTable("custody_events", "compliance");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.ActorUserId).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.Action).HasMaxLength(64);
            entity.Property(x => x.Reason).HasMaxLength(LegalHoldLimits.MaxReasonLength);
            entity.Property(x => x.PayloadHash).HasMaxLength(64);
            entity.Property(x => x.PreviousHash).HasMaxLength(64);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(LegalHoldLimits.MaxIdempotencyKeyLength);
            entity.HasIndex(x => new { x.TenantId, x.ActorUserId, x.IdempotencyKey }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.WorkspaceId, x.OccurredAt });
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<DlpPolicy>(entity =>
        {
            entity.ToTable("dlp_policies", "compliance");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.HasIndex(x => new { x.TenantId, x.WorkspaceId }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<DataLabel>(entity =>
        {
            entity.ToTable("data_labels", "compliance");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.CreatedBy).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.Key).HasMaxLength(DlpLimits.MaxKeyLength);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(DlpLimits.MaxIdempotencyKeyLength);
            entity.HasIndex(x => new { x.TenantId, x.WorkspaceId, x.Key }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.CreatedBy, x.IdempotencyKey }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<LabelAssignment>(entity =>
        {
            entity.ToTable("label_assignments", "compliance");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.UpdatedBy).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.ScopeKind).HasMaxLength(32);
            entity.Property(x => x.LabelKey).HasMaxLength(DlpLimits.MaxKeyLength);
            entity.HasIndex(x => new { x.TenantId, x.WorkspaceId, x.ScopeKind, x.ScopeId }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<DlpRule>(entity =>
        {
            entity.ToTable("dlp_rules", "compliance");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.Action).HasMaxLength(32);
            entity.Property(x => x.Effect).HasMaxLength(32);
            entity.HasIndex(x => new { x.TenantId, x.WorkspaceId, x.Action }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<DlpDetector>(entity =>
        {
            entity.ToTable("dlp_detectors", "compliance");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.CreatedBy).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.Name).HasMaxLength(DlpLimits.MaxNameLength);
            entity.Property(x => x.Pattern).HasMaxLength(DlpLimits.MaxPatternLength);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(DlpLimits.MaxIdempotencyKeyLength);
            entity.HasIndex(x => new { x.TenantId, x.CreatedBy, x.IdempotencyKey }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<DlpException>(entity =>
        {
            entity.ToTable("dlp_exceptions", "compliance");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.CreatedBy).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.Action).HasMaxLength(32);
            entity.Property(x => x.ScopeKind).HasMaxLength(32);
            entity.Property(x => x.Reason).HasMaxLength(DlpLimits.MaxReasonLength);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(DlpLimits.MaxIdempotencyKeyLength);
            entity.HasIndex(x => new { x.TenantId, x.CreatedBy, x.IdempotencyKey }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<DlpFalsePositive>(entity =>
        {
            entity.ToTable("dlp_false_positives", "compliance");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.ReviewedBy).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.Reason).HasMaxLength(DlpLimits.MaxReasonLength);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(DlpLimits.MaxIdempotencyKeyLength);
            entity.HasIndex(x => new { x.TenantId, x.WorkspaceId, x.DetectorId, x.SubjectId }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.ReviewedBy, x.IdempotencyKey }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<DlpDecisionRecord>(entity =>
        {
            entity.ToTable("dlp_decisions", "compliance");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.Action).HasMaxLength(32);
            entity.Property(x => x.Effect).HasMaxLength(32);
            entity.Property(x => x.ReasonCode).HasMaxLength(32);
            entity.Property(x => x.LabelKey).HasMaxLength(DlpLimits.MaxKeyLength);
            entity.HasIndex(x => new { x.TenantId, x.WorkspaceId, x.CreatedAt });
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<SiemDestination>(entity =>
        {
            entity.ToTable("siem_destinations", "compliance");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.Name).HasMaxLength(SiemLimits.MaxNameLength);
            entity.Property(x => x.Transport).HasMaxLength(32);
            entity.Property(x => x.Url).HasMaxLength(SiemLimits.MaxUrlLength);
            entity.Property(x => x.CategoriesJson).HasMaxLength(400);
            entity.Property(x => x.MinSeverity).HasMaxLength(16);
            entity.Property(x => x.Status).HasMaxLength(16);
            entity.Property(x => x.Circuit).HasMaxLength(16);
            entity.Property(x => x.LastError).HasMaxLength(64);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(SiemLimits.MaxIdempotencyKeyLength);
            entity.Property(x => x.RotateKey).HasMaxLength(SiemLimits.MaxIdempotencyKeyLength);
            entity.Property(x => x.TestKey).HasMaxLength(SiemLimits.MaxIdempotencyKeyLength);
            entity.Property(x => x.BackfillKey).HasMaxLength(SiemLimits.MaxIdempotencyKeyLength);
            entity.HasIndex(x => new { x.TenantId, x.WorkspaceId, x.IdempotencyKey }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
            MapEncryptedSecret(entity.OwnsOne(x => x.SigningSecret), "SigningSecret");
        });

        modelBuilder.Entity<SiemDelivery>(entity =>
        {
            entity.ToTable("siem_deliveries", "compliance");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.Status).HasMaxLength(16);
            entity.Property(x => x.ErrorCode).HasMaxLength(64);
            entity.HasIndex(x => new { x.DestinationId, x.EventId }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<PolicyPackAssignment>(entity =>
        {
            entity.ToTable("policy_assignments", "compliance");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.AppliedBy).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.PackId).HasMaxLength(64);
            entity.Property(x => x.OverridesJson).HasMaxLength(4000);
            entity.Property(x => x.EffectiveJson).HasMaxLength(12000);
            entity.Property(x => x.PreviousPackId).HasMaxLength(64);
            entity.Property(x => x.PreviousOverridesJson).HasMaxLength(4000);
            entity.Property(x => x.PreviousEffectiveJson).HasMaxLength(12000);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(PolicyPackLimits.MaxIdempotencyKeyLength);
            entity.Property(x => x.RequestHash).HasMaxLength(64);
            entity.HasIndex(x => new { x.TenantId, x.WorkspaceId }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<ScopedRoleAssignment>(entity =>
        {
            entity.ToTable("scoped_role_assignments", "compliance");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.GranteeId).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.GrantedBy).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.Scope).HasMaxLength(32);
            entity.Property(x => x.Reason).HasMaxLength(DelegatedAdminLimits.MaxReasonLength);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(DelegatedAdminLimits.MaxIdempotencyKeyLength);
            entity.Property(x => x.RequestHash).HasMaxLength(64);
            entity.Property(x => x.ResultJson).HasMaxLength(4000);
            entity.HasIndex(x => new { x.TenantId, x.GrantedBy, x.IdempotencyKey }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.WorkspaceId, x.GranteeId, x.Scope, x.SpaceKey })
                .IsUnique()
                .HasFilter("\"RevokedAt\" IS NULL");
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<ResourceQuota>(entity =>
        {
            entity.ToTable("quotas", "compliance");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.UpdatedBy).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.Resource).HasMaxLength(32);
            entity.HasIndex(x => new { x.TenantId, x.WorkspaceId, x.Resource }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<QuotaCounter>(entity =>
        {
            entity.ToTable("quota_counters", "compliance");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.Resource).HasMaxLength(32);
            entity.HasIndex(x => new { x.TenantId, x.WorkspaceId, x.Resource }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<QuotaAlert>(entity =>
        {
            entity.ToTable("quota_alerts", "compliance");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.Resource).HasMaxLength(32);
            entity.Property(x => x.Kind).HasMaxLength(32);
            entity.HasIndex(x => new { x.TenantId, x.WorkspaceId, x.CreatedAt });
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
