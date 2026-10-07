using Microsoft.EntityFrameworkCore;
using VibeChat.Administration;
using VibeChat.BuildingBlocks;
using VibeChat.SharedKernel;

namespace VibeChat.Infrastructure;

internal static class SupportDiagnosticsConfiguration
{
    internal static void Configure(ModelBuilder modelBuilder, ITenantContext tenantContext)
    {
        modelBuilder.Entity<SupportBundleRecord>(entity =>
        {
            entity.ToTable("bundles", "support");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.RequestedBy).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.IdempotencyKey).HasMaxLength(200);
            entity.Property(x => x.RequestHash).HasMaxLength(80);
            entity.Property(x => x.Status).HasMaxLength(32);
            entity.Property(x => x.SchemaVersion).HasMaxLength(64);
            entity.Property(x => x.Checksum).HasMaxLength(80);
            entity.Property(x => x.CorrelationId).HasMaxLength(64);
            entity.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.WorkspaceId, x.CreatedAt });
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<SupportRepairRecord>(entity =>
        {
            entity.ToTable("repair_jobs", "support");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.RequestedBy).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.ActionCode).HasMaxLength(64);
            entity.Property(x => x.Status).HasMaxLength(32);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(200);
            entity.Property(x => x.RequestHash).HasMaxLength(80);
            entity.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.WorkspaceId, x.CreatedAt });
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });
    }
}
