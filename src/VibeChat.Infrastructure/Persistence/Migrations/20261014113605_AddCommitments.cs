using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCommitments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "commitment_changes",
                schema: "messaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommitmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_commitment_changes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "commitments",
                schema: "messaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    State = table.Column<int>(type: "integer", nullable: false),
                    SourceMessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceThreadId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceVersion = table.Column<long>(type: "bigint", nullable: false),
                    ExcerptHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Method = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthzSnapshot = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Visibility = table.Column<int>(type: "integer", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClosedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceRedacted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_commitments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_commitment_changes_TenantId_CommitmentId_At",
                schema: "messaging",
                table: "commitment_changes",
                columns: new[] { "TenantId", "CommitmentId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_commitments_TenantId_CreatedBy_IdempotencyKey",
                schema: "messaging",
                table: "commitments",
                columns: new[] { "TenantId", "CreatedBy", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_commitments_TenantId_OwnerUserId_State",
                schema: "messaging",
                table: "commitments",
                columns: new[] { "TenantId", "OwnerUserId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_commitments_TenantId_SourceMessageId",
                schema: "messaging",
                table: "commitments",
                columns: new[] { "TenantId", "SourceMessageId" });

            migrationBuilder.CreateIndex(
                name: "IX_commitments_TenantId_WorkspaceId_State_CreatedAt",
                schema: "messaging",
                table: "commitments",
                columns: new[] { "TenantId", "WorkspaceId", "State", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "commitment_changes",
                schema: "messaging");

            migrationBuilder.DropTable(
                name: "commitments",
                schema: "messaging");
        }
    }
}
