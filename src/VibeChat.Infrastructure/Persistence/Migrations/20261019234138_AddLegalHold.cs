using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLegalHold : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "compliance");

            migrationBuilder.CreateTable(
                name: "custody_events",
                schema: "compliance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    HoldId = table.Column<Guid>(type: "uuid", nullable: true),
                    CaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExportId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PreviousHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custody_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "discovery_cases",
                schema: "compliance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    QueryJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_discovery_cases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "discovery_exports",
                schema: "compliance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Progress = table.Column<int>(type: "integer", nullable: false),
                    ManifestJson = table.Column<string>(type: "text", nullable: false),
                    Checksum = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ItemCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_discovery_exports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "grants",
                schema: "compliance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Capability = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    GrantedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_grants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "legal_holds",
                schema: "compliance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ScopeId = table.Column<Guid>(type: "uuid", nullable: true),
                    IntervalStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IntervalEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ReleaseRequestedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ReleaseApprovedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActivatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_legal_holds", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_custody_events_TenantId_ActorUserId_IdempotencyKey",
                schema: "compliance",
                table: "custody_events",
                columns: new[] { "TenantId", "ActorUserId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_custody_events_TenantId_WorkspaceId_OccurredAt",
                schema: "compliance",
                table: "custody_events",
                columns: new[] { "TenantId", "WorkspaceId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_discovery_cases_TenantId_CreatedBy_IdempotencyKey",
                schema: "compliance",
                table: "discovery_cases",
                columns: new[] { "TenantId", "CreatedBy", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_discovery_cases_TenantId_WorkspaceId_CreatedAt",
                schema: "compliance",
                table: "discovery_cases",
                columns: new[] { "TenantId", "WorkspaceId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_discovery_exports_TenantId_CaseId_CreatedAt",
                schema: "compliance",
                table: "discovery_exports",
                columns: new[] { "TenantId", "CaseId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_discovery_exports_TenantId_CreatedBy_IdempotencyKey",
                schema: "compliance",
                table: "discovery_exports",
                columns: new[] { "TenantId", "CreatedBy", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_grants_TenantId_GrantedBy_IdempotencyKey",
                schema: "compliance",
                table: "grants",
                columns: new[] { "TenantId", "GrantedBy", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_grants_TenantId_WorkspaceId_UserId_Capability",
                schema: "compliance",
                table: "grants",
                columns: new[] { "TenantId", "WorkspaceId", "UserId", "Capability" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_legal_holds_TenantId_RequestedBy_IdempotencyKey",
                schema: "compliance",
                table: "legal_holds",
                columns: new[] { "TenantId", "RequestedBy", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_legal_holds_TenantId_WorkspaceId_Status",
                schema: "compliance",
                table: "legal_holds",
                columns: new[] { "TenantId", "WorkspaceId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "custody_events",
                schema: "compliance");

            migrationBuilder.DropTable(
                name: "discovery_cases",
                schema: "compliance");

            migrationBuilder.DropTable(
                name: "discovery_exports",
                schema: "compliance");

            migrationBuilder.DropTable(
                name: "grants",
                schema: "compliance");

            migrationBuilder.DropTable(
                name: "legal_holds",
                schema: "compliance");
        }
    }
}
