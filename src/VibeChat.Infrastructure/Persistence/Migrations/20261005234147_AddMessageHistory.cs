using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MovedFromChannelId",
                schema: "messaging",
                table: "messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MovedFromMessageId",
                schema: "messaging",
                table: "messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MovedToChannelId",
                schema: "messaging",
                table: "messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MovedToMessageId",
                schema: "messaging",
                table: "messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HistoryEnabled",
                schema: "messaging",
                table: "message_lifecycle_policies",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "LeaveTombstone",
                schema: "messaging",
                table: "message_lifecycle_policies",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "message_moves",
                schema: "messaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SourceMessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationMessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationSequence = table.Column<long>(type: "bigint", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Scope = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    LeaveTombstone = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_message_moves", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "message_versions",
                schema: "messaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    Body = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_message_versions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_message_moves_TenantId_IdempotencyKey",
                schema: "messaging",
                table: "message_moves",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_message_moves_TenantId_SourceMessageId",
                schema: "messaging",
                table: "message_moves",
                columns: new[] { "TenantId", "SourceMessageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_message_versions_TenantId_MessageId_VersionNumber",
                schema: "messaging",
                table: "message_versions",
                columns: new[] { "TenantId", "MessageId", "VersionNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "message_moves",
                schema: "messaging");

            migrationBuilder.DropTable(
                name: "message_versions",
                schema: "messaging");

            migrationBuilder.DropColumn(
                name: "MovedFromChannelId",
                schema: "messaging",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "MovedFromMessageId",
                schema: "messaging",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "MovedToChannelId",
                schema: "messaging",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "MovedToMessageId",
                schema: "messaging",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "HistoryEnabled",
                schema: "messaging",
                table: "message_lifecycle_policies");

            migrationBuilder.DropColumn(
                name: "LeaveTombstone",
                schema: "messaging",
                table: "message_lifecycle_policies");
        }
    }
}
