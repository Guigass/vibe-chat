using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VibeChat.Infrastructure;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(VibeChatDbContext))]
    [Migration("20261002120000_AddAnnouncements")]
    public partial class AddAnnouncements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "announcements",
                schema: "messaging",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequiresAcknowledgement = table.Column<bool>(type: "boolean", nullable: false),
                    AcknowledgeBy = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_announcements", x => x.MessageId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_announcements_TenantId_ChannelId",
                schema: "messaging",
                table: "announcements",
                columns: new[] { "TenantId", "ChannelId" });

            migrationBuilder.CreateIndex(
                name: "IX_announcements_TenantId_RequiresAcknowledgement_ClosedAt_AcknowledgeBy",
                schema: "messaging",
                table: "announcements",
                columns: new[] { "TenantId", "RequiresAcknowledgement", "ClosedAt", "AcknowledgeBy" });

            migrationBuilder.CreateTable(
                name: "announcement_acknowledgements",
                schema: "messaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcknowledgedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_announcement_acknowledgements", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_announcement_acknowledgements_TenantId_MessageId_UserId",
                schema: "messaging",
                table: "announcement_acknowledgements",
                columns: new[] { "TenantId", "MessageId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_announcement_acknowledgements_TenantId_ChannelId_MessageId",
                schema: "messaging",
                table: "announcement_acknowledgements",
                columns: new[] { "TenantId", "ChannelId", "MessageId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "announcement_acknowledgements", schema: "messaging");
            migrationBuilder.DropTable(name: "announcements", schema: "messaging");
        }
    }
}
