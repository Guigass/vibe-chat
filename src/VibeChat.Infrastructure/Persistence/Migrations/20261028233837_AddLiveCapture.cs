using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLiveCapture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SharingScreen",
                schema: "conversations",
                table: "live_participants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "live_policies",
                schema: "conversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShareEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    RecordingEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_live_policies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "live_recording_consents",
                schema: "conversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordingId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Decision = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_live_recording_consents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "live_recordings",
                schema: "conversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StartedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ObjectKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ByteSize = table.Column<long>(type: "bigint", nullable: false),
                    ScanStatus = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    OnHold = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    StoppedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_live_recordings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_live_policies_TenantId_WorkspaceId",
                schema: "conversations",
                table: "live_policies",
                columns: new[] { "TenantId", "WorkspaceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_live_recording_consents_TenantId_RecordingId_UserId",
                schema: "conversations",
                table: "live_recording_consents",
                columns: new[] { "TenantId", "RecordingId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_live_recordings_TenantId_SessionId_IdempotencyKey",
                schema: "conversations",
                table: "live_recordings",
                columns: new[] { "TenantId", "SessionId", "IdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "live_policies",
                schema: "conversations");

            migrationBuilder.DropTable(
                name: "live_recording_consents",
                schema: "conversations");

            migrationBuilder.DropTable(
                name: "live_recordings",
                schema: "conversations");

            migrationBuilder.DropColumn(
                name: "SharingScreen",
                schema: "conversations",
                table: "live_participants");
        }
    }
}
