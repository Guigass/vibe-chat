using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(VibeChatDbContext))]
    [Migration("20261013180000_AddInboxSnoozes")]
    public partial class AddInboxSnoozes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inbox_snoozes",
                schema: "messaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SnoozeUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbox_snoozes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_inbox_snoozes_TenantId_UserId_ItemKey",
                schema: "messaging",
                table: "inbox_snoozes",
                columns: new[] { "TenantId", "UserId", "ItemKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inbox_snoozes_TenantId_UserId_WorkspaceId_SnoozeUntil",
                schema: "messaging",
                table: "inbox_snoozes",
                columns: new[] { "TenantId", "UserId", "WorkspaceId", "SnoozeUntil" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inbox_snoozes",
                schema: "messaging");
        }
    }
}
