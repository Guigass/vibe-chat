using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddForms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "form_decisions",
                schema: "messaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Decision = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_form_decisions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "form_submissions",
                schema: "messaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FormId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    AnswersJson = table.Column<string>(type: "jsonb", nullable: false),
                    AnswersPurged = table.Column<bool>(type: "boolean", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    SubmittedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    TitleSnapshot = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RetainUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResultMessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResultTaskId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_form_submissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "form_versions",
                schema: "messaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FormId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    SchemaJson = table.Column<string>(type: "jsonb", nullable: false),
                    PublishedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_form_versions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "forms",
                schema: "messaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PublishedVersion = table.Column<int>(type: "integer", nullable: false),
                    DraftSchemaJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_forms", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_form_decisions_TenantId_ActorUserId_IdempotencyKey",
                schema: "messaging",
                table: "form_decisions",
                columns: new[] { "TenantId", "ActorUserId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_form_decisions_TenantId_SubmissionId_ActorUserId",
                schema: "messaging",
                table: "form_decisions",
                columns: new[] { "TenantId", "SubmissionId", "ActorUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_form_decisions_TenantId_SubmissionId_Version",
                schema: "messaging",
                table: "form_decisions",
                columns: new[] { "TenantId", "SubmissionId", "Version" });

            migrationBuilder.CreateIndex(
                name: "IX_form_submissions_TenantId_FormId_SubmittedAt",
                schema: "messaging",
                table: "form_submissions",
                columns: new[] { "TenantId", "FormId", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_form_submissions_TenantId_RetainUntil_AnswersPurged",
                schema: "messaging",
                table: "form_submissions",
                columns: new[] { "TenantId", "RetainUntil", "AnswersPurged" });

            migrationBuilder.CreateIndex(
                name: "IX_form_submissions_TenantId_SubmittedBy_IdempotencyKey",
                schema: "messaging",
                table: "form_submissions",
                columns: new[] { "TenantId", "SubmittedBy", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_form_versions_TenantId_FormId_Version",
                schema: "messaging",
                table: "form_versions",
                columns: new[] { "TenantId", "FormId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_forms_TenantId_ChannelId_Status_UpdatedAt",
                schema: "messaging",
                table: "forms",
                columns: new[] { "TenantId", "ChannelId", "Status", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_forms_TenantId_CreatedBy_IdempotencyKey",
                schema: "messaging",
                table: "forms",
                columns: new[] { "TenantId", "CreatedBy", "IdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "form_decisions",
                schema: "messaging");

            migrationBuilder.DropTable(
                name: "form_submissions",
                schema: "messaging");

            migrationBuilder.DropTable(
                name: "form_versions",
                schema: "messaging");

            migrationBuilder.DropTable(
                name: "forms",
                schema: "messaging");
        }
    }
}
