using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddImportPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "import");

            migrationBuilder.CreateTable(
                name: "historical_principals",
                schema: "import",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImportJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_historical_principals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "id_map",
                schema: "import",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImportJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CanonicalId = table.Column<Guid>(type: "uuid", nullable: true),
                    Disposition = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_id_map", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "jobs",
                schema: "import",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    Adapter = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PauseFrom = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DocumentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CanonicalJson = table.Column<string>(type: "text", nullable: false),
                    ReportJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_jobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_historical_principals_TenantId_ImportJobId_ExternalId",
                schema: "import",
                table: "historical_principals",
                columns: new[] { "TenantId", "ImportJobId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_id_map_TenantId_ImportJobId_ResourceType_ExternalId",
                schema: "import",
                table: "id_map",
                columns: new[] { "TenantId", "ImportJobId", "ResourceType", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_jobs_TenantId_IdempotencyKey",
                schema: "import",
                table: "jobs",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_jobs_TenantId_WorkspaceId_CreatedAt",
                schema: "import",
                table: "jobs",
                columns: new[] { "TenantId", "WorkspaceId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "historical_principals",
                schema: "import");

            migrationBuilder.DropTable(
                name: "id_map",
                schema: "import");

            migrationBuilder.DropTable(
                name: "jobs",
                schema: "import");
        }
    }
}
