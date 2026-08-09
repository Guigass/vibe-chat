using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VibeChat.Infrastructure;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(VibeChatDbContext))]
    [Migration("20260809010000_AddAttachmentAudioFields")]
    public partial class AddAttachmentAudioFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DurationMs",
                schema: "files",
                table: "attachments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                schema: "files",
                table: "attachments",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "File");

            migrationBuilder.AddColumn<int[]>(
                name: "Waveform",
                schema: "files",
                table: "attachments",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DurationMs",
                schema: "files",
                table: "attachments");

            migrationBuilder.DropColumn(
                name: "Kind",
                schema: "files",
                table: "attachments");

            migrationBuilder.DropColumn(
                name: "Waveform",
                schema: "files",
                table: "attachments");
        }
    }
}
