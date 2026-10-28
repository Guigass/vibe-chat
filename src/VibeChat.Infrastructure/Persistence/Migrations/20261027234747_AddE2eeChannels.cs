using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddE2eeChannels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "E2eeGeneration",
                schema: "conversations",
                table: "channels",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "e2ee_backups",
                schema: "messaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Blob = table.Column<string>(type: "character varying(16384)", maxLength: 16384, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_e2ee_backups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "e2ee_devices",
                schema: "messaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicJwk = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_e2ee_devices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "e2ee_verifications",
                schema: "messaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_e2ee_verifications", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_e2ee_backups_TenantId_UserId",
                schema: "messaging",
                table: "e2ee_backups",
                columns: new[] { "TenantId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_e2ee_devices_TenantId_UserId",
                schema: "messaging",
                table: "e2ee_devices",
                columns: new[] { "TenantId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_e2ee_verifications_TenantId_UserId_DeviceId",
                schema: "messaging",
                table: "e2ee_verifications",
                columns: new[] { "TenantId", "UserId", "DeviceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "e2ee_backups",
                schema: "messaging");

            migrationBuilder.DropTable(
                name: "e2ee_devices",
                schema: "messaging");

            migrationBuilder.DropTable(
                name: "e2ee_verifications",
                schema: "messaging");

            migrationBuilder.DropColumn(
                name: "E2eeGeneration",
                schema: "conversations",
                table: "channels");
        }
    }
}
