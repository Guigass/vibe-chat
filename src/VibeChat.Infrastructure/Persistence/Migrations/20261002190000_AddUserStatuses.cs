using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VibeChat.Infrastructure;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// B-116: one custom status per user inside a tenant. Down drops only this table.
    /// </summary>
    [DbContext(typeof(VibeChatDbContext))]
    [Migration("20261002190000_AddUserStatuses")]
    public partial class AddUserStatuses : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TABLE identity.user_statuses (
                    "Id" uuid NOT NULL,
                    "TenantId" uuid NOT NULL,
                    "UserId" uuid NOT NULL,
                    "State" character varying(16) NOT NULL,
                    "Emoji" character varying(16) NOT NULL,
                    "Text" character varying(80) NOT NULL,
                    "ClearAtEndOfDay" boolean NOT NULL,
                    "ExpiresAt" timestamp with time zone NULL,
                    "UpdatedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_user_statuses" PRIMARY KEY ("Id")
                );
                CREATE UNIQUE INDEX "IX_user_statuses_TenantId_UserId"
                    ON identity.user_statuses ("TenantId", "UserId");
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS identity.user_statuses;");
        }
    }
}
