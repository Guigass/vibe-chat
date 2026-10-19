using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VibeChat.Infrastructure;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(VibeChatDbContext))]
    [Migration("20261019120000_AddAttachmentScanFields")]
    public partial class AddAttachmentScanFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ScanStatus",
                schema: "files",
                table: "attachments",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScanEngine",
                schema: "files",
                table: "attachments",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScanSignatureVersion",
                schema: "files",
                table: "attachments",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScanReason",
                schema: "files",
                table: "attachments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ScanAttempts",
                schema: "files",
                table: "attachments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ScanRequestedAt",
                schema: "files",
                table: "attachments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ScannedAt",
                schema: "files",
                table: "attachments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_attachments_TenantId_ScanStatus",
                schema: "files",
                table: "attachments",
                columns: new[] { "TenantId", "ScanStatus" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_attachments_TenantId_ScanStatus",
                schema: "files",
                table: "attachments");

            migrationBuilder.DropColumn(name: "ScanStatus", schema: "files", table: "attachments");
            migrationBuilder.DropColumn(name: "ScanEngine", schema: "files", table: "attachments");
            migrationBuilder.DropColumn(name: "ScanSignatureVersion", schema: "files", table: "attachments");
            migrationBuilder.DropColumn(name: "ScanReason", schema: "files", table: "attachments");
            migrationBuilder.DropColumn(name: "ScanAttempts", schema: "files", table: "attachments");
            migrationBuilder.DropColumn(name: "ScanRequestedAt", schema: "files", table: "attachments");
            migrationBuilder.DropColumn(name: "ScannedAt", schema: "files", table: "attachments");
        }
    }
}
