using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLinkPreview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "link_preview_settings",
                schema: "messaging",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_link_preview_settings", x => x.TenantId);
                });

            migrationBuilder.CreateTable(
                name: "link_previews",
                schema: "messaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UrlHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Title = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    Description = table.Column<string>(type: "character varying(480)", maxLength: 480, nullable: true),
                    SiteName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ImageKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ImageContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    FetchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_link_previews", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "message_link_previews",
                schema: "messaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkPreviewId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RemovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_message_link_previews", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_link_previews_TenantId_UrlHash",
                schema: "messaging",
                table: "link_previews",
                columns: new[] { "TenantId", "UrlHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_message_link_previews_LinkPreviewId",
                schema: "messaging",
                table: "message_link_previews",
                column: "LinkPreviewId");

            migrationBuilder.CreateIndex(
                name: "IX_message_link_previews_TenantId_MessageId",
                schema: "messaging",
                table: "message_link_previews",
                columns: new[] { "TenantId", "MessageId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "link_preview_settings",
                schema: "messaging");

            migrationBuilder.DropTable(
                name: "link_previews",
                schema: "messaging");

            migrationBuilder.DropTable(
                name: "message_link_previews",
                schema: "messaging");
        }
    }
}
