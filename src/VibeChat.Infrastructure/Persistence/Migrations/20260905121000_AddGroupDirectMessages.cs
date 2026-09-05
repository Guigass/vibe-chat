using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VibeChat.Infrastructure;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(VibeChatDbContext))]
    [Migration("20260905121000_AddGroupDirectMessages")]
    public partial class AddGroupDirectMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Title",
                schema: "conversations",
                table: "channels",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ParticipantSetKey",
                schema: "conversations",
                table: "channels",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "JoinedSeq",
                schema: "conversations",
                table: "channel_members",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LeftAt",
                schema: "conversations",
                table: "channel_members",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LeftSeq",
                schema: "conversations",
                table: "channel_members",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_channels_TenantId_WorkspaceId_ParticipantSetKey",
                schema: "conversations",
                table: "channels",
                columns: new[] { "TenantId", "WorkspaceId", "ParticipantSetKey" },
                unique: true,
                filter: "\"ParticipantSetKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_channels_TenantId_WorkspaceId_ParticipantSetKey",
                schema: "conversations",
                table: "channels");

            migrationBuilder.DropColumn(
                name: "Title",
                schema: "conversations",
                table: "channels");

            migrationBuilder.DropColumn(
                name: "ParticipantSetKey",
                schema: "conversations",
                table: "channels");

            migrationBuilder.DropColumn(
                name: "JoinedSeq",
                schema: "conversations",
                table: "channel_members");

            migrationBuilder.DropColumn(
                name: "LeftAt",
                schema: "conversations",
                table: "channel_members");

            migrationBuilder.DropColumn(
                name: "LeftSeq",
                schema: "conversations",
                table: "channel_members");
        }
    }
}
