using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "task_assignees",
                schema: "messaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_assignees", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "task_checklist_items",
                schema: "messaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Done = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_checklist_items", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "tasks",
                schema: "messaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OverdueNotifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceRedacted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tasks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_task_assignees_TenantId_TaskId_UserId",
                schema: "messaging",
                table: "task_assignees",
                columns: new[] { "TenantId", "TaskId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_task_assignees_TenantId_UserId",
                schema: "messaging",
                table: "task_assignees",
                columns: new[] { "TenantId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_task_checklist_items_TenantId_TaskId_Position",
                schema: "messaging",
                table: "task_checklist_items",
                columns: new[] { "TenantId", "TaskId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_tasks_DueAt_OverdueNotifiedAt",
                schema: "messaging",
                table: "tasks",
                columns: new[] { "DueAt", "OverdueNotifiedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_tasks_TenantId_CreatedBy_IdempotencyKey",
                schema: "messaging",
                table: "tasks",
                columns: new[] { "TenantId", "CreatedBy", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tasks_TenantId_SourceMessageId",
                schema: "messaging",
                table: "tasks",
                columns: new[] { "TenantId", "SourceMessageId" });

            migrationBuilder.CreateIndex(
                name: "IX_tasks_TenantId_WorkspaceId_State_CreatedAt",
                schema: "messaging",
                table: "tasks",
                columns: new[] { "TenantId", "WorkspaceId", "State", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "task_assignees",
                schema: "messaging");

            migrationBuilder.DropTable(
                name: "task_checklist_items",
                schema: "messaging");

            migrationBuilder.DropTable(
                name: "tasks",
                schema: "messaging");
        }
    }
}
