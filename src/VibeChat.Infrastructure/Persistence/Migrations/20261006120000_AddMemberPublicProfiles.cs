using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VibeChat.Infrastructure;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// B-167: one public member card per user inside a tenant. Down drops only this table.
    /// </summary>
    [DbContext(typeof(VibeChatDbContext))]
    [Migration("20261006120000_AddMemberPublicProfiles")]
    public partial class AddMemberPublicProfiles : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TABLE identity.member_profiles (
                    "Id" uuid NOT NULL,
                    "TenantId" uuid NOT NULL,
                    "UserId" uuid NOT NULL,
                    "JobTitle" character varying(80) NULL,
                    "About" character varying(500) NULL,
                    "HighlightMessage" character varying(160) NULL,
                    "AvatarObjectKey" character varying(512) NULL,
                    "AvatarContentType" character varying(64) NULL,
                    "UpdatedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_member_profiles" PRIMARY KEY ("Id")
                );
                CREATE UNIQUE INDEX "IX_member_profiles_TenantId_UserId"
                    ON identity.member_profiles ("TenantId", "UserId");
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS identity.member_profiles;");
        }
    }
}
