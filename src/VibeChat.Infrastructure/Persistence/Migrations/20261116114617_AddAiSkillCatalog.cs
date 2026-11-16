using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiSkillCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "skill_definitions",
                schema: "ai",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ActiveVersionNumber = table.Column<int>(type: "integer", nullable: true),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "character varying(280)", maxLength: 280, nullable: false),
                    Instructions = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    ArgumentsSchemaJson = table.Column<string>(type: "jsonb", nullable: false),
                    ExamplesJson = table.Column<string>(type: "jsonb", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Compatibility = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Classification = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    KnowledgeGrantIds = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    McpToolGrantIds = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_skill_definitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "skill_versions",
                schema: "ai",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SkillId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    Slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "character varying(280)", maxLength: 280, nullable: false),
                    Instructions = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    InstructionsSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ArgumentsSchemaJson = table.Column<string>(type: "jsonb", nullable: false),
                    ExamplesJson = table.Column<string>(type: "jsonb", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Compatibility = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Classification = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    KnowledgeGrantIds = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    McpToolGrantIds = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    PublishedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_skill_versions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_skill_definitions_TenantId_WorkspaceId_Slug",
                schema: "ai",
                table: "skill_definitions",
                columns: new[] { "TenantId", "WorkspaceId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_skill_versions_TenantId_SkillId_VersionNumber",
                schema: "ai",
                table: "skill_versions",
                columns: new[] { "TenantId", "SkillId", "VersionNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "skill_definitions",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "skill_versions",
                schema: "ai");
        }
    }
}
