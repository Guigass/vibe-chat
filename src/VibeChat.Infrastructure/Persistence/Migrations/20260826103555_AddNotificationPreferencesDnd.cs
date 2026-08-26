using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationPreferencesDnd : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DigestEnabled",
                schema: "notifications",
                table: "preferences",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<short>(
                name: "DndDays",
                schema: "notifications",
                table: "preferences",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<bool>(
                name: "DndEnabled",
                schema: "notifications",
                table: "preferences",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "DndEnd",
                schema: "notifications",
                table: "preferences",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "DndStart",
                schema: "notifications",
                table: "preferences",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HidePreview",
                schema: "notifications",
                table: "preferences",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Level",
                schema: "notifications",
                table: "preferences",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "MentionsAndDms");

            migrationBuilder.AddColumn<Guid[]>(
                name: "PriorityContactUserIds",
                schema: "notifications",
                table: "preferences",
                type: "uuid[]",
                nullable: false,
                defaultValue: new Guid[0]);

            migrationBuilder.AddColumn<string>(
                name: "TimeZone",
                schema: "notifications",
                table: "preferences",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "channel_preferences",
                schema: "notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    Level = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MutedUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_channel_preferences", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_channel_preferences_ChannelId_UserId",
                schema: "notifications",
                table: "channel_preferences",
                columns: new[] { "ChannelId", "UserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "channel_preferences",
                schema: "notifications");

            migrationBuilder.DropColumn(
                name: "DigestEnabled",
                schema: "notifications",
                table: "preferences");

            migrationBuilder.DropColumn(
                name: "DndDays",
                schema: "notifications",
                table: "preferences");

            migrationBuilder.DropColumn(
                name: "DndEnabled",
                schema: "notifications",
                table: "preferences");

            migrationBuilder.DropColumn(
                name: "DndEnd",
                schema: "notifications",
                table: "preferences");

            migrationBuilder.DropColumn(
                name: "DndStart",
                schema: "notifications",
                table: "preferences");

            migrationBuilder.DropColumn(
                name: "HidePreview",
                schema: "notifications",
                table: "preferences");

            migrationBuilder.DropColumn(
                name: "Level",
                schema: "notifications",
                table: "preferences");

            migrationBuilder.DropColumn(
                name: "PriorityContactUserIds",
                schema: "notifications",
                table: "preferences");

            migrationBuilder.DropColumn(
                name: "TimeZone",
                schema: "notifications",
                table: "preferences");
        }
    }
}
