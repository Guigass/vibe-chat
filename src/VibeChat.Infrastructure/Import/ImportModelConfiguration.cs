using Microsoft.EntityFrameworkCore;
using VibeChat.Administration;
using VibeChat.BuildingBlocks;
using VibeChat.SharedKernel;

namespace VibeChat.Infrastructure;

internal static class ImportModelConfiguration
{
    internal static void Configure(ModelBuilder modelBuilder, ITenantContext tenantContext)
    {
        modelBuilder.Entity<ImportJobRecord>(entity =>
        {
            entity.ToTable("jobs", "import");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.CreatedBy).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.Adapter).HasMaxLength(32);
            entity.Property(x => x.Status).HasMaxLength(32);
            entity.Property(x => x.PauseFrom).HasMaxLength(32);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(200);
            entity.Property(x => x.DocumentHash).HasMaxLength(64);
            entity.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.WorkspaceId, x.CreatedAt });
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<ImportIdMapRecord>(entity =>
        {
            entity.ToTable("id_map", "import");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.ResourceType).HasMaxLength(32);
            entity.Property(x => x.ExternalId).HasMaxLength(200);
            entity.Property(x => x.Disposition).HasMaxLength(32);
            entity.HasIndex(x => new { x.TenantId, x.ImportJobId, x.ResourceType, x.ExternalId }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<ImportHistoricalPrincipalRecord>(entity =>
        {
            entity.ToTable("historical_principals", "import");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.UserId).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.ExternalId).HasMaxLength(200);
            entity.HasIndex(x => new { x.TenantId, x.ImportJobId, x.ExternalId }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });
    }
}
