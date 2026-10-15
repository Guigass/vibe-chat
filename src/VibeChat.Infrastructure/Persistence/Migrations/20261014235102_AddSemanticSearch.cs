using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSemanticSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "search");

            migrationBuilder.CreateTable(
                name: "semantic_chunks",
                schema: "search",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKind = table.Column<int>(type: "integer", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChunkIndex = table.Column<int>(type: "integer", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: true),
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Text = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Embedding = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_semantic_chunks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "semantic_settings",
                schema: "search",
                columns: table => new
                {
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    MonthlyTokenBudget = table.Column<int>(type: "integer", nullable: false),
                    TokensUsed = table.Column<int>(type: "integer", nullable: false),
                    BudgetPeriodStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_semantic_settings", x => x.WorkspaceId);
                });

            migrationBuilder.CreateTable(
                name: "semantic_usage",
                schema: "search",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tokens = table.Column<int>(type: "integer", nullable: false),
                    CitationCount = table.Column<int>(type: "integer", nullable: false),
                    LatencyMs = table.Column<int>(type: "integer", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_semantic_usage", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_semantic_chunks_TenantId_SourceKind_SourceId_ChunkIndex",
                schema: "search",
                table: "semantic_chunks",
                columns: new[] { "TenantId", "SourceKind", "SourceId", "ChunkIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_semantic_chunks_TenantId_WorkspaceId_DeletedAt",
                schema: "search",
                table: "semantic_chunks",
                columns: new[] { "TenantId", "WorkspaceId", "DeletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_semantic_settings_TenantId",
                schema: "search",
                table: "semantic_settings",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_semantic_usage_TenantId_WorkspaceId_UserId_CreatedAt",
                schema: "search",
                table: "semantic_usage",
                columns: new[] { "TenantId", "WorkspaceId", "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "semantic_chunks",
                schema: "search");

            migrationBuilder.DropTable(
                name: "semantic_settings",
                schema: "search");

            migrationBuilder.DropTable(
                name: "semantic_usage",
                schema: "search");
        }
    }
}
