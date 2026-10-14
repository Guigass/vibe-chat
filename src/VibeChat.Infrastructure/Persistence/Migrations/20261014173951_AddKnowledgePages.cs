using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKnowledgePages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "knowledge");

            migrationBuilder.CreateTable(
                name: "collections",
                schema: "knowledge",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeKind = table.Column<int>(type: "integer", nullable: false),
                    ScopeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "page_versions",
                schema: "knowledge",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Body = table.Column<string>(type: "character varying(16000)", maxLength: 16000, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RestoredFromVersion = table.Column<int>(type: "integer", nullable: true),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_page_versions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "pages",
                schema: "knowledge",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Body = table.Column<string>(type: "character varying(16000)", maxLength: 16000, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_collections_TenantId_CreatedBy_IdempotencyKey",
                schema: "knowledge",
                table: "collections",
                columns: new[] { "TenantId", "CreatedBy", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_collections_TenantId_WorkspaceId_ScopeKind_ScopeId_Slug",
                schema: "knowledge",
                table: "collections",
                columns: new[] { "TenantId", "WorkspaceId", "ScopeKind", "ScopeId", "Slug" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_page_versions_TenantId_PageId_Version",
                schema: "knowledge",
                table: "page_versions",
                columns: new[] { "TenantId", "PageId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pages_TenantId_CollectionId_Slug",
                schema: "knowledge",
                table: "pages",
                columns: new[] { "TenantId", "CollectionId", "Slug" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_pages_TenantId_CreatedBy_IdempotencyKey",
                schema: "knowledge",
                table: "pages",
                columns: new[] { "TenantId", "CreatedBy", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pages_TenantId_WorkspaceId_DeletedAt",
                schema: "knowledge",
                table: "pages",
                columns: new[] { "TenantId", "WorkspaceId", "DeletedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "collections",
                schema: "knowledge");

            migrationBuilder.DropTable(
                name: "page_versions",
                schema: "knowledge");

            migrationBuilder.DropTable(
                name: "pages",
                schema: "knowledge");
        }
    }
}
