using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VibeChat.Infrastructure;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(VibeChatDbContext))]
    [Migration("20260724153000_AddChannelSpaces")]
    public partial class AddChannelSpaces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Order",
                schema: "directory",
                table: "spaces",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "SpaceId",
                schema: "conversations",
                table: "channels",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_spaces_WorkspaceId_Order",
                schema: "directory",
                table: "spaces",
                columns: new[] { "WorkspaceId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_channels_SpaceId",
                schema: "conversations",
                table: "channels",
                column: "SpaceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_channels_SpaceId",
                schema: "conversations",
                table: "channels");

            migrationBuilder.DropIndex(
                name: "IX_spaces_WorkspaceId_Order",
                schema: "directory",
                table: "spaces");

            migrationBuilder.DropColumn(
                name: "SpaceId",
                schema: "conversations",
                table: "channels");

            migrationBuilder.DropColumn(
                name: "Order",
                schema: "directory",
                table: "spaces");
        }
    }
}
