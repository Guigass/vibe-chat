using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VibeChat.Infrastructure;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(VibeChatDbContext))]
    [Migration("20260826010000_AddPolls")]
    public partial class AddPolls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "polls",
                schema: "messaging",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Question = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    AllowMultiple = table.Column<bool>(type: "boolean", nullable: false),
                    Anonymous = table.Column<bool>(type: "boolean", nullable: false),
                    ClosesAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_polls", x => x.MessageId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_polls_TenantId_ChannelId",
                schema: "messaging",
                table: "polls",
                columns: new[] { "TenantId", "ChannelId" });

            migrationBuilder.CreateIndex(
                name: "IX_polls_TenantId_ClosedAt_ClosesAt",
                schema: "messaging",
                table: "polls",
                columns: new[] { "TenantId", "ClosedAt", "ClosesAt" });

            migrationBuilder.CreateTable(
                name: "poll_options",
                schema: "messaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PollId = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_poll_options", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_poll_options_TenantId_PollId_Position",
                schema: "messaging",
                table: "poll_options",
                columns: new[] { "TenantId", "PollId", "Position" },
                unique: true);

            migrationBuilder.CreateTable(
                name: "poll_votes",
                schema: "messaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PollId = table.Column<Guid>(type: "uuid", nullable: false),
                    OptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_poll_votes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_poll_votes_TenantId_PollId_OptionId_UserId",
                schema: "messaging",
                table: "poll_votes",
                columns: new[] { "TenantId", "PollId", "OptionId", "UserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "poll_votes", schema: "messaging");
            migrationBuilder.DropTable(name: "poll_options", schema: "messaging");
            migrationBuilder.DropTable(name: "polls", schema: "messaging");
        }
    }
}
