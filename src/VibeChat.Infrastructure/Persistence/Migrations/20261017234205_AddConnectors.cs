using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConnectors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "connectors");

            migrationBuilder.CreateTable(
                name: "checkpoints",
                schema: "connectors",
                columns: table => new
                {
                    InstallationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Cursor = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_checkpoints", x => x.InstallationId);
                });

            migrationBuilder.CreateTable(
                name: "deliveries",
                schema: "connectors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstallationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Direction = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deliveries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "effects",
                schema: "connectors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstallationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalEventId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    DestinationKind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DestinationId = table.Column<Guid>(type: "uuid", nullable: false),
                    EffectRef = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_effects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "installations",
                schema: "connectors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    OpaqueKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DestinationKind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DestinationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Scopes = table.Column<string[]>(type: "text[]", nullable: false),
                    AllowlistHosts = table.Column<string[]>(type: "text[]", nullable: false),
                    BaseUrl = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ClientId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    OauthStateHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AccessTokenCiphertext = table.Column<byte[]>(type: "bytea", nullable: true),
                    AccessTokenNonce = table.Column<byte[]>(type: "bytea", nullable: true),
                    AccessTokenTag = table.Column<byte[]>(type: "bytea", nullable: true),
                    AccessTokenKeyVersion = table.Column<int>(type: "integer", nullable: true),
                    AccessTokenFormatVersion = table.Column<short>(type: "smallint", nullable: true),
                    AccessTokenMaskSuffix = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    AccessTokenRotatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RefreshTokenCiphertext = table.Column<byte[]>(type: "bytea", nullable: true),
                    RefreshTokenNonce = table.Column<byte[]>(type: "bytea", nullable: true),
                    RefreshTokenTag = table.Column<byte[]>(type: "bytea", nullable: true),
                    RefreshTokenKeyVersion = table.Column<int>(type: "integer", nullable: true),
                    RefreshTokenFormatVersion = table.Column<short>(type: "smallint", nullable: true),
                    RefreshTokenMaskSuffix = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    RefreshTokenRotatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OauthVerifierCiphertext = table.Column<byte[]>(type: "bytea", nullable: true),
                    OauthVerifierNonce = table.Column<byte[]>(type: "bytea", nullable: true),
                    OauthVerifierTag = table.Column<byte[]>(type: "bytea", nullable: true),
                    OauthVerifierKeyVersion = table.Column<int>(type: "integer", nullable: true),
                    OauthVerifierFormatVersion = table.Column<short>(type: "smallint", nullable: true),
                    OauthVerifierMaskSuffix = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    OauthVerifierRotatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SecretVersion = table.Column<int>(type: "integer", nullable: false),
                    Circuit = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ConsecutiveFailures = table.Column<int>(type: "integer", nullable: false),
                    OpenUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastErrorCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    IdentityUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_installations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_deliveries_TenantId_InstallationId_CreatedAt",
                schema: "connectors",
                table: "deliveries",
                columns: new[] { "TenantId", "InstallationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_effects_InstallationId_ExternalEventId",
                schema: "connectors",
                table: "effects",
                columns: new[] { "InstallationId", "ExternalEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_installations_OpaqueKey",
                schema: "connectors",
                table: "installations",
                column: "OpaqueKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_installations_TenantId_CreatedBy_IdempotencyKey",
                schema: "connectors",
                table: "installations",
                columns: new[] { "TenantId", "CreatedBy", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_installations_TenantId_WorkspaceId_Status",
                schema: "connectors",
                table: "installations",
                columns: new[] { "TenantId", "WorkspaceId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "checkpoints",
                schema: "connectors");

            migrationBuilder.DropTable(
                name: "deliveries",
                schema: "connectors");

            migrationBuilder.DropTable(
                name: "effects",
                schema: "connectors");

            migrationBuilder.DropTable(
                name: "installations",
                schema: "connectors");
        }
    }
}
