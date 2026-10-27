using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Identity;
using VibeChat.Integrations;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;

namespace VibeChat.Infrastructure;

/// <summary>
/// Enqueues and executes versioned automations (B-125). Effects stay behind the
/// published capability snapshot and the owner's current permissions.
/// </summary>
public sealed class AutomationService(
    VibeChatDbContext db,
    ITenantContext tenant,
    IOptionsMonitor<AutomationOptions> options,
    IClock clock,
    IAuditWriter audit,
    IOutboxWriter outbox,
    IMessageWriter messages,
    IOutboundWebhookDispatcher webhooks,
    ILogger<AutomationService> logger)
{
    public bool FlagOn => options.CurrentValue.Enabled;

    public async Task ObserveOutboxAsync(string type, string payload, CancellationToken ct)
    {
        if (!FlagOn || !AutomationPolicies.TryReadSignal(type, payload, out var signal) || signal is null)
        {
            return;
        }

        var resolved = await WithDepthAsync(signal, payload, ct);
        var definitions = await db.AutomationDefinitions
            .Where(row => row.Enabled && row.Status == AutomationStatus.Published)
            .OrderBy(row => row.UpdatedAt)
            .Take(AutomationPolicies.MaxScan)
            .ToListAsync(ct);
        foreach (var definition in definitions)
        {
            await TryEnqueueAsync(definition, resolved, AutomationRunMode.Live, execute: false, ct);
        }
    }

    public async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        if (!FlagOn)
        {
            return 0;
        }

        tenant.SetJobRole(AutomationPolicies.JobRole);
        await RlsSession.EnsureAppliedAsync(db, tenant, ct);
        var now = clock.UtcNow;
        var due = await db.AutomationDefinitions.IgnoreQueryFilters()
            .Where(row => row.Enabled
                && row.TriggerType == AutomationPolicies.TriggerSchedule
                && row.NextRunAt != null
                && row.NextRunAt <= now)
            .OrderBy(row => row.NextRunAt)
            .Take(20)
            .ToListAsync(ct);
        foreach (var group in due.GroupBy(row => row.TenantId))
        {
            tenant.SetTenant(group.Key);
            tenant.SetJobRole(AutomationPolicies.JobRole);
            await RlsSession.EnsureAppliedAsync(db, tenant, ct);
            foreach (var definition in group)
            {
                var version = await LatestVersionAsync(definition, ct);
                if (version is null || !AutomationPolicies.TryParse(version.DefinitionJson, out var model, out _) || model is null)
                {
                    continue;
                }

                var slot = definition.NextRunAt ?? now;
                var signal = new AutomationSignal(
                    AutomationPolicies.TriggerSchedule,
                    "fired",
                    $"sch:{definition.Id:N}:{slot.UtcTicks}",
                    null,
                    null,
                    null,
                    null,
                    "fired",
                    0,
                    null);
                await TryEnqueueAsync(definition, signal, AutomationRunMode.Live, execute: false, ct);
                definition.NextRunAt = now.AddMinutes(Math.Max(model.Trigger.IntervalMinutes, AutomationPolicies.MinIntervalMinutes));
            }

            await db.SaveChangesAsync(ct);
        }

        tenant.SetJobRole(AutomationPolicies.JobRole);
        tenant.SetTenant(TenantId.Empty);
        await RlsSession.EnsureAppliedAsync(db, tenant, ct);
        var pending = await db.AutomationRuns.IgnoreQueryFilters()
            .Where(row => row.Status == AutomationRunStatus.Pending && row.Mode != AutomationRunMode.DryRun)
            .OrderBy(row => row.CreatedAt)
            .Take(20)
            .ToListAsync(ct);
        var executed = 0;
        foreach (var group in pending.GroupBy(row => row.TenantId))
        {
            tenant.SetTenant(group.Key);
            tenant.SetJobRole(null);
            await RlsSession.EnsureAppliedAsync(db, tenant, ct);
            foreach (var run in group)
            {
                var definition = await db.AutomationDefinitions.FirstOrDefaultAsync(row => row.Id == run.AutomationId, ct);
                if (definition is null)
                {
                    continue;
                }

                await ExecuteAsync(definition, run, dryRun: false, ct);
                executed++;
            }

            await db.SaveChangesAsync(ct);
        }

        await RlsSession.CommitAsync(db, ct);
        if (executed > 0)
        {
            logger.LogInformation("Executed {Count} automation runs", executed);
        }

        return executed;
    }

    public async Task<AutomationRun?> TryEnqueueAsync(
        AutomationDefinition definition,
        AutomationSignal signal,
        AutomationRunMode mode,
        bool execute,
        CancellationToken ct)
    {
        if (!FlagOn)
        {
            return null;
        }

        var version = await LatestVersionAsync(definition, ct);
        if (version is null || !AutomationPolicies.TryParse(version.DefinitionJson, out var model, out _) || model is null)
        {
            return null;
        }

        if (!AutomationPolicies.Matches(model, signal))
        {
            return null;
        }

        var now = clock.UtcNow;
        var runsInWindow = await db.AutomationRuns.CountAsync(
            row => row.AutomationId == definition.Id
                && row.Mode != AutomationRunMode.DryRun
                && row.CreatedAt >= now.AddHours(-1),
            ct);
        if (!AutomationPolicies.TryAdmit(true, definition.Enabled || mode == AutomationRunMode.DryRun, definition.CircuitOpenUntil, runsInWindow, signal.Depth, now, out var block))
        {
            if (block is "CircuitOpen" or "RateLimited" or "LoopStopped")
            {
                Audit(signal, block switch
                {
                    "CircuitOpen" => AutomationPolicies.CircuitOpened,
                    "RateLimited" => AutomationPolicies.RateLimited,
                    _ => AutomationPolicies.LoopStopped,
                }, definition);
                await db.SaveChangesAsync(ct);
            }

            return null;
        }

        var key = AutomationPolicies.RunKey(definition.Id, version.Version, signal.EventId, mode);
        var existing = await db.AutomationRuns.FirstOrDefaultAsync(row => row.IdempotencyKey == key, ct);
        if (existing is not null)
        {
            return execute ? await ExecuteAsync(definition, existing, mode == AutomationRunMode.DryRun, ct) : existing;
        }

        var run = new AutomationRun
        {
            Id = Guid.NewGuid(),
            TenantId = definition.TenantId,
            AutomationId = definition.Id,
            Version = version.Version,
            EventId = signal.EventId,
            EventJson = signal.ToJson(),
            Depth = signal.Depth,
            Mode = mode,
            Status = AutomationRunStatus.Pending,
            IdempotencyKey = key,
            CausationRunId = signal.CausationRunId,
            CreatedAt = now,
        };
        db.AutomationRuns.Add(run);
        VibeChatMetrics.AutomationRuns.Add(1);
        await db.SaveChangesAsync(ct);
        return execute ? await ExecuteAsync(definition, run, mode == AutomationRunMode.DryRun, ct) : run;
    }

    public async Task<AutomationRun> ExecuteAsync(
        AutomationDefinition definition,
        AutomationRun run,
        bool dryRun,
        CancellationToken ct)
    {
        if (run.Status is AutomationRunStatus.Succeeded or AutomationRunStatus.Skipped or AutomationRunStatus.Stopped)
        {
            return run;
        }

        var version = await db.AutomationVersions.FirstOrDefaultAsync(
            row => row.AutomationId == definition.Id && row.Version == run.Version, ct);
        if (version is null || !AutomationPolicies.TryParse(version.DefinitionJson, out var model, out _) || model is null)
        {
            return Finish(definition, run, AutomationRunStatus.Failed, "InvalidDefinition", false);
        }

        var signal = ReadStoredSignal(run, model);
        if (!definition.Enabled && !dryRun)
        {
            return Finish(definition, run, AutomationRunStatus.Stopped, "AutomationDisabled", false);
        }

        if (!AutomationPolicies.ConditionsPass(model, signal))
        {
            await PlanStepsAsync(definition, run, model, AutomationStepStatus.Skipped, "ConditionNotMet", ct);
            return Finish(definition, run, AutomationRunStatus.Skipped, null, false);
        }

        if (dryRun)
        {
            await PlanStepsAsync(definition, run, model, AutomationStepStatus.Planned, null, ct);
            return Finish(definition, run, AutomationRunStatus.Succeeded, null, false);
        }

        var granted = await CurrentCapabilitiesAsync(definition, version, ct);
        var failed = false;
        var transient = false;
        foreach (var action in model.Actions.Select((item, index) => (item, index)))
        {
            var stepKey = AutomationPolicies.StepKey(definition.Id, run.Version, run.EventId, action.index);
            var step = await db.AutomationSteps.FirstOrDefaultAsync(row => row.IdempotencyKey == stepKey, ct);
            if (step?.Status == AutomationStepStatus.Succeeded)
            {
                continue;
            }

            step ??= NewStep(definition, run, action.index, action.item.Type, stepKey);
            if (!granted.Contains(action.item.Capability))
            {
                Mark(step, AutomationStepStatus.Failed, "CapabilityDenied", null);
                failed = true;
                continue;
            }

            var attempt = 0;
            string? error = null;
            Guid? effect = null;
            while (attempt < 2)
            {
                attempt++;
                step.Attempts = attempt;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(AutomationPolicies.ActionTimeoutSeconds));
                try
                {
                    effect = await EffectAsync(definition, run, signal, model, action.item, stepKey, timeout.Token);
                    error = null;
                    break;
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    error = "Timeout";
                }
                catch (UnauthorizedAccessException)
                {
                    error = "CapabilityDenied";
                    break;
                }
                catch (InvalidOperationException ex) when (ex.Message is "CapabilityDenied" or "ChannelNotInScope" or "SourceRequired" or "TaskNotFound" or "ConnectorNotAllowed" or "Timeout" or "ConnectorUnavailable")
                {
                    error = ex.Message;
                    if (!AutomationPolicies.IsTransient(error))
                    {
                        break;
                    }
                }
            }

            if (error is null)
            {
                Mark(step, AutomationStepStatus.Succeeded, null, effect);
            }
            else
            {
                Mark(step, AutomationStepStatus.Failed, error, effect);
                failed = true;
                transient = AutomationPolicies.IsTransient(error);
            }
        }

        if (!failed)
        {
            return Finish(definition, run, AutomationRunStatus.Succeeded, null, true);
        }

        return Finish(definition, run, AutomationRunStatus.Failed, transient ? "Timeout" : "ActionFailed", false);
    }

    private async Task<Guid?> EffectAsync(
        AutomationDefinition definition,
        AutomationRun run,
        AutomationSignal signal,
        AutomationDefinitionModel model,
        AutomationActionModel action,
        string stepKey,
        CancellationToken ct)
    {
        var channelId = action.ChannelId ?? signal.ChannelId;
        if (action.Type is AutomationPolicies.ActionPostMessage or AutomationPolicies.ActionCreateTask)
        {
            if (channelId is null || !model.ChannelIds.Contains(channelId.Value))
            {
                throw new InvalidOperationException("ChannelNotInScope");
            }
        }

        switch (action.Type)
        {
            case AutomationPolicies.ActionPostMessage:
                return await PostAsync(definition, channelId!.Value, action.Body, stepKey, ct);
            case AutomationPolicies.ActionCreateTask:
                return await CreateTaskAsync(definition, run, signal, action, channelId!.Value, stepKey, ct);
            case AutomationPolicies.ActionUpdateTask:
                return await UpdateTaskAsync(definition, model, action, ct);
            case AutomationPolicies.ActionNotify:
                return await NotifyAsync(definition, run, action, ct);
            case AutomationPolicies.ActionConnector:
                await CallConnectorAsync(definition, run, action, ct);
                return action.ConnectorId;
            default:
                throw new InvalidOperationException("CapabilityDenied");
        }
    }

    private async Task<Guid> PostAsync(
        AutomationDefinition definition,
        Guid channelId,
        string body,
        string stepKey,
        CancellationToken ct)
    {
        if (definition.IdentityUserId is null)
        {
            throw new InvalidOperationException("CapabilityDenied");
        }

        var previous = tenant.HasUser ? tenant.UserId : UserId.Empty;
        try
        {
            var result = await messages.SendAsync(new SendMessageCommand(
                definition.TenantId,
                definition.IdentityUserId.Value,
                new ChannelId(channelId),
                MessageId.New(),
                stepKey,
                body,
                null,
                null,
                null,
                AuthorIsBot: true), ct);
            return result.MessageId.Value;
        }
        finally
        {
            if (previous.Value != Guid.Empty)
            {
                tenant.SetUser(previous);
            }
        }
    }

    private async Task<Guid> CreateTaskAsync(
        AutomationDefinition definition,
        AutomationRun run,
        AutomationSignal signal,
        AutomationActionModel action,
        Guid channelId,
        string stepKey,
        CancellationToken ct)
    {
        if (signal.SourceId is null || definition.IdentityUserId is null)
        {
            throw new InvalidOperationException("SourceRequired");
        }

        var sourceId = new MessageId(signal.SourceId.Value);
        var exists = await db.Messages.AsNoTracking().AnyAsync(
            row => row.Id == sourceId && row.ConversationId == new ChannelId(channelId), ct);
        if (!exists)
        {
            throw new InvalidOperationException("SourceRequired");
        }

        var existing = await db.MessageTasks.FirstOrDefaultAsync(row => row.IdempotencyKey == stepKey, ct);
        if (existing is not null)
        {
            return existing.Id;
        }

        var now = clock.UtcNow;
        var task = new MessageTask
        {
            Id = Guid.NewGuid(),
            TenantId = definition.TenantId,
            WorkspaceId = definition.WorkspaceId,
            ChannelId = new ChannelId(channelId),
            Title = action.Title,
            Priority = TaskPriority.Normal,
            State = TaskState.Open,
            Version = 1,
            SourceMessageId = sourceId,
            SourceVersion = 1,
            ExcerptHash = TaskPolicies.ExcerptHash(null),
            Method = AutomationPolicies.TaskMethod,
            CreatedBy = definition.IdentityUserId.Value,
            AuthzSnapshot = "{\"method\":\"automation\"}",
            Visibility = CommitmentVisibility.Channel,
            IdempotencyKey = stepKey,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.MessageTasks.Add(task);
        outbox.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TenantId = definition.TenantId,
            Type = TaskPolicies.Created,
            OccurredAt = now,
            Payload = JsonSerializer.Serialize(new
            {
                tenantId = definition.TenantId.Value,
                taskId = task.Id,
                channelId,
                state = "open",
                automationDepth = run.Depth + 1,
                automationRunId = run.Id,
            }),
        });
        return task.Id;
    }

    private async Task<Guid> UpdateTaskAsync(
        AutomationDefinition definition,
        AutomationDefinitionModel model,
        AutomationActionModel action,
        CancellationToken ct)
    {
        var task = await db.MessageTasks.FirstOrDefaultAsync(row => row.Id == action.TaskId, ct);
        if (task is null || task.WorkspaceId != definition.WorkspaceId)
        {
            throw new InvalidOperationException("TaskNotFound");
        }

        if (!model.ChannelIds.Contains(task.ChannelId.Value))
        {
            throw new InvalidOperationException("ChannelNotInScope");
        }

        if (!TaskPolicies.TryParseState(action.State, out var state))
        {
            throw new InvalidOperationException("CapabilityDenied");
        }

        if (task.State != state)
        {
            task.State = state;
            task.Version += 1;
            task.UpdatedAt = clock.UtcNow;
            if (state == TaskState.Completed)
            {
                task.CompletedAt = task.UpdatedAt;
                task.CompletedBy = definition.IdentityUserId;
            }

            outbox.Add(new OutboxMessage
            {
                Id = Guid.NewGuid(),
                TenantId = definition.TenantId,
                Type = state == TaskState.Completed ? TaskPolicies.Completed : TaskPolicies.Updated,
                OccurredAt = task.UpdatedAt,
                Payload = JsonSerializer.Serialize(new
                {
                    tenantId = definition.TenantId.Value,
                    taskId = task.Id,
                    channelId = task.ChannelId.Value,
                    state = TaskPolicies.StateToken(state),
                }),
            });
        }

        return task.Id;
    }

    private async Task<Guid> NotifyAsync(
        AutomationDefinition definition,
        AutomationRun run,
        AutomationActionModel action,
        CancellationToken ct)
    {
        var members = await db.WorkspaceMembers.AsNoTracking()
            .Where(row => row.WorkspaceId == definition.WorkspaceId)
            .Select(row => row.UserId)
            .ToListAsync(ct);
        var memberIds = members.Select(row => row.Value).ToHashSet();
        if (action.UserIds.Any(id => !memberIds.Contains(id)))
        {
            throw new InvalidOperationException("CapabilityDenied");
        }

        var id = Guid.NewGuid();
        outbox.Add(new OutboxMessage
        {
            Id = id,
            TenantId = definition.TenantId,
            Type = AutomationPolicies.Notified,
            OccurredAt = clock.UtcNow,
            Payload = JsonSerializer.Serialize(new
            {
                tenantId = definition.TenantId.Value,
                automationId = definition.Id,
                runId = run.Id,
                userIds = action.UserIds,
            }),
        });
        return id;
    }

    private async Task CallConnectorAsync(
        AutomationDefinition definition,
        AutomationRun run,
        AutomationActionModel action,
        CancellationToken ct)
    {
        if (action.ConnectorId is null)
        {
            throw new InvalidOperationException("ConnectorNotAllowed");
        }

        var payload = AutomationPolicies.Redact(JsonSerializer.Serialize(new
        {
            type = AutomationPolicies.ConnectorEvent,
            tenantId = definition.TenantId.Value,
            automationId = definition.Id,
            runId = run.Id,
        }));
        var result = await webhooks.DeliverAllowlistedAsync(
            definition.TenantId,
            action.ConnectorId.Value,
            AutomationPolicies.ConnectorEvent,
            payload,
            ct);
        if (!result.Ok)
        {
            throw new InvalidOperationException(result.Error ?? "ConnectorNotAllowed");
        }
    }

    private async Task<AutomationSignal> WithDepthAsync(AutomationSignal signal, string payload, CancellationToken ct)
    {
        if (signal.Type != AutomationPolicies.TriggerMessage || signal.SourceId is null)
        {
            return signal;
        }

        Guid authorId;
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (!document.RootElement.TryGetProperty("authorId", out var author) || author.ValueKind != JsonValueKind.String)
            {
                return signal;
            }

            if (!Guid.TryParse(author.GetString(), out authorId))
            {
                return signal;
            }
        }
        catch (JsonException)
        {
            return signal;
        }

        var profile = await db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(row => row.Id == new UserId(authorId), ct);
        if (profile is null || !profile.Subject.StartsWith(AutomationPolicies.IdentitySubjectPrefix, StringComparison.Ordinal))
        {
            return signal;
        }

        var step = await db.AutomationSteps.AsNoTracking()
            .FirstOrDefaultAsync(row => row.EffectId == signal.SourceId, ct);
        if (step is null)
        {
            return signal with { Depth = signal.Depth + 1 };
        }

        var cause = await db.AutomationRuns.AsNoTracking().FirstOrDefaultAsync(row => row.Id == step.RunId, ct);
        return cause is null
            ? signal with { Depth = signal.Depth + 1 }
            : signal with { Depth = cause.Depth + 1, CausationRunId = cause.Id };
    }

    private async Task<HashSet<string>> CurrentCapabilitiesAsync(
        AutomationDefinition definition,
        AutomationVersion version,
        CancellationToken ct)
    {
        var owner = await db.WorkspaceMembers.AsNoTracking().FirstOrDefaultAsync(
            row => row.WorkspaceId == definition.WorkspaceId && row.UserId == version.PublishedBy, ct);
        var requested = JsonSerializer.Deserialize<string[]>(version.CapabilitiesJson) ?? [];
        if (owner is null)
        {
            return [];
        }

        return AutomationPolicies.Intersect(RolePermissionCatalog.For(owner.Role), requested).ToHashSet(StringComparer.Ordinal);
    }

    private async Task PlanStepsAsync(
        AutomationDefinition definition,
        AutomationRun run,
        AutomationDefinitionModel model,
        AutomationStepStatus status,
        string? error,
        CancellationToken ct)
    {
        foreach (var action in model.Actions.Select((item, index) => (item, index)))
        {
            var key = AutomationPolicies.StepKey(definition.Id, run.Version, run.EventId, action.index);
            if (await db.AutomationSteps.AnyAsync(row => row.IdempotencyKey == key, ct))
            {
                continue;
            }

            var step = NewStep(definition, run, action.index, action.item.Type, key);
            Mark(step, status, error, null);
        }
    }

    private AutomationStep NewStep(AutomationDefinition definition, AutomationRun run, int index, string type, string key)
    {
        var step = new AutomationStep
        {
            Id = Guid.NewGuid(),
            TenantId = definition.TenantId,
            RunId = run.Id,
            ActionIndex = index,
            ActionType = type,
            Status = AutomationStepStatus.Planned,
            IdempotencyKey = key,
            LogJson = "{}",
        };
        db.AutomationSteps.Add(step);
        return step;
    }

    private static void Mark(AutomationStep step, AutomationStepStatus status, string? error, Guid? effectId)
    {
        step.Status = status;
        step.ErrorCode = error;
        step.EffectId = effectId;
        step.LogJson = AutomationPolicies.StepLog(step.ActionType, AutomationPolicies.StepToken(status), error, effectId);
    }

    private AutomationRun Finish(
        AutomationDefinition definition,
        AutomationRun run,
        AutomationRunStatus status,
        string? error,
        bool success)
    {
        run.Status = status;
        run.ErrorCode = error;
        run.FinishedAt = clock.UtcNow;
        if (success)
        {
            definition.ConsecutiveFailures = 0;
            definition.CircuitOpenUntil = null;
        }
        else if (status == AutomationRunStatus.Failed)
        {
            definition.ConsecutiveFailures += 1;
            if (definition.ConsecutiveFailures >= AutomationPolicies.MaxConsecutiveFailures)
            {
                definition.CircuitOpenUntil = clock.UtcNow.AddMinutes(AutomationPolicies.CircuitCooldownMinutes);
                Audit(ReadStoredSignal(run, null), AutomationPolicies.CircuitOpened, definition);
                VibeChatMetrics.AutomationStopped.Add(1);
            }
        }
        else if (status == AutomationRunStatus.Stopped)
        {
            VibeChatMetrics.AutomationStopped.Add(1);
        }

        return run;
    }

    private static AutomationSignal ReadStoredSignal(AutomationRun run, AutomationDefinitionModel? model)
    {
        try
        {
            using var document = JsonDocument.Parse(run.EventJson);
            var root = document.RootElement;
            return new AutomationSignal(
                ReadString(root, "type") ?? model?.Trigger.Type ?? string.Empty,
                ReadString(root, "eventName") ?? string.Empty,
                run.EventId,
                ReadGuid(root, "channelId"),
                ReadGuid(root, "sourceId"),
                ReadGuid(root, "formId"),
                ReadGuid(root, "taskId"),
                ReadString(root, "state"),
                run.Depth,
                run.CausationRunId);
        }
        catch (JsonException)
        {
            return new AutomationSignal(model?.Trigger.Type ?? string.Empty, string.Empty, run.EventId, null, null, null, null, null, run.Depth, run.CausationRunId);
        }
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static Guid? ReadGuid(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return Guid.TryParse(value.GetString(), out var id) ? id : null;
    }

    public async Task EnsureIdentityAsync(AutomationDefinition definition, AutomationDefinitionModel model, CancellationToken ct)
    {
        var now = clock.UtcNow;
        if (definition.IdentityUserId is null)
        {
            var userId = UserId.New();
            definition.IdentityUserId = userId;
            db.UserProfiles.Add(new UserProfile
            {
                Id = userId,
                Subject = $"{AutomationPolicies.IdentitySubjectPrefix}{definition.Id:N}",
                Email = $"automation+{definition.Id:N}@automations.vibechat.local",
                DisplayName = definition.Name,
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.WorkspaceMembers.Add(new WorkspaceMember
            {
                Id = Guid.NewGuid(),
                TenantId = definition.TenantId,
                WorkspaceId = definition.WorkspaceId,
                UserId = userId,
                Role = Role.Bot,
                JoinedAt = now,
            });
        }
        else
        {
            var profile = await db.UserProfiles.FirstOrDefaultAsync(row => row.Id == definition.IdentityUserId, ct);
            if (profile is not null)
            {
                profile.DisplayName = definition.Name;
                profile.UpdatedAt = now;
            }
        }

        var identityId = definition.IdentityUserId.Value;
        foreach (var channelId in model.ChannelIds)
        {
            var channel = await db.Channels.FirstOrDefaultAsync(
                row => row.Id == new ChannelId(channelId) && row.WorkspaceId == definition.WorkspaceId, ct);
            if (channel is null || !channel.Type.RequiresChannelMembership())
            {
                continue;
            }

            var member = await db.ChannelMembers.FirstOrDefaultAsync(
                row => row.ChannelId == channel.Id && row.UserId == identityId, ct);
            if (member is null)
            {
                db.ChannelMembers.Add(new ChannelMember
                {
                    Id = Guid.NewGuid(),
                    TenantId = definition.TenantId,
                    ChannelId = channel.Id,
                    UserId = identityId,
                    JoinedAt = now,
                });
            }
            else
            {
                member.LeftAt = null;
            }
        }
    }

    private async Task<AutomationVersion?> LatestVersionAsync(AutomationDefinition definition, CancellationToken ct) =>
        definition.PublishedVersion == 0
            ? null
            : await db.AutomationVersions.FirstOrDefaultAsync(
                row => row.AutomationId == definition.Id && row.Version == definition.PublishedVersion, ct);

    private void Audit(AutomationSignal signal, string action, AutomationDefinition? definition)
    {
        audit.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            TenantId = definition?.TenantId ?? (tenant.HasTenant ? tenant.TenantId : TenantId.Empty),
            ActorUserId = definition?.CreatedBy ?? UserId.Empty,
            Action = action,
            EntityType = "Automation",
            EntityId = definition?.Id.ToString() ?? signal.EventId,
            MetadataJson = AutomationPolicies.Redact(JsonSerializer.Serialize(new
            {
                depth = signal.Depth,
                eventId = signal.EventId,
                type = signal.Type,
            })),
            OccurredAt = clock.UtcNow,
        });
    }
}

public sealed class AutomationDispatchDispatcher(
    IServiceScopeFactory scopes,
    HaJobGate jobs,
    ILogger<AutomationDispatchDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!await jobs.WaitTurnAsync(stoppingToken))
            {
                continue;
            }

            try
            {
                using var scope = scopes.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<AutomationService>();
                await service.ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Automation batch failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
