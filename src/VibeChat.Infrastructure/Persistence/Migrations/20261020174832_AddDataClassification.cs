using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDataClassification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "data_labels",
                schema: "compliance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Rank = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_data_labels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "dlp_decisions",
                schema: "compliance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Effect = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Simulation = table.Column<bool>(type: "boolean", nullable: false),
                    Inspectable = table.Column<bool>(type: "boolean", nullable: false),
                    LabelKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    MatchCount = table.Column<int>(type: "integer", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dlp_decisions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "dlp_detectors",
                schema: "compliance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Pattern = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RaiseRank = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dlp_detectors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "dlp_exceptions",
                schema: "compliance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ScopeKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ScopeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dlp_exceptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "dlp_false_positives",
                schema: "compliance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    DetectorId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ReviewedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dlp_false_positives", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "dlp_policies",
                schema: "compliance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Simulation = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dlp_policies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "dlp_rules",
                schema: "compliance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MinRank = table.Column<int>(type: "integer", nullable: false),
                    Effect = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dlp_rules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "label_assignments",
                schema: "compliance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ScopeId = table.Column<Guid>(type: "uuid", nullable: false),
                    LabelKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_label_assignments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_data_labels_TenantId_CreatedBy_IdempotencyKey",
                schema: "compliance",
                table: "data_labels",
                columns: new[] { "TenantId", "CreatedBy", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_data_labels_TenantId_WorkspaceId_Key",
                schema: "compliance",
                table: "data_labels",
                columns: new[] { "TenantId", "WorkspaceId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_dlp_decisions_TenantId_WorkspaceId_CreatedAt",
                schema: "compliance",
                table: "dlp_decisions",
                columns: new[] { "TenantId", "WorkspaceId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_dlp_detectors_TenantId_CreatedBy_IdempotencyKey",
                schema: "compliance",
                table: "dlp_detectors",
                columns: new[] { "TenantId", "CreatedBy", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_dlp_exceptions_TenantId_CreatedBy_IdempotencyKey",
                schema: "compliance",
                table: "dlp_exceptions",
                columns: new[] { "TenantId", "CreatedBy", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_dlp_false_positives_TenantId_ReviewedBy_IdempotencyKey",
                schema: "compliance",
                table: "dlp_false_positives",
                columns: new[] { "TenantId", "ReviewedBy", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_dlp_false_positives_TenantId_WorkspaceId_DetectorId_Subject~",
                schema: "compliance",
                table: "dlp_false_positives",
                columns: new[] { "TenantId", "WorkspaceId", "DetectorId", "SubjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_dlp_policies_TenantId_WorkspaceId",
                schema: "compliance",
                table: "dlp_policies",
                columns: new[] { "TenantId", "WorkspaceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_dlp_rules_TenantId_WorkspaceId_Action",
                schema: "compliance",
                table: "dlp_rules",
                columns: new[] { "TenantId", "WorkspaceId", "Action" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_label_assignments_TenantId_WorkspaceId_ScopeKind_ScopeId",
                schema: "compliance",
                table: "label_assignments",
                columns: new[] { "TenantId", "WorkspaceId", "ScopeKind", "ScopeId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "data_labels",
                schema: "compliance");

            migrationBuilder.DropTable(
                name: "dlp_decisions",
                schema: "compliance");

            migrationBuilder.DropTable(
                name: "dlp_detectors",
                schema: "compliance");

            migrationBuilder.DropTable(
                name: "dlp_exceptions",
                schema: "compliance");

            migrationBuilder.DropTable(
                name: "dlp_false_positives",
                schema: "compliance");

            migrationBuilder.DropTable(
                name: "dlp_policies",
                schema: "compliance");

            migrationBuilder.DropTable(
                name: "dlp_rules",
                schema: "compliance");

            migrationBuilder.DropTable(
                name: "label_assignments",
                schema: "compliance");
        }
    }
}
