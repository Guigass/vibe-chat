using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VibeChat.Infrastructure;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// B-185: personal wallpaper and accent, one row per tenant and user.
    /// </summary>
    [DbContext(typeof(VibeChatDbContext))]
    [Migration("20261002120000_AddUserVisualPreferences")]
    public partial class AddUserVisualPreferences : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "visual_preferences",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChatWallpaperId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    AccentColorId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_visual_preferences", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_visual_preferences_TenantId_UserId",
                schema: "identity",
                table: "visual_preferences",
                columns: new[] { "TenantId", "UserId" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "visual_preferences",
                schema: "identity");
        }
    }
}
