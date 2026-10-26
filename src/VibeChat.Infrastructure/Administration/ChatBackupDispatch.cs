using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VibeChat.Administration;
using VibeChat.BuildingBlocks;
using VibeChat.SharedKernel;

namespace VibeChat.Infrastructure;

public sealed class ChatBackupDispatchProcessor(
    IServiceScopeFactory scopeFactory,
    ILogger<ChatBackupDispatchProcessor> logger)
{
    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        List<ClaimedBackupJob> claimed;
        try
        {
            claimed = await ClaimAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Chat backup claim failed");
            return 0;
        }

        var ran = 0;
        foreach (var row in claimed)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<VibeChatDbContext>();
                var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
                var service = scope.ServiceProvider.GetRequiredService<ChatBackupService>();
                tenantContext.SetTenant(new(row.TenantId));
                tenantContext.SetJobRole(ChatBackupLimits.JobRole);
                await RlsSession.EnsureAppliedAsync(db, tenantContext, cancellationToken);
                var key = "schedule:" + row.Id.ToString("N") + ":" + row.PreviousRunAt.UtcDateTime.ToString("yyyyMMddHHmmss");
                await service.RunScheduledAsync(row.Id, key, cancellationToken);
                await RlsSession.CommitAsync(db, cancellationToken);
                ran++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Chat backup job {JobId} failed closed", row.Id);
            }
        }

        return ran;
    }

    private async Task<List<ClaimedBackupJob>> ClaimAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VibeChatDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        if (!scope.ServiceProvider.GetRequiredService<ChatBackupService>().Enabled)
        {
            return [];
        }

        var now = clock.UtcNow;
        var lease = now.AddMinutes(ChatBackupLimits.ClaimLeaseMinutes);
        tenantContext.SetJobRole(ChatBackupLimits.JobRole);
        await RlsSession.EnsureAppliedAsync(db, tenantContext, cancellationToken);
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText =
            """
            WITH due AS (
                SELECT "Id", "NextRunAt"
                FROM administration.chat_backup_jobs
                WHERE "Paused" = false AND "NextRunAt" IS NOT NULL AND "NextRunAt" <= @now
                ORDER BY "NextRunAt"
                LIMIT 3
                FOR UPDATE SKIP LOCKED
            )
            UPDATE administration.chat_backup_jobs AS job
            SET "NextRunAt" = @lease, "UpdatedAt" = @now
            FROM due
            WHERE job."Id" = due."Id"
            RETURNING job."Id", job."TenantId", due."NextRunAt"
            """;
        var nowParameter = command.CreateParameter();
        nowParameter.ParameterName = "now";
        nowParameter.Value = now;
        command.Parameters.Add(nowParameter);
        var leaseParameter = command.CreateParameter();
        leaseParameter.ParameterName = "lease";
        leaseParameter.Value = lease;
        command.Parameters.Add(leaseParameter);

        var rows = new List<ClaimedBackupJob>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new ClaimedBackupJob(reader.GetGuid(0), reader.GetGuid(1), reader.GetFieldValue<DateTimeOffset>(2)));
            }
        }

        await RlsSession.CommitAsync(db, cancellationToken);
        return rows;
    }

    private readonly record struct ClaimedBackupJob(Guid Id, Guid TenantId, DateTimeOffset PreviousRunAt);
}

public sealed class ChatBackupDispatchDispatcher(
    ChatBackupDispatchProcessor processor,
    ILogger<ChatBackupDispatchDispatcher> logger) : BackgroundService
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
                logger.LogWarning(ex, "Chat backup dispatch batch failed");
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
