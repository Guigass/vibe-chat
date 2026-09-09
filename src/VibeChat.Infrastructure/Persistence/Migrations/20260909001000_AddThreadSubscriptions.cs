using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VibeChat.Infrastructure;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(VibeChatDbContext))]
    [Migration("20260909001000_AddThreadSubscriptions")]
    public partial class AddThreadSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "FollowAllThreads",
                schema: "notifications",
                table: "channel_preferences",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "thread_subscriptions",
                schema: "messaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ThreadId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    LastReadSeq = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_thread_subscriptions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_thread_subscriptions_TenantId_UserId_ThreadId",
                schema: "messaging",
                table: "thread_subscriptions",
                columns: new[] { "TenantId", "UserId", "ThreadId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_thread_subscriptions_TenantId_UserId_ChannelId",
                schema: "messaging",
                table: "thread_subscriptions",
                columns: new[] { "TenantId", "UserId", "ChannelId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "thread_subscriptions",
                schema: "messaging");

            migrationBuilder.DropColumn(
                name: "FollowAllThreads",
                schema: "notifications",
                table: "channel_preferences");
        }
    }
}
