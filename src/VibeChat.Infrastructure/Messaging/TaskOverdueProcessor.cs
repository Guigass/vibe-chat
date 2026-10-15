using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VibeChat.BuildingBlocks;
using VibeChat.Messaging;
using VibeChat.SharedKernel;

namespace VibeChat.Infrastructure.Messaging;

public sealed class TaskOverdueProcessor(
    IServiceScopeFactory scopeFactory,
    ILogger<TaskOverdueProcessor> logger)
{
    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VibeChatDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxWriter>();

        tenant.SetJobRole("tasks");
        await RlsSession.EnsureAppliedAsync(db, tenant, cancellationToken);

        var now = clock.UtcNow;
        var due = await db.MessageTasks.IgnoreQueryFilters()
            .Where(row => row.OverdueNotifiedAt == null
                && row.DueAt != null
                && row.DueAt <= now
                && row.State != TaskState.Completed)
            .OrderBy(row => row.DueAt)
            .Take(50)
            .ToListAsync(cancellationToken);
        if (due.Count == 0)
        {
            await RlsSession.CommitAsync(db, cancellationToken);
            return 0;
        }

        var emitted = 0;
        foreach (var group in due.GroupBy(row => row.TenantId))
        {
            tenant.SetTenant(group.Key);
            await RlsSession.EnsureAppliedAsync(db, tenant, cancellationToken);
            var ids = group.Select(row => row.Id).ToArray();
            var assignees = await db.MessageTaskAssignees.IgnoreQueryFilters()
                .Where(row => row.TenantId == group.Key && ids.Contains(row.TaskId))
                .ToListAsync(cancellationToken);

            foreach (var task in group)
            {
                if (!TaskPolicies.ShouldEmitOverdue(task.DueAt, task.State, task.OverdueNotifiedAt, now))
                {
                    continue;
                }

                task.OverdueNotifiedAt = now;
                var assigneeIds = assignees
                    .Where(row => row.TaskId == task.Id)
                    .Select(row => row.UserId.Value)
                    .ToArray();
                outbox.Add(new OutboxMessage
                {
                    Id = Guid.NewGuid(),
                    TenantId = task.TenantId,
                    Type = TaskPolicies.Overdue,
                    OccurredAt = now,
                    Payload = JsonSerializer.Serialize(new
                    {
                        tenantId = task.TenantId.Value,
                        workspaceId = task.WorkspaceId.Value,
                        taskId = task.Id,
                        channelId = task.ChannelId.Value,
                        createdBy = task.CreatedBy.Value,
                        assigneeUserIds = assigneeIds,
                        visibility = CommitmentPolicies.VisibilityToken(task.Visibility),
                        state = TaskPolicies.StateToken(task.State),
                        sourceMessageId = task.SourceMessageId.Value,
                    }),
                });
                emitted++;
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        await RlsSession.CommitAsync(db, cancellationToken);
        logger.LogInformation("Emitted {Count} task overdue events", emitted);
        return emitted;
    }
}

public sealed class TaskOverdueDispatcher(
    TaskOverdueProcessor processor,
    ILogger<TaskOverdueDispatcher> logger) : BackgroundService
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
                logger.LogWarning(ex, "Task overdue batch failed");
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
