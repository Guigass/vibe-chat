using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBridges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bridge_events",
                schema: "integrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    BridgeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Direction = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    EventType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RemoteEventId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    LocalMessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    RemoteSubject = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Link = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Cursor = table.Column<long>(type: "bigint", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bridge_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "bridge_identities",
                schema: "integrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    BridgeId = table.Column<Guid>(type: "uuid", nullable: false),
                    RemoteSubject = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    LocalMarker = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bridge_identities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "bridges",
                schema: "integrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    BotId = table.Column<Guid>(type: "uuid", nullable: false),
                    Network = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RemoteRoom = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    DestinationHost = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Capabilities = table.Column<string[]>(type: "text[]", nullable: false),
                    DeleteRemoteGuaranteed = table.Column<bool>(type: "boolean", nullable: false),
                    ConsentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConsentedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    LocalCheckpoint = table.Column<long>(type: "bigint", nullable: false),
                    RemoteCheckpoint = table.Column<long>(type: "bigint", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bridges", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_bridge_events_TenantId_BridgeId_RemoteEventId",
                schema: "integrations",
                table: "bridge_events",
                columns: new[] { "TenantId", "BridgeId", "RemoteEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bridge_events_TenantId_LocalMessageId",
                schema: "integrations",
                table: "bridge_events",
                columns: new[] { "TenantId", "LocalMessageId" });

            migrationBuilder.CreateIndex(
                name: "IX_bridge_identities_TenantId_BridgeId_RemoteSubject",
                schema: "integrations",
                table: "bridge_identities",
                columns: new[] { "TenantId", "BridgeId", "RemoteSubject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bridges_TenantId_WorkspaceId_ChannelId",
                schema: "integrations",
                table: "bridges",
                columns: new[] { "TenantId", "WorkspaceId", "ChannelId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bridges_TenantId_WorkspaceId_IdempotencyKey",
                schema: "integrations",
                table: "bridges",
                columns: new[] { "TenantId", "WorkspaceId", "IdempotencyKey" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bridge_events",
                schema: "integrations");

            migrationBuilder.DropTable(
                name: "bridge_identities",
                schema: "integrations");

            migrationBuilder.DropTable(
                name: "bridges",
                schema: "integrations");
        }
    }
}
