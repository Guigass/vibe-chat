using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VibeChat.Infrastructure;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// B-110: local plugin install is a manifest plus a 1:1 bot from B-109.
    /// Down drops only this table. Bots, tokens and messages stay.
    /// </summary>
    [DbContext(typeof(VibeChatDbContext))]
    [Migration("20261001233000_AddInstalledPlugins")]
    public partial class AddInstalledPlugins : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TABLE integrations.plugins (
                    "Id" uuid NOT NULL,
                    "TenantId" uuid NOT NULL,
                    "WorkspaceId" uuid NOT NULL,
                    "PluginId" character varying(64) NOT NULL,
                    "Name" character varying(80) NOT NULL,
                    "Version" character varying(32) NOT NULL,
                    "ManifestJson" character varying(4096) NOT NULL,
                    "Capabilities" text[] NOT NULL,
                    "BotId" uuid NOT NULL,
                    "Enabled" boolean NOT NULL,
                    "InstalledAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_plugins" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_plugins_bots_BotId" FOREIGN KEY ("BotId") REFERENCES integrations.bots ("Id")
                );

                CREATE UNIQUE INDEX "IX_plugins_BotId" ON integrations.plugins ("BotId");
                CREATE UNIQUE INDEX "IX_plugins_TenantId_WorkspaceId_PluginId"
                    ON integrations.plugins ("TenantId", "WorkspaceId", "PluginId");
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TABLE IF EXISTS integrations.plugins;
                """);
        }
    }
}
