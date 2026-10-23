using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPluginRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "plugin_registry_installs",
                schema: "integrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    PluginId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PreviousVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Payload = table.Column<string>(type: "character varying(65536)", maxLength: 65536, nullable: false),
                    Signature = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    PreviousPayload = table.Column<string>(type: "character varying(65536)", maxLength: 65536, nullable: true),
                    PreviousSignature = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Publisher = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CatalogId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    KeyId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Checksum = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ArtifactRef = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Provenance = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Risk = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Advisory = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Capabilities = table.Column<string[]>(type: "text[]", nullable: false),
                    GrantedCapabilities = table.Column<string[]>(type: "text[]", nullable: false),
                    DependsOn = table.Column<string[]>(type: "text[]", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    InstalledPluginId = table.Column<Guid>(type: "uuid", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plugin_registry_installs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "plugin_registry_quarantine",
                schema: "integrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    PluginId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plugin_registry_quarantine", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_plugin_registry_installs_TenantId_WorkspaceId_PluginId",
                schema: "integrations",
                table: "plugin_registry_installs",
                columns: new[] { "TenantId", "WorkspaceId", "PluginId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_plugin_registry_quarantine_TenantId_WorkspaceId_PluginId_Ve~",
                schema: "integrations",
                table: "plugin_registry_quarantine",
                columns: new[] { "TenantId", "WorkspaceId", "PluginId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "plugin_registry_installs",
                schema: "integrations");

            migrationBuilder.DropTable(
                name: "plugin_registry_quarantine",
                schema: "integrations");
        }
    }
}
