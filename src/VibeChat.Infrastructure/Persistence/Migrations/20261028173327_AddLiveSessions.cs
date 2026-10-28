using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLiveSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "live_participants",
                schema: "conversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Muted = table.Column<bool>(type: "boolean", nullable: false),
                    CameraOn = table.Column<bool>(type: "boolean", nullable: false),
                    AudioConnected = table.Column<bool>(type: "boolean", nullable: false),
                    Reaction = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    JoinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LeftAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_live_participants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "live_sessions",
                schema: "conversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    VideoAllowed = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_live_sessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "live_tokens",
                schema: "conversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Room = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_live_tokens", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_live_participants_TenantId_SessionId_UserId",
                schema: "conversations",
                table: "live_participants",
                columns: new[] { "TenantId", "SessionId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_live_sessions_TenantId_ChannelId",
                schema: "conversations",
                table: "live_sessions",
                columns: new[] { "TenantId", "ChannelId" },
                unique: true,
                filter: "\"State\" = 'active'");

            migrationBuilder.CreateIndex(
                name: "IX_live_sessions_TenantId_ChannelId_IdempotencyKey",
                schema: "conversations",
                table: "live_sessions",
                columns: new[] { "TenantId", "ChannelId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_live_tokens_TenantId_TokenHash",
                schema: "conversations",
                table: "live_tokens",
                columns: new[] { "TenantId", "TokenHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "live_participants",
                schema: "conversations");

            migrationBuilder.DropTable(
                name: "live_sessions",
                schema: "conversations");

            migrationBuilder.DropTable(
                name: "live_tokens",
                schema: "conversations");
        }
    }
}
