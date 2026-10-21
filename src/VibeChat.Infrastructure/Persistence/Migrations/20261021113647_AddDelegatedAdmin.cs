using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDelegatedAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "quota_alerts",
                schema: "compliance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Resource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AssignmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quota_alerts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "quota_counters",
                schema: "compliance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Resource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Used = table.Column<long>(type: "bigint", nullable: false),
                    PreviousUsed = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quota_counters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "quotas",
                schema: "compliance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Resource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SoftLimit = table.Column<long>(type: "bigint", nullable: false),
                    HardLimit = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quotas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "scoped_role_assignments",
                schema: "compliance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    GranteeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Scope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SpaceKey = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    GrantedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    GrantedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    BreakGlass = table.Column<bool>(type: "boolean", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResultJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scoped_role_assignments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_quota_alerts_TenantId_WorkspaceId_CreatedAt",
                schema: "compliance",
                table: "quota_alerts",
                columns: new[] { "TenantId", "WorkspaceId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_quota_counters_TenantId_WorkspaceId_Resource",
                schema: "compliance",
                table: "quota_counters",
                columns: new[] { "TenantId", "WorkspaceId", "Resource" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_quotas_TenantId_WorkspaceId_Resource",
                schema: "compliance",
                table: "quotas",
                columns: new[] { "TenantId", "WorkspaceId", "Resource" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_scoped_role_assignments_TenantId_GrantedBy_IdempotencyKey",
                schema: "compliance",
                table: "scoped_role_assignments",
                columns: new[] { "TenantId", "GrantedBy", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_scoped_role_assignments_TenantId_WorkspaceId_GranteeId_Scop~",
                schema: "compliance",
                table: "scoped_role_assignments",
                columns: new[] { "TenantId", "WorkspaceId", "GranteeId", "Scope", "SpaceKey" },
                unique: true,
                filter: "\"RevokedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "quota_alerts",
                schema: "compliance");

            migrationBuilder.DropTable(
                name: "quota_counters",
                schema: "compliance");

            migrationBuilder.DropTable(
                name: "quotas",
                schema: "compliance");

            migrationBuilder.DropTable(
                name: "scoped_role_assignments",
                schema: "compliance");
        }
    }
}
