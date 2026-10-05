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

public sealed class SeedData(
    VibeChatDbContext dbContext,
    IClock clock,
    ILogger<SeedData> logger)
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

        var seededMemberships = new List<(UserId UserId, Role Role)>
        {
            (DemoUserId, Role.WorkspaceOwner),
            (AliceUserId, Role.Member),
            (BobUserId, Role.Member)
        };
        foreach (var (userId, role) in seededMemberships)
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

        if (!await dbContext.ProcessSettings.AnyAsync(x => x.Id == ProcessSettings.SingletonId, cancellationToken))
        {
            dbContext.ProcessSettings.Add(new ProcessSettings
            {
                Id = ProcessSettings.SingletonId,
                AiEnabled = true,
                EmailEnabled = false,
                MessageRetentionEnabled = true,
                PushEnabled = true,
                LinkPreviewEnabled = true,
                OpenRouterBaseUrl = ProcessSettingsDefaults.OpenRouterBaseUrl,
                RetentionDefaultDays = ProcessSettingsDefaults.RetentionDefaultDays,
                RetentionBatchSize = ProcessSettingsDefaults.RetentionBatchSize,
                RetentionIntervalMinutes = ProcessSettingsDefaults.RetentionIntervalMinutes,
                LinkPreviewTimeoutMs = ProcessSettingsDefaults.LinkPreviewTimeoutMs,
                VapidSubject = ProcessSettingsDefaults.VapidSubject,
                UpdatedAt = now
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Demo seed data ensured");
    }
}

/// <summary>
/// Creates the minimum tenant data required for the first staging login.
/// This deliberately contains no demo users, messages or AI fixtures.
/// </summary>
public sealed class InitialWorkspaceBootstrap(
    VibeChatDbContext dbContext,
    IClock clock,
    ILogger<InitialWorkspaceBootstrap> logger,
    IConfiguration configuration)
{
    public static readonly WorkspaceId WorkspaceId = new(Guid.Parse("a1000000-0000-0000-0000-000000000001"));
    public static readonly TenantId TenantId = new(WorkspaceId.Value);
    public static readonly UserId AdminUserId = new(Guid.Parse("a1000000-0000-0000-0000-000000000002"));
    public static readonly Guid GeneralSpaceId = Guid.Parse("a1000000-0000-0000-0000-000000000003");
    public static readonly ChannelId GeneralChannelId = new(Guid.Parse("a1000000-0000-0000-0000-000000000004"));

    public async Task EnsureAsync(CancellationToken cancellationToken)
    {
        var email = configuration["Bootstrap:InitialAdminEmail"]?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            throw new InvalidOperationException(
                "Bootstrap:InitialAdminEmail must be a valid non-empty email when Bootstrap:Enabled=true.");
        }

        var workspaceName = configuration["Bootstrap:WorkspaceName"]?.Trim();
        if (string.IsNullOrWhiteSpace(workspaceName))
        {
            workspaceName = "VibeChat Alpha";
        }

        var workspaceSlug = configuration["Bootstrap:WorkspaceSlug"]?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(workspaceSlug))
        {
            workspaceSlug = "vibechat-alpha";
        }
        if (workspaceSlug.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
        {
            throw new InvalidOperationException(
                "Bootstrap:WorkspaceSlug must contain only ASCII letters, numbers and hyphens.");
        }

        var now = clock.UtcNow;
        var workspace = await dbContext.Workspaces.IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.Id == WorkspaceId, cancellationToken);
        if (workspace is null)
        {
            dbContext.Workspaces.Add(new Workspace
            {
                Id = WorkspaceId,
                TenantId = TenantId,
                Name = workspaceName,
                Slug = workspaceSlug,
                AiEnabled = false,
                CreatedAt = now
            });
        }

        var admin = await dbContext.UserProfiles
            .FirstOrDefaultAsync(item => item.Id == AdminUserId || item.Email.ToLower() == email, cancellationToken);
        if (admin is null)
        {
            admin = new UserProfile
            {
                Id = AdminUserId,
                Subject = WorkspaceRolePolicies.PendingSubjectForEmail(email),
                Email = email,
                DisplayName = "Admin",
                CreatedAt = now,
                UpdatedAt = now
            };
            dbContext.UserProfiles.Add(admin);
        }

        if (!await dbContext.Spaces.IgnoreQueryFilters().AnyAsync(item => item.Id == GeneralSpaceId, cancellationToken))
        {
            dbContext.Spaces.Add(new Space
            {
                Id = GeneralSpaceId,
                TenantId = TenantId,
                WorkspaceId = WorkspaceId,
                Name = "Geral",
                Order = 0,
                CreatedAt = now
            });
        }

        if (!await dbContext.Channels.IgnoreQueryFilters().AnyAsync(item => item.Id == GeneralChannelId, cancellationToken))
        {
            dbContext.Channels.Add(new Channel
            {
                Id = GeneralChannelId,
                TenantId = TenantId,
                WorkspaceId = WorkspaceId,
                SpaceId = GeneralSpaceId,
                Name = "geral",
                Type = ChannelType.Public,
                CreatedAt = now,
                CreatedBy = admin.Id
            });
        }

        if (!await dbContext.WorkspaceMembers.IgnoreQueryFilters().AnyAsync(
                item => item.WorkspaceId == WorkspaceId && item.UserId == admin.Id,
                cancellationToken))
        {
            dbContext.WorkspaceMembers.Add(new WorkspaceMember
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                WorkspaceId = WorkspaceId,
                UserId = admin.Id,
                Role = Role.WorkspaceOwner,
                JoinedAt = now
            });
        }

        if (!await dbContext.ChannelMembers.IgnoreQueryFilters().AnyAsync(
                item => item.ChannelId == GeneralChannelId && item.UserId == admin.Id,
                cancellationToken))
        {
            dbContext.ChannelMembers.Add(new ChannelMember
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                ChannelId = GeneralChannelId,
                UserId = admin.Id,
                JoinedAt = now
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Initial workspace bootstrap ensured for {AdminEmail}", email);
    }
}
