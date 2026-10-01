using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VibeChat.Infrastructure;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// B-109: bot identity, hashed API token and explicit channel grants.
    /// Down drops only these tables; messages the bot already sent stay.
    /// </summary>
    [DbContext(typeof(VibeChatDbContext))]
    [Migration("20260930220000_AddIntegrationBots")]
    public partial class AddIntegrationBots : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TABLE integrations.bots (
                    "Id" uuid NOT NULL,
                    "TenantId" uuid NOT NULL,
                    "WorkspaceId" uuid NOT NULL,
                    "UserId" uuid NOT NULL,
                    "Name" character varying(80) NOT NULL,
                    "Enabled" boolean NOT NULL,
                    "AllowDms" boolean NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_bots" PRIMARY KEY ("Id")
                );

                CREATE INDEX "IX_bots_TenantId_WorkspaceId" ON integrations.bots ("TenantId", "WorkspaceId");
                CREATE UNIQUE INDEX "IX_bots_UserId" ON integrations.bots ("UserId");

                CREATE TABLE integrations.bot_tokens (
                    "Id" uuid NOT NULL,
                    "TenantId" uuid NOT NULL,
                    "BotId" uuid NOT NULL,
                    "TokenHash" character varying(64) NOT NULL,
                    "Last4" character varying(8) NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "LastUsedAt" timestamp with time zone NULL,
                    "RevokedAt" timestamp with time zone NULL,
                    CONSTRAINT "PK_bot_tokens" PRIMARY KEY ("Id")
                );

                CREATE UNIQUE INDEX "IX_bot_tokens_TokenHash" ON integrations.bot_tokens ("TokenHash");
                CREATE INDEX "IX_bot_tokens_BotId" ON integrations.bot_tokens ("BotId");

                CREATE TABLE integrations.bot_channel_scopes (
                    "Id" uuid NOT NULL,
                    "TenantId" uuid NOT NULL,
                    "BotId" uuid NOT NULL,
                    "ChannelId" uuid NOT NULL,
                    CONSTRAINT "PK_bot_channel_scopes" PRIMARY KEY ("Id")
                );

                CREATE UNIQUE INDEX "IX_bot_channel_scopes_BotId_ChannelId"
                    ON integrations.bot_channel_scopes ("BotId", "ChannelId");
                CREATE INDEX "IX_bot_channel_scopes_TenantId" ON integrations.bot_channel_scopes ("TenantId");
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TABLE IF EXISTS integrations.bot_channel_scopes;
                DROP TABLE IF EXISTS integrations.bot_tokens;
                DROP TABLE IF EXISTS integrations.bots;
                """);
        }
    }
}
