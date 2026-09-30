using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VibeChat.Infrastructure;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(VibeChatDbContext))]
    [Migration("20260929180000_AddMessageLifecyclePolicies")]
    public partial class AddMessageLifecyclePolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "message_lifecycle_policies",
                schema: "messaging",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EditEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    EditWindowMinutes = table.Column<int>(type: "integer", nullable: true),
                    EditRoles = table.Column<string[]>(type: "text[]", nullable: false),
                    EditRolesRestricted = table.Column<bool>(type: "boolean", nullable: false),
                    EditAllowModeratorOverride = table.Column<bool>(type: "boolean", nullable: false),
                    DeleteEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    DeleteWindowMinutes = table.Column<int>(type: "integer", nullable: true),
                    DeleteRoles = table.Column<string[]>(type: "text[]", nullable: false),
                    DeleteRolesRestricted = table.Column<bool>(type: "boolean", nullable: false),
                    DeleteAllowModeratorOverride = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_message_lifecycle_policies", x => x.TenantId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "message_lifecycle_policies",
                schema: "messaging");
        }
    }
}
