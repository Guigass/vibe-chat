using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSiemExport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "siem_deliveries",
                schema: "compliance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_siem_deliveries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "siem_destinations",
                schema: "compliance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Transport = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Url = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    SigningSecretCiphertext = table.Column<byte[]>(type: "bytea", nullable: true),
                    SigningSecretNonce = table.Column<byte[]>(type: "bytea", nullable: true),
                    SigningSecretTag = table.Column<byte[]>(type: "bytea", nullable: true),
                    SigningSecretKeyVersion = table.Column<int>(type: "integer", nullable: true),
                    SigningSecretFormatVersion = table.Column<short>(type: "smallint", nullable: true),
                    SigningSecretMaskSuffix = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    SigningSecretRotatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CategoriesJson = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    MinSeverity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Minimize = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Circuit = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    FailureCount = table.Column<int>(type: "integer", nullable: false),
                    OpenUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSuccessAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CheckpointAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CheckpointEventId = table.Column<Guid>(type: "uuid", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RotateKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TestKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TestEventId = table.Column<Guid>(type: "uuid", nullable: true),
                    BackfillKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    BackfillCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_siem_destinations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_siem_deliveries_DestinationId_EventId",
                schema: "compliance",
                table: "siem_deliveries",
                columns: new[] { "DestinationId", "EventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_siem_destinations_TenantId_WorkspaceId_IdempotencyKey",
                schema: "compliance",
                table: "siem_destinations",
                columns: new[] { "TenantId", "WorkspaceId", "IdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "siem_deliveries",
                schema: "compliance");

            migrationBuilder.DropTable(
                name: "siem_destinations",
                schema: "compliance");
        }
    }
}
