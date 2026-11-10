using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VibeChat.AI;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Identity;
using VibeChat.Integrations;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;

namespace VibeChat.Infrastructure.Ai;

public sealed record AiBotCommandResult(int Status, string? Error, object? Body);

public sealed class AiBotCatalogService(
    VibeChatDbContext db,
    ITenantContext tenant,
    IAuditWriter audit,
    IOutboxWriter outbox,
    IIdempotencyStore idempotency,
    IClock clock,
    IOptions<AiBotOptions> options)
{
    public async Task<AiBotCommandResult> ListAsync(Workspace workspace, CancellationToken ct)
    {
        var rows = await db.AiBotDefinitions.AsNoTracking()
            .Where(x => x.WorkspaceId == workspace.Id)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);
        return Ok(rows.Select(Summary).ToArray());
    }

    public async Task<AiBotCommandResult> GetAsync(Workspace workspace, Guid botId, CancellationToken ct)
    {
        var bot = await FindAsync(workspace.Id, botId, ct);
        return bot is null
            ? Fail(404, AiBotErrors.BotNotFound)
            : Ok(await DetailAsync(bot, ct));
    }

    public Task<AiBotCommandResult> CreateAsync(
        Workspace workspace, UserId actor, AiBotDraft draft, string key, string hash, CancellationToken ct) =>
        MutateAsync(workspace, key, hash, async () =>
        {
            var count = await db.AiBotDefinitions.CountAsync(x => x.WorkspaceId == workspace.Id, ct);
            var quota = AiBotCatalogPolicies.QuotaError(count, 0, 0, creating: true, options.Value);
            if (quota is not null)
            {
                return Fail(409, quota);
            }

            if (!await ChannelsBelongAsync(workspace.Id, draft.ChannelIds, ct))
            {
                return Fail(400, AiBotErrors.InvalidChannelScope);
            }

            var now = clock.UtcNow;
            var bot = NewDefinition(workspace, actor, draft, now);
            db.AiBotDefinitions.Add(bot);
            db.AiModelBindings.Add(NewBinding(bot, AiBotCatalogPolicies.DraftVersion, draft));
            AddScopes(bot, AiBotCatalogPolicies.DraftVersion, draft.ChannelIds);
            AddPrincipal(workspace, bot, now);
            Touch(bot, actor, "ai.bot.create", now, version: null);
            await db.SaveChangesAsync(ct);
            return Ok(201, await DetailAsync(bot, ct));
        }, ct);

    public Task<AiBotCommandResult> UpdateAsync(
        Workspace workspace, UserId actor, Guid botId, int expectedRevision, AiBotDraft draft, string key, string hash, CancellationToken ct) =>
        MutateAsync(workspace, key, hash, async () =>
        {
            var bot = await FindTrackedAsync(workspace.Id, botId, ct);
            if (bot is null)
            {
                return Fail(404, AiBotErrors.BotNotFound);
            }

            if (bot.Revision != expectedRevision)
            {
                return Fail(409, AiBotErrors.BotRevisionConflict);
            }

            if (!await ChannelsBelongAsync(workspace.Id, draft.ChannelIds, ct))
            {
                return Fail(400, AiBotErrors.InvalidChannelScope);
            }

            var binding = await DraftBindingAsync(bot, ct);
            Apply(bot, binding, draft);
            bot.Revision++;
            bot.UpdatedAt = clock.UtcNow;
            await ReplaceDraftScopesAsync(bot, draft.ChannelIds, ct);
            Touch(bot, actor, "ai.bot.update", bot.UpdatedAt, version: null);
            await db.SaveChangesAsync(ct);
            return Ok(await DetailAsync(bot, ct));
        }, ct);

    public async Task<AiBotCommandResult> DiffAsync(Workspace workspace, Guid botId, int fromVersion, int toVersion, CancellationToken ct)
    {
        var bot = await FindAsync(workspace.Id, botId, ct);
        if (bot is null)
        {
            return Fail(404, AiBotErrors.BotNotFound);
        }

        var left = await SnapshotAsync(bot, fromVersion, ct);
        var right = await SnapshotAsync(bot, toVersion, ct);
        if (left is null || right is null)
        {
            return Fail(404, AiBotErrors.BotVersionNotFound);
        }

        return Ok(new
        {
            fromVersion,
            toVersion,
            changes = AiBotCatalogPolicies.Diff(left, right)
        });
    }

    public Task<AiBotCommandResult> PublishAsync(
        Workspace workspace, UserId actor, Guid botId, int expectedRevision, AiBotDraft draft, string key, string hash, CancellationToken ct) =>
        MutateAsync(workspace, key, hash, async () =>
        {
            var bot = await FindTrackedAsync(workspace.Id, botId, ct);
            if (bot is null)
            {
                return Fail(404, AiBotErrors.BotNotFound);
            }

            if (bot.Revision != expectedRevision)
            {
                return Fail(409, AiBotErrors.BotRevisionConflict);
            }

            if (!await ChannelsBelongAsync(workspace.Id, draft.ChannelIds, ct))
            {
                return Fail(400, AiBotErrors.InvalidChannelScope);
            }

            var budget = AiBotCatalogPolicies.QuotaError(0, await OtherBudgetAsync(workspace.Id, bot.Id, ct), draft.DailyBudgetUsd, false, options.Value);
            if (budget is not null)
            {
                return Fail(409, budget);
            }

            var binding = await DraftBindingAsync(bot, ct);
            Apply(bot, binding, draft);
            var numbers = await db.AiBotVersions.Where(x => x.BotId == bot.Id).Select(x => x.VersionNumber).ToListAsync(ct);
            var number = AiBotCatalogPolicies.NextVersion(numbers);
            var now = clock.UtcNow;
            var version = SnapshotVersion(bot, number, actor, now);
            db.AiBotVersions.Add(version);
            db.AiModelBindings.Add(NewBinding(bot, number, draft));
            AddScopes(bot, number, draft.ChannelIds);
            bot.Status = AiBotStatus.Published;
            bot.ActiveVersionNumber = number;
            bot.Revision++;
            bot.UpdatedAt = now;
            await SyncPrincipalAsync(bot, ct);
            Emit(bot, actor, PublicEventCatalog.BotPublished, number, draft.Provider, draft.ModelId, now);
            Touch(bot, actor, "ai.bot.publish", now, number);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                return Fail(409, AiBotErrors.BotRevisionConflict);
            }

            return Ok(await DetailAsync(bot, ct));
        }, ct);

    public Task<AiBotCommandResult> DisableAsync(
        Workspace workspace, UserId actor, Guid botId, int expectedRevision, string key, string hash, CancellationToken ct) =>
        MutateAsync(workspace, key, hash, async () =>
        {
            var bot = await FindTrackedAsync(workspace.Id, botId, ct);
            if (bot is null)
            {
                return Fail(404, AiBotErrors.BotNotFound);
            }

            if (bot.Revision != expectedRevision)
            {
                return Fail(409, AiBotErrors.BotRevisionConflict);
            }

            if (bot.Status != AiBotStatus.Published || bot.ActiveVersionNumber is null)
            {
                return Fail(409, AiBotErrors.BotNotPublished);
            }

            var binding = await ActiveBindingAsync(bot, ct);
            var now = clock.UtcNow;
            bot.Status = AiBotStatus.Disabled;
            bot.Revision++;
            bot.UpdatedAt = now;
            Emit(bot, actor, PublicEventCatalog.BotDisabled, bot.ActiveVersionNumber.Value, binding.Provider, binding.ModelId, now);
            Touch(bot, actor, "ai.bot.disable", now, bot.ActiveVersionNumber);
            await db.SaveChangesAsync(ct);
            return Ok(await DetailAsync(bot, ct));
        }, ct);

    public Task<AiBotCommandResult> RollbackAsync(
        Workspace workspace, UserId actor, Guid botId, int expectedRevision, int versionNumber, string key, string hash, CancellationToken ct) =>
        MutateAsync(workspace, key, hash, async () =>
        {
            var bot = await FindTrackedAsync(workspace.Id, botId, ct);
            if (bot is null)
            {
                return Fail(404, AiBotErrors.BotNotFound);
            }

            if (bot.Revision != expectedRevision)
            {
                return Fail(409, AiBotErrors.BotRevisionConflict);
            }

            var version = await db.AiBotVersions.FirstOrDefaultAsync(
                x => x.BotId == bot.Id && x.VersionNumber == versionNumber, ct);
            if (version is null)
            {
                return Fail(404, AiBotErrors.BotVersionNotFound);
            }

            if (bot.Status == AiBotStatus.Published && bot.ActiveVersionNumber == versionNumber)
            {
                return Ok(await DetailAsync(bot, ct));
            }

            var before = await db.AiBotVersions.Where(x => x.BotId == bot.Id).Select(x => x.VersionNumber).ToListAsync(ct);
            var binding = await db.AiModelBindings.FirstAsync(
                x => x.BotId == bot.Id && x.VersionNumber == versionNumber, ct);
            var scopes = await db.AiBotChannelScopes.Where(x => x.BotId == bot.Id && x.VersionNumber == versionNumber)
                .Select(x => x.ChannelId.Value).ToArrayAsync(ct);
            var draftBinding = await DraftBindingAsync(bot, ct);
            Apply(bot, draftBinding, FromVersion(version, binding, scopes));
            await ReplaceDraftScopesAsync(bot, scopes, ct);
            var now = clock.UtcNow;
            bot.Status = AiBotStatus.Published;
            bot.ActiveVersionNumber = versionNumber;
            bot.Revision++;
            bot.UpdatedAt = now;
            await SyncPrincipalAsync(bot, ct);
            Emit(bot, actor, PublicEventCatalog.BotRolledBack, versionNumber, binding.Provider, binding.ModelId, now);
            Touch(bot, actor, "ai.bot.rollback", now, versionNumber);
            await db.SaveChangesAsync(ct);
            var after = await db.AiBotVersions.Where(x => x.BotId == bot.Id).Select(x => x.VersionNumber).ToListAsync(ct);
            if (!AiBotCatalogPolicies.KeepsHistory(before, after))
            {
                return Fail(500, AiBotErrors.BotRevisionConflict);
            }

            return Ok(await DetailAsync(bot, ct));
        }, ct);

    public async Task<AiBotCommandResult> ListInvocableAsync(Workspace workspace, UserId actor, CancellationToken ct)
    {
        var channels = await MemberChannelsAsync(actor, ct);
        var bots = await db.AiBotDefinitions.AsNoTracking()
            .Where(x => x.WorkspaceId == workspace.Id && x.Status == AiBotStatus.Published)
            .ToListAsync(ct);
        var visible = new List<object>();
        foreach (var bot in bots)
        {
            if (bot.Status == AiBotStatus.Published && await InScopeAsync(bot, channels, ct))
            {
                visible.Add(new
                {
                    id = bot.Id,
                    name = bot.Name,
                    description = bot.Description,
                    avatarKey = bot.AvatarKey,
                    version = bot.ActiveVersionNumber,
                    principalUserId = bot.PrincipalUserId.Value
                });
            }
        }

        return Ok(visible);
    }

    public Task<AiBotCommandResult> InvokeAsync(
        Workspace workspace, UserId actor, Guid botId, string key, string hash, CancellationToken ct) =>
        MutateAsync(workspace, key, hash, async () =>
        {
            var bot = await FindAsync(workspace.Id, botId, ct);
            if (bot is null || !await InScopeAsync(bot, await MemberChannelsAsync(actor, ct), ct))
            {
                return Fail(404, AiBotErrors.BotNotFound);
            }

            var blocked = AiBotCatalogPolicies.InvocationBlock(bot.Status, bot.ActiveVersionNumber);
            if (blocked is not null)
            {
                return Fail(409, blocked);
            }

            return Ok(new
            {
                botId = bot.Id,
                versionNumber = bot.ActiveVersionNumber,
                principalUserId = bot.PrincipalUserId.Value,
                accepted = true
            });
        }, ct);

    private async Task<AiBotCommandResult> MutateAsync(
        Workspace workspace, string key, string hash, Func<Task<AiBotCommandResult>> action, CancellationToken ct)
    {
        tenant.SetTenant(workspace.TenantId);
        await RlsSession.EnsureAppliedAsync(db, tenant, ct);
        var storeKey = $"aibot:{workspace.Id.Value:N}:{key}";
        var existing = await idempotency.FindAsync(workspace.TenantId, storeKey, ct);
        if (existing is not null)
        {
            if (!string.Equals(existing.RequestHash, hash, StringComparison.Ordinal))
            {
                return Fail(409, AiBotErrors.IdempotencyConflict);
            }

            var envelope = JsonSerializer.Deserialize<StoredBody>(existing.ResultJson);
            return envelope is null
                ? Fail(409, AiBotErrors.IdempotencyConflict)
                : new AiBotCommandResult(envelope.Status, null, envelope.Body);
        }

        var result = await action();
        if (result.Error is null && result.Body is not null)
        {
            await idempotency.StoreAsync(new IdempotencyRecord(
                workspace.TenantId,
                storeKey,
                hash,
                JsonSerializer.Serialize(new StoredBody(result.Status, JsonSerializer.SerializeToElement(result.Body))),
                clock.UtcNow), ct);
            await db.SaveChangesAsync(ct);
        }

        if (result.Error is null)
        {
            await RlsSession.CommitAsync(db, ct);
        }

        return result;
    }

    private async Task<AiBotDefinition?> FindAsync(WorkspaceId workspaceId, Guid botId, CancellationToken ct) =>
        await db.AiBotDefinitions.AsNoTracking().FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == botId, ct);

    private async Task<AiBotDefinition?> FindTrackedAsync(WorkspaceId workspaceId, Guid botId, CancellationToken ct) =>
        await db.AiBotDefinitions.FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == botId, ct);

    private async Task<bool> ChannelsBelongAsync(WorkspaceId workspaceId, Guid[] ids, CancellationToken ct)
    {
        if (ids.Length == 0)
        {
            return true;
        }

        var typed = ids.Select(id => new ChannelId(id)).ToArray();
        var rows = await db.Channels.Where(x => x.WorkspaceId == workspaceId && typed.Contains(x.Id)).ToListAsync(ct);
        return rows.Count == ids.Length && rows.All(x => x.Type is not ChannelType.Direct and not ChannelType.GroupDm);
    }

    private async Task<decimal> OtherBudgetAsync(WorkspaceId workspaceId, Guid botId, CancellationToken ct)
    {
        var others = await db.AiBotDefinitions.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && x.Id != botId && x.Status == AiBotStatus.Published && x.ActiveVersionNumber != null)
            .ToListAsync(ct);
        decimal sum = 0;
        foreach (var other in others)
        {
            sum += await db.AiBotVersions.Where(x => x.BotId == other.Id && x.VersionNumber == other.ActiveVersionNumber)
                .Select(x => x.DailyBudgetUsd).FirstAsync(ct);
        }

        return sum;
    }

    private async Task<AiModelBinding> DraftBindingAsync(AiBotDefinition bot, CancellationToken ct) =>
        await db.AiModelBindings.FirstAsync(
            x => x.BotId == bot.Id && x.VersionNumber == AiBotCatalogPolicies.DraftVersion, ct);

    private async Task<AiModelBinding> ActiveBindingAsync(AiBotDefinition bot, CancellationToken ct) =>
        await db.AiModelBindings.FirstAsync(
            x => x.BotId == bot.Id && x.VersionNumber == bot.ActiveVersionNumber, ct);

    private async Task ReplaceDraftScopesAsync(AiBotDefinition bot, Guid[] channelIds, CancellationToken ct)
    {
        var current = await db.AiBotChannelScopes
            .Where(x => x.BotId == bot.Id && x.VersionNumber == AiBotCatalogPolicies.DraftVersion)
            .ToListAsync(ct);
        db.AiBotChannelScopes.RemoveRange(current);
        AddScopes(bot, AiBotCatalogPolicies.DraftVersion, channelIds);
    }

    private async Task<AiBotSnapshot?> SnapshotAsync(AiBotDefinition bot, int version, CancellationToken ct)
    {
        if (version == AiBotCatalogPolicies.DraftVersion)
        {
            var binding = await db.AiModelBindings.AsNoTracking().FirstAsync(
                x => x.BotId == bot.Id && x.VersionNumber == AiBotCatalogPolicies.DraftVersion, ct);
            var channels = await db.AiBotChannelScopes.AsNoTracking()
                .Where(x => x.BotId == bot.Id && x.VersionNumber == AiBotCatalogPolicies.DraftVersion)
                .Select(x => x.ChannelId.Value).ToArrayAsync(ct);
            return ToSnapshot(bot.Name, bot.Description, bot.AvatarKey, bot.AllowDms, bot.SystemPrompt,
                binding, channels, bot.SkillVersionIds, bot.KnowledgeGrantIds, bot.McpToolGrantIds,
                bot.GuardrailPolicyVersion, bot.MaxTurns, bot.MaxInputTokens, bot.MaxOutputTokens, bot.DailyBudgetUsd);
        }

        var row = await db.AiBotVersions.AsNoTracking().FirstOrDefaultAsync(
            x => x.BotId == bot.Id && x.VersionNumber == version, ct);
        if (row is null)
        {
            return null;
        }

        var publishedBinding = await db.AiModelBindings.AsNoTracking().FirstAsync(
            x => x.BotId == bot.Id && x.VersionNumber == version, ct);
        var publishedChannels = await db.AiBotChannelScopes.AsNoTracking()
            .Where(x => x.BotId == bot.Id && x.VersionNumber == version)
            .Select(x => x.ChannelId.Value).ToArrayAsync(ct);
        return ToSnapshot(row.Name, row.Description, row.AvatarKey, row.AllowDms, row.SystemPrompt,
            publishedBinding, publishedChannels, row.SkillVersionIds, row.KnowledgeGrantIds, row.McpToolGrantIds,
            row.GuardrailPolicyVersion, row.MaxTurns, row.MaxInputTokens, row.MaxOutputTokens, row.DailyBudgetUsd);
    }

    private async Task<bool> InScopeAsync(AiBotDefinition bot, IReadOnlySet<Guid> channels, CancellationToken ct)
    {
        if (bot.ActiveVersionNumber is null)
        {
            return false;
        }

        var version = await db.AiBotVersions.AsNoTracking().FirstAsync(
            x => x.BotId == bot.Id && x.VersionNumber == bot.ActiveVersionNumber, ct);
        if (version.AllowDms)
        {
            return true;
        }

        var scopes = await db.AiBotChannelScopes.AsNoTracking()
            .Where(x => x.BotId == bot.Id && x.VersionNumber == version.VersionNumber)
            .Select(x => x.ChannelId.Value).ToListAsync(ct);
        return scopes.Any(channels.Contains);
    }

    private async Task<HashSet<Guid>> MemberChannelsAsync(UserId actor, CancellationToken ct) =>
        (await db.ChannelMembers.AsNoTracking()
            .Where(x => x.UserId == actor && x.LeftAt == null)
            .Select(x => x.ChannelId.Value)
            .ToListAsync(ct)).ToHashSet();

    private async Task SyncPrincipalAsync(AiBotDefinition bot, CancellationToken ct)
    {
        var profile = await db.UserProfiles.FirstAsync(x => x.Id == bot.PrincipalUserId, ct);
        profile.DisplayName = bot.Name;
        profile.UpdatedAt = bot.UpdatedAt;
        var integration = await db.IntegrationBots.FirstAsync(x => x.Id == bot.IntegrationBotId, ct);
        integration.Name = bot.Name;
    }

    private async Task<object> DetailAsync(AiBotDefinition bot, CancellationToken ct)
    {
        var binding = await db.AiModelBindings.AsNoTracking().FirstAsync(
            x => x.BotId == bot.Id && x.VersionNumber == AiBotCatalogPolicies.DraftVersion, ct);
        var channels = await db.AiBotChannelScopes.AsNoTracking()
            .Where(x => x.BotId == bot.Id && x.VersionNumber == AiBotCatalogPolicies.DraftVersion)
            .Select(x => x.ChannelId.Value).ToArrayAsync(ct);
        var versions = await (
            from version in db.AiBotVersions.AsNoTracking()
            join model in db.AiModelBindings.AsNoTracking() on new { version.BotId, version.VersionNumber }
                equals new { model.BotId, model.VersionNumber }
            where version.BotId == bot.Id
            orderby version.VersionNumber
            select new { version.VersionNumber, version.PublishedAt, model.Provider, model.ModelId, version.Name }
        ).ToListAsync(ct);
        return new
        {
            id = bot.Id,
            principalUserId = bot.PrincipalUserId.Value,
            status = bot.Status.ToString(),
            revision = bot.Revision,
            activeVersion = bot.ActiveVersionNumber,
            draft = DraftBody(bot, binding, channels),
            versions
        };
    }

    private void Emit(AiBotDefinition bot, UserId actor, string eventType, int version, string provider, string modelId, DateTimeOffset now)
    {
        var payload = AiBotCatalogPolicies.EventDocument(
            eventType, bot.Id, bot.WorkspaceId.Value, bot.TenantId.Value, version, actor.Value,
            AiBotCatalogPolicies.PromptHash(bot.SystemPrompt), provider, modelId);
        var decision = PublicEventCatalog.Validate(eventType, 1, payload);
        if (!decision.Accepted)
        {
            throw new InvalidOperationException(decision.Reason);
        }

        outbox.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TenantId = bot.TenantId,
            Type = eventType,
            Payload = payload,
            OccurredAt = now
        });
    }

    private void Touch(AiBotDefinition bot, UserId actor, string action, DateTimeOffset now, int? version)
    {
        audit.Add(new AuditEvent
        {
            TenantId = bot.TenantId,
            ActorUserId = actor,
            Action = action,
            EntityType = "AiBot",
            EntityId = bot.Id.ToString(),
            MetadataJson = JsonSerializer.Serialize(new { version, status = bot.Status.ToString() }),
            OccurredAt = now
        });
    }

    private void AddPrincipal(Workspace workspace, AiBotDefinition bot, DateTimeOffset now)
    {
        db.UserProfiles.Add(new UserProfile
        {
            Id = bot.PrincipalUserId,
            Subject = $"bot:{bot.IntegrationBotId:N}",
            Email = $"bot+{bot.IntegrationBotId:N}@bots.vibechat.local",
            DisplayName = bot.Name,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.WorkspaceMembers.Add(new WorkspaceMember
        {
            Id = Guid.NewGuid(),
            TenantId = workspace.TenantId,
            WorkspaceId = workspace.Id,
            UserId = bot.PrincipalUserId,
            Role = Role.Bot,
            JoinedAt = now
        });
        db.IntegrationBots.Add(new IntegrationBot
        {
            Id = bot.IntegrationBotId,
            TenantId = workspace.TenantId,
            WorkspaceId = workspace.Id,
            UserId = bot.PrincipalUserId,
            Name = bot.Name,
            Enabled = true,
            AllowDms = false,
            CreatedAt = now
        });
    }

    private static AiBotDefinition NewDefinition(Workspace workspace, UserId actor, AiBotDraft draft, DateTimeOffset now)
    {
        var bot = new AiBotDefinition
        {
            Id = Guid.NewGuid(),
            TenantId = workspace.TenantId,
            WorkspaceId = workspace.Id,
            IntegrationBotId = Guid.NewGuid(),
            PrincipalUserId = UserId.New(),
            OwnerUserId = actor,
            Status = AiBotStatus.Draft,
            Revision = 1,
            CreatedAt = now,
            UpdatedAt = now
        };
        Apply(bot, null, draft);
        return bot;
    }

    private static void Apply(AiBotDefinition bot, AiModelBinding? binding, AiBotDraft draft)
    {
        bot.Name = draft.Name;
        bot.Description = draft.Description;
        bot.AvatarKey = draft.AvatarKey;
        bot.AllowDms = draft.AllowDms;
        bot.SystemPrompt = draft.SystemPrompt;
        bot.SkillVersionIds = draft.SkillVersionIds;
        bot.KnowledgeGrantIds = draft.KnowledgeGrantIds;
        bot.McpToolGrantIds = draft.McpToolGrantIds;
        bot.GuardrailPolicyVersion = draft.GuardrailPolicyVersion;
        bot.MaxTurns = draft.MaxTurns;
        bot.MaxInputTokens = draft.MaxInputTokens;
        bot.MaxOutputTokens = draft.MaxOutputTokens;
        bot.DailyBudgetUsd = draft.DailyBudgetUsd;
        if (binding is null)
        {
            return;
        }

        binding.Provider = draft.Provider;
        binding.ModelId = draft.ModelId;
        binding.ParametersJson = draft.ParametersJson;
        binding.MaxTokens = draft.MaxTokens;
        binding.TimeoutMs = draft.TimeoutMs;
        binding.FallbackModelId = draft.FallbackModelId;
        binding.DataClass = draft.DataClass;
    }

    private static AiModelBinding NewBinding(AiBotDefinition bot, int version, AiBotDraft draft) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = bot.TenantId,
        WorkspaceId = bot.WorkspaceId,
        BotId = bot.Id,
        VersionNumber = version,
        Provider = draft.Provider,
        ModelId = draft.ModelId,
        ParametersJson = draft.ParametersJson,
        MaxTokens = draft.MaxTokens,
        TimeoutMs = draft.TimeoutMs,
        FallbackModelId = draft.FallbackModelId,
        DataClass = draft.DataClass
    };

    private void AddScopes(AiBotDefinition bot, int version, Guid[] channelIds)
    {
        foreach (var channelId in channelIds)
        {
            db.AiBotChannelScopes.Add(new AiBotChannelScope
            {
                Id = Guid.NewGuid(),
                TenantId = bot.TenantId,
                WorkspaceId = bot.WorkspaceId,
                BotId = bot.Id,
                VersionNumber = version,
                ChannelId = new ChannelId(channelId)
            });
        }
    }

    private static AiBotVersion SnapshotVersion(AiBotDefinition bot, int number, UserId actor, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = bot.TenantId,
        WorkspaceId = bot.WorkspaceId,
        BotId = bot.Id,
        VersionNumber = number,
        Name = bot.Name,
        Description = bot.Description,
        AvatarKey = bot.AvatarKey,
        AllowDms = bot.AllowDms,
        SystemPrompt = bot.SystemPrompt,
        PromptSha256 = AiBotCatalogPolicies.PromptHash(bot.SystemPrompt),
        SkillVersionIds = bot.SkillVersionIds.ToArray(),
        KnowledgeGrantIds = bot.KnowledgeGrantIds.ToArray(),
        McpToolGrantIds = bot.McpToolGrantIds.ToArray(),
        GuardrailPolicyVersion = bot.GuardrailPolicyVersion,
        MaxTurns = bot.MaxTurns,
        MaxInputTokens = bot.MaxInputTokens,
        MaxOutputTokens = bot.MaxOutputTokens,
        DailyBudgetUsd = bot.DailyBudgetUsd,
        PublishedBy = actor,
        PublishedAt = now
    };

    private static AiBotDraft FromVersion(AiBotVersion version, AiModelBinding binding, Guid[] channels) => new(
        version.Name, version.Description, version.AvatarKey, version.AllowDms, version.SystemPrompt,
        binding.Provider, binding.ModelId, binding.ParametersJson, binding.MaxTokens, binding.TimeoutMs,
        binding.FallbackModelId, binding.DataClass, channels, version.SkillVersionIds.ToArray(),
        version.KnowledgeGrantIds.ToArray(), version.McpToolGrantIds.ToArray(), version.GuardrailPolicyVersion,
        version.MaxTurns, version.MaxInputTokens, version.MaxOutputTokens, version.DailyBudgetUsd);

    private static AiBotSnapshot ToSnapshot(
        string name, string description, string avatar, bool allowDms, string prompt, AiModelBinding binding,
        Guid[] channels, Guid[] skills, Guid[] knowledge, Guid[] tools, string? policy, int turns, int input,
        int output, decimal budget) => new(
        name, description, avatar, allowDms, prompt, binding.Provider, binding.ModelId, binding.ParametersJson,
        binding.FallbackModelId, binding.DataClass, channels, skills, knowledge, tools, policy, turns, input,
        output, budget, binding.TimeoutMs);

    private static object Summary(AiBotDefinition bot) => new
    {
        id = bot.Id,
        name = bot.Name,
        description = bot.Description,
        status = bot.Status.ToString(),
        revision = bot.Revision,
        activeVersion = bot.ActiveVersionNumber,
        principalUserId = bot.PrincipalUserId.Value
    };

    private static object DraftBody(AiBotDefinition bot, AiModelBinding binding, Guid[] channels) => new
    {
        name = bot.Name,
        description = bot.Description,
        avatarKey = bot.AvatarKey,
        allowDms = bot.AllowDms,
        systemPrompt = bot.SystemPrompt,
        provider = binding.Provider,
        modelId = binding.ModelId,
        parameters = JsonSerializer.Deserialize<JsonElement>(binding.ParametersJson),
        maxTokens = binding.MaxTokens,
        timeoutMs = binding.TimeoutMs,
        fallbackModelId = binding.FallbackModelId,
        dataClass = binding.DataClass,
        channelIds = channels,
        skillVersionIds = bot.SkillVersionIds,
        knowledgeGrantIds = bot.KnowledgeGrantIds,
        mcpToolGrantIds = bot.McpToolGrantIds,
        guardrailPolicyVersion = bot.GuardrailPolicyVersion,
        maxTurns = bot.MaxTurns,
        maxInputTokens = bot.MaxInputTokens,
        maxOutputTokens = bot.MaxOutputTokens,
        dailyBudgetUsd = bot.DailyBudgetUsd
    };

    private static AiBotCommandResult Ok(object body) => new(200, null, body);

    private static AiBotCommandResult Ok(int status, object body) => new(status, null, body);

    private static AiBotCommandResult Fail(int status, string error) => new(status, error, null);

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is Npgsql.PostgresException postgres && postgres.SqlState == "23505";

    private sealed record StoredBody(int Status, JsonElement Body);
}
