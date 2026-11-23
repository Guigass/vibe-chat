using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBotKnowledgeSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bot_knowledge_grants",
                schema: "ai",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    BotId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    GrantedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bot_knowledge_grants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "knowledge_chunks",
                schema: "ai",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    ChunkIndex = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Embedding = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    EmbeddingProfile = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_chunks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "knowledge_ingestion_jobs",
                schema: "ai",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_ingestion_jobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "knowledge_source_versions",
                schema: "ai",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    ContentSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Text = table.Column<string>(type: "character varying(32000)", maxLength: 32000, nullable: false),
                    ChunkCount = table.Column<int>(type: "integer", nullable: false),
                    Checksum = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EmbeddingProfile = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_source_versions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "knowledge_sources",
                schema: "ai",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Classification = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ChunkCount = table.Column<int>(type: "integer", nullable: false),
                    Checksum = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContentSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EmbeddingProfile = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ProjectorVersion = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    LastError = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Locator = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    FileId = table.Column<Guid>(type: "uuid", nullable: true),
                    PageId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurgeEvidence = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_sources", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_bot_knowledge_grants_TenantId_BotId_SourceId_RevokedAt",
                schema: "ai",
                table: "bot_knowledge_grants",
                columns: new[] { "TenantId", "BotId", "SourceId", "RevokedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_chunks_TenantId_SourceId_VersionNumber_ChunkIndex",
                schema: "ai",
                table: "knowledge_chunks",
                columns: new[] { "TenantId", "SourceId", "VersionNumber", "ChunkIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_ingestion_jobs_TenantId_SourceId_CreatedAt",
                schema: "ai",
                table: "knowledge_ingestion_jobs",
                columns: new[] { "TenantId", "SourceId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_source_versions_TenantId_SourceId_VersionNumber",
                schema: "ai",
                table: "knowledge_source_versions",
                columns: new[] { "TenantId", "SourceId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_sources_TenantId_WorkspaceId_CreatedAt",
                schema: "ai",
                table: "knowledge_sources",
                columns: new[] { "TenantId", "WorkspaceId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bot_knowledge_grants",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "knowledge_chunks",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "knowledge_ingestion_jobs",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "knowledge_source_versions",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "knowledge_sources",
                schema: "ai");
        }
    }
}
