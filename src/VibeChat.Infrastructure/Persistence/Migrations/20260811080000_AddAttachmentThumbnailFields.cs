using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VibeChat.Infrastructure;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(VibeChatDbContext))]
    [Migration("20260811080000_AddAttachmentThumbnailFields")]
    public partial class AddAttachmentThumbnailFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ThumbnailKey",
                schema: "files",
                table: "attachments",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Width",
                schema: "files",
                table: "attachments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Height",
                schema: "files",
                table: "attachments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThumbnailStatus",
                schema: "files",
                table: "attachments",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PageCount",
                schema: "files",
                table: "attachments",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ThumbnailKey",
                schema: "files",
                table: "attachments");

            migrationBuilder.DropColumn(
                name: "Width",
                schema: "files",
                table: "attachments");

            migrationBuilder.DropColumn(
                name: "Height",
                schema: "files",
                table: "attachments");

            migrationBuilder.DropColumn(
                name: "ThumbnailStatus",
                schema: "files",
                table: "attachments");

            migrationBuilder.DropColumn(
                name: "PageCount",
                schema: "files",
                table: "attachments");
        }
    }
}
