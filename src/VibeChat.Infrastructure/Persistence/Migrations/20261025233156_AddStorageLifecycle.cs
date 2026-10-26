using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStorageLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "storage_jobs",
                schema: "files",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ResultJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storage_jobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "storage_ledgers",
                schema: "files",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReservedBytes = table.Column<long>(type: "bigint", nullable: false),
                    UsedBytes = table.Column<long>(type: "bigint", nullable: false),
                    PreviousUsedBytes = table.Column<long>(type: "bigint", nullable: false),
                    SoftLimit = table.Column<long>(type: "bigint", nullable: false),
                    HardLimit = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storage_ledgers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "storage_objects",
                schema: "files",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Bytes = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Referenced = table.Column<bool>(type: "boolean", nullable: false),
                    Clean = table.Column<bool>(type: "boolean", nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    AttachmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: true),
                    AuthorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PurgeAfter = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PurgedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storage_objects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "storage_tombstones",
                schema: "files",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurgedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storage_tombstones", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_storage_jobs_TenantId_IdempotencyKey",
                schema: "files",
                table: "storage_jobs",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_storage_ledgers_TenantId_WorkspaceId_Kind",
                schema: "files",
                table: "storage_ledgers",
                columns: new[] { "TenantId", "WorkspaceId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_storage_objects_TenantId_AttachmentId",
                schema: "files",
                table: "storage_objects",
                columns: new[] { "TenantId", "AttachmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_storage_objects_TenantId_WorkspaceId_Kind",
                schema: "files",
                table: "storage_objects",
                columns: new[] { "TenantId", "WorkspaceId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_storage_tombstones_TenantId_ObjectId",
                schema: "files",
                table: "storage_tombstones",
                columns: new[] { "TenantId", "ObjectId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "storage_jobs",
                schema: "files");

            migrationBuilder.DropTable(
                name: "storage_ledgers",
                schema: "files");

            migrationBuilder.DropTable(
                name: "storage_objects",
                schema: "files");

            migrationBuilder.DropTable(
                name: "storage_tombstones",
                schema: "files");
        }
    }
}
