using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBrandSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "brand_settings",
                schema: "directory",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    BrandColor = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    AccentColor = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    LogoLightKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LogoDarkKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    FaviconKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LogoLightContentType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    LogoDarkContentType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    FaviconContentType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    AssetVersion = table.Column<int>(type: "integer", nullable: false),
                    PreviousJson = table.Column<string>(type: "jsonb", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_brand_settings", x => x.TenantId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "brand_settings",
                schema: "directory");
        }
    }
}
