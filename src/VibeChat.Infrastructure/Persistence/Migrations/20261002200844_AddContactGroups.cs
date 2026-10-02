using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContactGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contact_groups",
                schema: "directory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contact_groups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "contact_group_members",
                schema: "directory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contact_group_members", x => x.Id);
                    table.ForeignKey(
                        name: "FK_contact_group_members_contact_groups_GroupId",
                        column: x => x.GroupId,
                        principalSchema: "directory",
                        principalTable: "contact_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_contact_group_members_GroupId_UserId",
                schema: "directory",
                table: "contact_group_members",
                columns: new[] { "GroupId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_contact_group_members_WorkspaceId_UserId",
                schema: "directory",
                table: "contact_group_members",
                columns: new[] { "WorkspaceId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_contact_groups_WorkspaceId_Kind_Order",
                schema: "directory",
                table: "contact_groups",
                columns: new[] { "WorkspaceId", "Kind", "Order" });

            // Case-insensitive name uniqueness. NULL owner (department) is its own scope.
            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX "IX_contact_groups_department_name"
                    ON directory.contact_groups ("WorkspaceId", "Kind", lower("Name"))
                    WHERE "OwnerUserId" IS NULL;

                CREATE UNIQUE INDEX "IX_contact_groups_personal_name"
                    ON directory.contact_groups ("WorkspaceId", "Kind", "OwnerUserId", lower("Name"))
                    WHERE "OwnerUserId" IS NOT NULL;

                CREATE OR REPLACE FUNCTION directory.detach_contact_group_members()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    DELETE FROM directory.contact_group_members
                    WHERE "TenantId" = OLD."TenantId"
                      AND "WorkspaceId" = OLD."WorkspaceId"
                      AND "UserId" = OLD."UserId";
                    RETURN OLD;
                END;
                $$;

                DROP TRIGGER IF EXISTS workspace_members_detach_contact_groups ON tenancy.workspace_members;
                CREATE TRIGGER workspace_members_detach_contact_groups
                    BEFORE DELETE ON tenancy.workspace_members
                    FOR EACH ROW
                    EXECUTE FUNCTION directory.detach_contact_group_members();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS workspace_members_detach_contact_groups ON tenancy.workspace_members;
                DROP FUNCTION IF EXISTS directory.detach_contact_group_members();
                """);

            migrationBuilder.DropTable(
                name: "contact_group_members",
                schema: "directory");

            migrationBuilder.DropTable(
                name: "contact_groups",
                schema: "directory");
        }
    }
}
