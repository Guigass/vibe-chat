using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIncidents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "incident");

            migrationBuilder.CreateTable(
                name: "responders",
                schema: "incident",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_responders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "rooms",
                schema: "incident",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateKey = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Title = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Severity = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CommanderUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    DeclaredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    MitigatingAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PostmortemJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rooms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "steps",
                schema: "incident",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    StepIndex = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    EvidenceKind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    EvidenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    AutomationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_steps1", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "timeline",
                schema: "incident",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_timeline", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_responders_TenantId_IncidentId_UserId",
                schema: "incident",
                table: "responders",
                columns: new[] { "TenantId", "IncidentId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rooms_TenantId_ChannelId",
                schema: "incident",
                table: "rooms",
                columns: new[] { "TenantId", "ChannelId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rooms_TenantId_CreatedBy_IdempotencyKey",
                schema: "incident",
                table: "rooms",
                columns: new[] { "TenantId", "CreatedBy", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rooms_TenantId_WorkspaceId_Status",
                schema: "incident",
                table: "rooms",
                columns: new[] { "TenantId", "WorkspaceId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_steps_TenantId_IncidentId_StepIndex",
                schema: "incident",
                table: "steps",
                columns: new[] { "TenantId", "IncidentId", "StepIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_timeline_TenantId_IncidentId_IdempotencyKey",
                schema: "incident",
                table: "timeline",
                columns: new[] { "TenantId", "IncidentId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_timeline_TenantId_IncidentId_Kind_SourceId",
                schema: "incident",
                table: "timeline",
                columns: new[] { "TenantId", "IncidentId", "Kind", "SourceId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "responders",
                schema: "incident");

            migrationBuilder.DropTable(
                name: "rooms",
                schema: "incident");

            migrationBuilder.DropTable(
                name: "steps",
                schema: "incident");

            migrationBuilder.DropTable(
                name: "timeline",
                schema: "incident");
        }
    }
}
