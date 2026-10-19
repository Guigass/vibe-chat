using Microsoft.EntityFrameworkCore;
using VibeChat.BuildingBlocks;
using VibeChat.Identity;
using VibeChat.SharedKernel;

namespace VibeChat.Infrastructure;

internal static class ScimModelConfiguration
{
    internal static void Configure(ModelBuilder modelBuilder, ITenantContext tenantContext)
    {
        modelBuilder.Entity<ScimToken>(entity =>
        {
            entity.ToTable("scim_tokens", "identity");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.CreatedByUserId).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.Name).HasMaxLength(ScimPolicies.MaxNameLength);
            entity.Property(x => x.TokenHash).HasMaxLength(64);
            entity.Property(x => x.Last4).HasMaxLength(8);
            entity.Property(x => x.Scopes).HasMaxLength(64);
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<ScimPrincipal>(entity =>
        {
            entity.ToTable("scim_principals", "identity");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.UserId).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.ExternalId).HasMaxLength(ScimPolicies.MaxExternalIdLength);
            entity.Property(x => x.UserName).HasMaxLength(ScimPolicies.MaxUserNameLength);
            entity.Property(x => x.DisplayName).HasMaxLength(ScimPolicies.MaxDisplayNameLength);
            entity.HasIndex(x => new { x.TenantId, x.UserName }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.ExternalId }).IsUnique().HasFilter("\"ExternalId\" IS NOT NULL");
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<ScimGroup>(entity =>
        {
            entity.ToTable("scim_groups", "identity");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.ExternalId).HasMaxLength(ScimPolicies.MaxExternalIdLength);
            entity.Property(x => x.DisplayName).HasMaxLength(ScimPolicies.MaxDisplayNameLength);
            entity.HasIndex(x => new { x.TenantId, x.ExternalId }).IsUnique().HasFilter("\"ExternalId\" IS NOT NULL");
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<ScimGroupMember>(entity =>
        {
            entity.ToTable("scim_group_members", "identity");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.HasIndex(x => new { x.GroupId, x.PrincipalId }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<ScimGroupMapping>(entity =>
        {
            entity.ToTable("scim_group_mappings", "identity");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.HasIndex(x => new { x.GroupId, x.WorkspaceId }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<ScimMembershipGrant>(entity =>
        {
            entity.ToTable("scim_membership_grants", "identity");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.WorkspaceId).HasConversion(v => v.Value, v => new WorkspaceId(v));
            entity.Property(x => x.UserId).HasConversion(v => v.Value, v => new UserId(v));
            entity.HasIndex(x => new { x.GroupId, x.WorkspaceId, x.UserId }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<AccessRevocation>(entity =>
        {
            entity.ToTable("access_revocations", "identity");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.TenantId).HasConversion(v => v.Value, v => new TenantId(v));
            entity.Property(x => x.UserId).HasConversion(v => v.Value, v => new UserId(v));
            entity.Property(x => x.Reason).HasMaxLength(32);
            entity.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique();
            entity.HasQueryFilter(x => !tenantContext.HasTenant || x.TenantId == tenantContext.TenantId);
        });
    }
}
