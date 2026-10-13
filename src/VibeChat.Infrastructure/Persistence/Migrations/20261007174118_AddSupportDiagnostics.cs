using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSupportDiagnostics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "support");

            migrationBuilder.CreateTable(
                name: "bundles",
                schema: "support",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SchemaVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ManifestJson = table.Column<string>(type: "text", nullable: false),
                    Checksum = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    RedactedFields = table.Column<int>(type: "integer", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DownloadCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bundles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "repair_jobs",
                schema: "support",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    ActionCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DryRun = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CheckpointJson = table.Column<string>(type: "text", nullable: false),
                    Estimated = table.Column<int>(type: "integer", nullable: false),
                    Written = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_repair_jobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_bundles_TenantId_IdempotencyKey",
                schema: "support",
                table: "bundles",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bundles_TenantId_WorkspaceId_CreatedAt",
                schema: "support",
                table: "bundles",
                columns: new[] { "TenantId", "WorkspaceId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_repair_jobs_TenantId_IdempotencyKey",
                schema: "support",
                table: "repair_jobs",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_repair_jobs_TenantId_WorkspaceId_CreatedAt",
                schema: "support",
                table: "repair_jobs",
                columns: new[] { "TenantId", "WorkspaceId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bundles",
                schema: "support");

            migrationBuilder.DropTable(
                name: "repair_jobs",
                schema: "support");
        }
    }
}
