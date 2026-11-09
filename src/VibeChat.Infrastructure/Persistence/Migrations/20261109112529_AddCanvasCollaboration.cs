using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCanvasCollaboration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "canvas_documents",
                schema: "knowledge",
                columns: table => new
                {
                    PageId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Epoch = table.Column<int>(type: "integer", nullable: false),
                    Seq = table.Column<long>(type: "bigint", nullable: false),
                    CompactedThroughSeq = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<byte[]>(type: "bytea", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_canvas_documents", x => x.PageId);
                });

            migrationBuilder.CreateTable(
                name: "canvas_snapshots",
                schema: "knowledge",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Epoch = table.Column<int>(type: "integer", nullable: false),
                    Seq = table.Column<long>(type: "bigint", nullable: false),
                    PageVersion = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<byte[]>(type: "bytea", nullable: false),
                    Body = table.Column<string>(type: "character varying(16000)", maxLength: 16000, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_canvas_snapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "canvas_updates",
                schema: "knowledge",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Epoch = table.Column<int>(type: "integer", nullable: false),
                    Seq = table.Column<long>(type: "bigint", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Payload = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_canvas_updates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_canvas_documents_TenantId_WorkspaceId",
                schema: "knowledge",
                table: "canvas_documents",
                columns: new[] { "TenantId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_canvas_snapshots_TenantId_PageId_CreatedAt",
                schema: "knowledge",
                table: "canvas_snapshots",
                columns: new[] { "TenantId", "PageId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_canvas_updates_TenantId_PageId_Epoch_AuthorUserId_Idempoten~",
                schema: "knowledge",
                table: "canvas_updates",
                columns: new[] { "TenantId", "PageId", "Epoch", "AuthorUserId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_canvas_updates_TenantId_PageId_Epoch_Seq",
                schema: "knowledge",
                table: "canvas_updates",
                columns: new[] { "TenantId", "PageId", "Epoch", "Seq" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "canvas_documents",
                schema: "knowledge");

            migrationBuilder.DropTable(
                name: "canvas_snapshots",
                schema: "knowledge");

            migrationBuilder.DropTable(
                name: "canvas_updates",
                schema: "knowledge");
        }
    }
}
