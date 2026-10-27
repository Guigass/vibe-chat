using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFederation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "federation_events",
                schema: "integrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    TrustId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoomId = table.Column<Guid>(type: "uuid", nullable: false),
                    Direction = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    EventType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RemoteEventId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    LocalMessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    RemoteSubject = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Cursor = table.Column<long>(type: "bigint", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_federation_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "federation_identities",
                schema: "integrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    TrustId = table.Column<Guid>(type: "uuid", nullable: false),
                    RemoteSubject = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    RemoteInstanceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LocalMarker = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_federation_identities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "federation_rooms",
                schema: "integrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    TrustId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    RemoteRoom = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    LocalCheckpoint = table.Column<long>(type: "bigint", nullable: false),
                    RemoteCheckpoint = table.Column<long>(type: "bigint", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_federation_rooms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "federation_trusts",
                schema: "integrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    BotId = table.Column<Guid>(type: "uuid", nullable: false),
                    Domain = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    RemoteInstanceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Capabilities = table.Column<string[]>(type: "text[]", nullable: false),
                    CurrentKeyId = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PreviousKeyId = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    CurrentTokenId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousTokenId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeleteRemoteGuaranteed = table.Column<bool>(type: "boolean", nullable: false),
                    ConsentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConsentedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_federation_trusts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_federation_events_TenantId_TrustId_RemoteEventId",
                schema: "integrations",
                table: "federation_events",
                columns: new[] { "TenantId", "TrustId", "RemoteEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_federation_identities_TenantId_TrustId_RemoteSubject",
                schema: "integrations",
                table: "federation_identities",
                columns: new[] { "TenantId", "TrustId", "RemoteSubject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_federation_rooms_TenantId_ChannelId",
                schema: "integrations",
                table: "federation_rooms",
                columns: new[] { "TenantId", "ChannelId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_federation_rooms_TenantId_TrustId_RemoteRoom",
                schema: "integrations",
                table: "federation_rooms",
                columns: new[] { "TenantId", "TrustId", "RemoteRoom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_federation_trusts_TenantId_WorkspaceId_Domain_RemoteInstanc~",
                schema: "integrations",
                table: "federation_trusts",
                columns: new[] { "TenantId", "WorkspaceId", "Domain", "RemoteInstanceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_federation_trusts_TenantId_WorkspaceId_IdempotencyKey",
                schema: "integrations",
                table: "federation_trusts",
                columns: new[] { "TenantId", "WorkspaceId", "IdempotencyKey" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "federation_events",
                schema: "integrations");

            migrationBuilder.DropTable(
                name: "federation_identities",
                schema: "integrations");

            migrationBuilder.DropTable(
                name: "federation_rooms",
                schema: "integrations");

            migrationBuilder.DropTable(
                name: "federation_trusts",
                schema: "integrations");
        }
    }
}
