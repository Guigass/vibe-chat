using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPluginGovernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastUsedAt",
                schema: "integrations",
                table: "plugins",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "plugin_action_receipts",
                schema: "integrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstalledPluginId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Action = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plugin_action_receipts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "plugin_history_grants",
                schema: "integrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstalledPluginId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    WindowStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    WindowEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GrantedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    GrantedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plugin_history_grants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "plugin_interactive_offers",
                schema: "integrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstalledPluginId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    PluginSlug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PluginName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Actions = table.Column<string[]>(type: "text[]", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plugin_interactive_offers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "plugin_quota_counters",
                schema: "integrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstalledPluginId = table.Column<Guid>(type: "uuid", nullable: false),
                    Capability = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plugin_quota_counters", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_plugin_action_receipts_TenantId_InstalledPluginId_Idempoten~",
                schema: "integrations",
                table: "plugin_action_receipts",
                columns: new[] { "TenantId", "InstalledPluginId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_plugin_history_grants_InstalledPluginId_ChannelId",
                schema: "integrations",
                table: "plugin_history_grants",
                columns: new[] { "InstalledPluginId", "ChannelId" });

            migrationBuilder.CreateIndex(
                name: "IX_plugin_interactive_offers_MessageId",
                schema: "integrations",
                table: "plugin_interactive_offers",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_plugin_interactive_offers_TenantId_InstalledPluginId_Idempo~",
                schema: "integrations",
                table: "plugin_interactive_offers",
                columns: new[] { "TenantId", "InstalledPluginId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_plugin_quota_counters_TenantId_InstalledPluginId_Capability~",
                schema: "integrations",
                table: "plugin_quota_counters",
                columns: new[] { "TenantId", "InstalledPluginId", "Capability", "Day" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "plugin_action_receipts",
                schema: "integrations");

            migrationBuilder.DropTable(
                name: "plugin_history_grants",
                schema: "integrations");

            migrationBuilder.DropTable(
                name: "plugin_interactive_offers",
                schema: "integrations");

            migrationBuilder.DropTable(
                name: "plugin_quota_counters",
                schema: "integrations");

            migrationBuilder.DropColumn(
                name: "LastUsedAt",
                schema: "integrations",
                table: "plugins");
        }
    }
}
