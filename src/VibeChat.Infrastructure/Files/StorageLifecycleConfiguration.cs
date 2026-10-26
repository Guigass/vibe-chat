using Microsoft.EntityFrameworkCore;
using VibeChat.BuildingBlocks;
using VibeChat.Files;
using VibeChat.SharedKernel;

namespace VibeChat.Infrastructure;

internal static class StorageLifecycleConfiguration
{
    internal static void Configure(ModelBuilder modelBuilder, ITenantContext tenantContext)
    {
        modelBuilder.Entity<StorageLedger>(entity =>
        {
            entity.ToTable("storage_ledgers", "files");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.Kind).HasMaxLength(32);
            entity.HasIndex(x => new { x.TenantId, x.WorkspaceId, x.Kind }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<StorageObjectRecord>(entity =>
        {
            entity.ToTable("storage_objects", "files");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.Kind).HasMaxLength(32);
            entity.Property(x => x.State).HasMaxLength(32);
            entity.Property(x => x.StorageKey).HasMaxLength(StorageLifecycleLimits.MaxStorageKeyLength);
            entity.HasIndex(x => new { x.TenantId, x.AttachmentId });
            entity.HasIndex(x => new { x.TenantId, x.WorkspaceId, x.Kind });
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<StorageTombstone>(entity =>
        {
            entity.ToTable("storage_tombstones", "files");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.HasIndex(x => new { x.TenantId, x.ObjectId }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<StorageLifecycleJob>(entity =>
        {
            entity.ToTable("storage_jobs", "files");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.IdempotencyKey).HasMaxLength(StorageLifecycleLimits.MaxIdempotencyKeyLength);
            entity.Property(x => x.Action).HasMaxLength(32);
            entity.Property(x => x.ResultJson).HasMaxLength(4000);
            entity.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });
    }
}
