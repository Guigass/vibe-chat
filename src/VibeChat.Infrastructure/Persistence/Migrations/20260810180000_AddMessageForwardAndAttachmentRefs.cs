using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VibeChat.Infrastructure;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(VibeChatDbContext))]
    [Migration("20260810180000_AddMessageForwardAndAttachmentRefs")]
    public partial class AddMessageForwardAndAttachmentRefs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ForwardedFromMessageId",
                schema: "messaging",
                table: "messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ForwardedFromChannelId",
                schema: "messaging",
                table: "messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReferenceCount",
                schema: "files",
                table: "attachments",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.DropIndex(
                name: "IX_attachments_StorageKey",
                schema: "files",
                table: "attachments");

            migrationBuilder.CreateIndex(
                name: "IX_attachments_StorageKey",
                schema: "files",
                table: "attachments",
                column: "StorageKey");

            migrationBuilder.CreateIndex(
                name: "IX_messages_ForwardedFromMessageId",
                schema: "messaging",
                table: "messages",
                column: "ForwardedFromMessageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_messages_ForwardedFromMessageId",
                schema: "messaging",
                table: "messages");

            migrationBuilder.DropIndex(
                name: "IX_attachments_StorageKey",
                schema: "files",
                table: "attachments");

            migrationBuilder.CreateIndex(
                name: "IX_attachments_StorageKey",
                schema: "files",
                table: "attachments",
                column: "StorageKey",
                unique: true);

            migrationBuilder.DropColumn(
                name: "ReferenceCount",
                schema: "files",
                table: "attachments");

            migrationBuilder.DropColumn(
                name: "ForwardedFromChannelId",
                schema: "messaging",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "ForwardedFromMessageId",
                schema: "messaging",
                table: "messages");
        }
    }
}
