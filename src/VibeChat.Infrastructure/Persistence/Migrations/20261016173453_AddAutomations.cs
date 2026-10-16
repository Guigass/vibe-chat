using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "automation");

            migrationBuilder.CreateTable(
                name: "definitions",
                schema: "automation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    PublishedVersion = table.Column<int>(type: "integer", nullable: false),
                    DraftJson = table.Column<string>(type: "jsonb", nullable: false),
                    TriggerType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    IdentityUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    WebhookTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "integer", nullable: false),
                    CircuitOpenUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NextRunAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_definitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "runs",
                schema: "automation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AutomationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    EventId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    EventJson = table.Column<string>(type: "jsonb", nullable: false),
                    Depth = table.Column<int>(type: "integer", nullable: false),
                    Mode = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    CausationRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_runs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "steps",
                schema: "automation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActionIndex = table.Column<int>(type: "integer", nullable: false),
                    ActionType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LogJson = table.Column<string>(type: "jsonb", nullable: false),
                    EffectId = table.Column<Guid>(type: "uuid", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_steps", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "versions",
                schema: "automation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AutomationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    DefinitionJson = table.Column<string>(type: "jsonb", nullable: false),
                    CapabilitiesJson = table.Column<string>(type: "jsonb", nullable: false),
                    PublishedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_versions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_definitions_TenantId_CreatedBy_IdempotencyKey",
                schema: "automation",
                table: "definitions",
                columns: new[] { "TenantId", "CreatedBy", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_definitions_TenantId_WorkspaceId_Enabled_UpdatedAt",
                schema: "automation",
                table: "definitions",
                columns: new[] { "TenantId", "WorkspaceId", "Enabled", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_definitions_TriggerType_NextRunAt",
                schema: "automation",
                table: "definitions",
                columns: new[] { "TriggerType", "NextRunAt" });

            migrationBuilder.CreateIndex(
                name: "IX_runs_Status_CreatedAt",
                schema: "automation",
                table: "runs",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_runs_TenantId_AutomationId_CreatedAt",
                schema: "automation",
                table: "runs",
                columns: new[] { "TenantId", "AutomationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_runs_TenantId_IdempotencyKey",
                schema: "automation",
                table: "runs",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_steps_TenantId_IdempotencyKey",
                schema: "automation",
                table: "steps",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_steps_TenantId_RunId_ActionIndex",
                schema: "automation",
                table: "steps",
                columns: new[] { "TenantId", "RunId", "ActionIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_versions_TenantId_AutomationId_Version",
                schema: "automation",
                table: "versions",
                columns: new[] { "TenantId", "AutomationId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "definitions",
                schema: "automation");

            migrationBuilder.DropTable(
                name: "runs",
                schema: "automation");

            migrationBuilder.DropTable(
                name: "steps",
                schema: "automation");

            migrationBuilder.DropTable(
                name: "versions",
                schema: "automation");
        }
    }
}
