using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VibeChat.Infrastructure;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(VibeChatDbContext))]
    [Migration("20260817120000_AddProcessSettings")]
    public partial class AddProcessSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "administration");

            migrationBuilder.CreateTable(
                name: "process_settings",
                schema: "administration",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    AiEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    EmailEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    MessageRetentionEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    PushEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LinkPreviewEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    OpenRouterBaseUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    RetentionDefaultDays = table.Column<int>(type: "integer", nullable: false),
                    RetentionBatchSize = table.Column<int>(type: "integer", nullable: false),
                    RetentionIntervalMinutes = table.Column<int>(type: "integer", nullable: false),
                    LinkPreviewTimeoutMs = table.Column<int>(type: "integer", nullable: false),
                    VapidPublicKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    VapidPrivateKeyCiphertext = table.Column<byte[]>(type: "bytea", nullable: true),
                    VapidPrivateKeyNonce = table.Column<byte[]>(type: "bytea", nullable: true),
                    VapidPrivateKeyTag = table.Column<byte[]>(type: "bytea", nullable: true),
                    VapidPrivateKeyKeyVersion = table.Column<int>(type: "integer", nullable: true),
                    VapidPrivateKeyFormatVersion = table.Column<short>(type: "smallint", nullable: true),
                    VapidPrivateKeyMaskSuffix = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    VapidPrivateKeyRotatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VapidSubject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_process_settings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "process_settings",
                schema: "administration");
        }
    }
}
