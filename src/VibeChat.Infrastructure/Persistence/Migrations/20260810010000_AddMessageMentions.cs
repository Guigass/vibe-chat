using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VibeChat.Infrastructure;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(VibeChatDbContext))]
    [Migration("20260810010000_AddMessageMentions")]
    public partial class AddMessageMentions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "message_mentions",
                schema: "messaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    MentionedUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_message_mentions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_message_mentions_TenantId_ChannelId_MentionedUserId",
                schema: "messaging",
                table: "message_mentions",
                columns: new[] { "TenantId", "ChannelId", "MentionedUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_message_mentions_TenantId_MessageId",
                schema: "messaging",
                table: "message_mentions",
                columns: new[] { "TenantId", "MessageId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "message_mentions",
                schema: "messaging");
        }
    }
}
