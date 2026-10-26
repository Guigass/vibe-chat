using Microsoft.EntityFrameworkCore;
using VibeChat.Administration;
using VibeChat.BuildingBlocks;
using VibeChat.SharedKernel;

namespace VibeChat.Infrastructure;

internal static class ChatBackupConfiguration
{
    internal static void Configure(ModelBuilder modelBuilder, ITenantContext tenantContext)
    {
        modelBuilder.Entity<ChatBackupDestination>(entity =>
        {
            entity.ToTable("chat_backup_destinations", "administration");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.Name).HasMaxLength(ChatBackupLimits.MaxNameLength);
            entity.Property(x => x.Kind).HasMaxLength(16);
            entity.Property(x => x.Host).HasMaxLength(ChatBackupLimits.MaxHostLength);
            entity.Property(x => x.RemotePath).HasMaxLength(ChatBackupLimits.MaxPathLength);
            entity.Property(x => x.Username).HasMaxLength(128);
            entity.Property(x => x.Bucket).HasMaxLength(128);
            entity.Property(x => x.SecretMask).HasMaxLength(16);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(ChatBackupLimits.MaxIdempotencyKeyLength);
            entity.Property(x => x.RequestHash).HasMaxLength(64);
            entity.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<ChatBackupJob>(entity =>
        {
            entity.ToTable("chat_backup_jobs", "administration");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.Name).HasMaxLength(ChatBackupLimits.MaxNameLength);
            entity.Property(x => x.Cron).HasMaxLength(80);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(ChatBackupLimits.MaxIdempotencyKeyLength);
            entity.Property(x => x.RequestHash).HasMaxLength(64);
            entity.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
            entity.HasIndex(x => new { x.Paused, x.NextRunAt });
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<ChatBackupRun>(entity =>
        {
            entity.ToTable("chat_backup_runs", "administration");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.Status).HasMaxLength(16);
            entity.Property(x => x.ChecksumSha256).HasMaxLength(64);
            entity.Property(x => x.RemoteKey).HasMaxLength(300);
            entity.Property(x => x.ErrorCode).HasMaxLength(80);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(ChatBackupLimits.MaxIdempotencyKeyLength);
            entity.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });
    }
}
