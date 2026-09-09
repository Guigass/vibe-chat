using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddThreadSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Level",
                schema: "notifications",
                table: "channel_preferences",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

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
                name: "IX_channel_preferences_ChannelId_FollowAllThreads",
                schema: "notifications",
                table: "channel_preferences",
                columns: new[] { "ChannelId", "FollowAllThreads" });

            migrationBuilder.CreateIndex(
                name: "IX_thread_subscriptions_TenantId_UserId_CreatedAt",
                schema: "messaging",
                table: "thread_subscriptions",
                columns: new[] { "TenantId", "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_thread_subscriptions_TenantId_UserId_ThreadId",
                schema: "messaging",
                table: "thread_subscriptions",
                columns: new[] { "TenantId", "UserId", "ThreadId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "thread_subscriptions",
                schema: "messaging");

            migrationBuilder.DropIndex(
                name: "IX_channel_preferences_ChannelId_FollowAllThreads",
                schema: "notifications",
                table: "channel_preferences");

            migrationBuilder.DropColumn(
                name: "FollowAllThreads",
                schema: "notifications",
                table: "channel_preferences");

            migrationBuilder.AlterColumn<string>(
                name: "Level",
                schema: "notifications",
                table: "channel_preferences",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);
        }
    }
}
