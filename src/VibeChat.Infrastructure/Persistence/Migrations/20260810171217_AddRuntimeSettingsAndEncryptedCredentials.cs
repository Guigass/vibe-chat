using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRuntimeSettingsAndEncryptedCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Secret",
                schema: "integrations",
                table: "webhook_endpoints",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512);

            migrationBuilder.AddColumn<byte[]>(
                name: "SigningSecretCiphertext",
                schema: "integrations",
                table: "webhook_endpoints",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "SigningSecretFormatVersion",
                schema: "integrations",
                table: "webhook_endpoints",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SigningSecretKeyVersion",
                schema: "integrations",
                table: "webhook_endpoints",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SigningSecretMaskSuffix",
                schema: "integrations",
                table: "webhook_endpoints",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "SigningSecretNonce",
                schema: "integrations",
                table: "webhook_endpoints",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SigningSecretRotatedAt",
                schema: "integrations",
                table: "webhook_endpoints",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "SigningSecretTag",
                schema: "integrations",
                table: "webhook_endpoints",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "OpenRouterApiKeyCiphertext",
                schema: "ai",
                table: "settings",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "OpenRouterApiKeyFormatVersion",
                schema: "ai",
                table: "settings",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OpenRouterApiKeyKeyVersion",
                schema: "ai",
                table: "settings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OpenRouterApiKeyMaskSuffix",
                schema: "ai",
                table: "settings",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "OpenRouterApiKeyNonce",
                schema: "ai",
                table: "settings",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OpenRouterApiKeyRotatedAt",
                schema: "ai",
                table: "settings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "OpenRouterApiKeyTag",
                schema: "ai",
                table: "settings",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "SmtpPasswordCiphertext",
                schema: "notifications",
                table: "email_settings",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "SmtpPasswordFormatVersion",
                schema: "notifications",
                table: "email_settings",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SmtpPasswordKeyVersion",
                schema: "notifications",
                table: "email_settings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SmtpPasswordMaskSuffix",
                schema: "notifications",
                table: "email_settings",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "SmtpPasswordNonce",
                schema: "notifications",
                table: "email_settings",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SmtpPasswordRotatedAt",
                schema: "notifications",
                table: "email_settings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "SmtpPasswordTag",
                schema: "notifications",
                table: "email_settings",
                type: "bytea",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "rate_limit_settings",
                schema: "building_blocks",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SendPerMinute = table.Column<int>(type: "integer", nullable: false),
                    HubPerMinute = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rate_limit_settings", x => x.TenantId);
                });

            migrationBuilder.CreateTable(
                name: "settings",
                schema: "files",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    MaxSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    MaxAttachmentsPerMessage = table.Column<int>(type: "integer", nullable: false),
                    PresignUploadTtlSeconds = table.Column<int>(type: "integer", nullable: false),
                    PresignDownloadTtlSeconds = table.Column<int>(type: "integer", nullable: false),
                    AllowedContentTypes = table.Column<string[]>(type: "text[]", nullable: false),
                    AudioMaxSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    AudioMaxDurationMs = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_files_settings", x => x.TenantId);
                });

            migrationBuilder.Sql("""
                ALTER TABLE ai.settings
                ADD CONSTRAINT ck_ai_settings_openrouter_envelope CHECK (
                    ("OpenRouterApiKeyCiphertext" IS NULL
                        AND "OpenRouterApiKeyNonce" IS NULL
                        AND "OpenRouterApiKeyTag" IS NULL
                        AND "OpenRouterApiKeyKeyVersion" IS NULL
                        AND "OpenRouterApiKeyFormatVersion" IS NULL)
                    OR (
                        "OpenRouterApiKeyCiphertext" IS NOT NULL
                        AND octet_length("OpenRouterApiKeyCiphertext") > 0
                        AND "OpenRouterApiKeyNonce" IS NOT NULL
                        AND octet_length("OpenRouterApiKeyNonce") = 12
                        AND "OpenRouterApiKeyTag" IS NOT NULL
                        AND octet_length("OpenRouterApiKeyTag") = 16
                        AND "OpenRouterApiKeyKeyVersion" IS NOT NULL
                        AND "OpenRouterApiKeyKeyVersion" > 0
                        AND "OpenRouterApiKeyFormatVersion" IS NOT NULL
                        AND "OpenRouterApiKeyFormatVersion" > 0
                    )
                );
                """);

            migrationBuilder.Sql("""
                ALTER TABLE notifications.email_settings
                ADD CONSTRAINT ck_email_settings_smtp_envelope CHECK (
                    ("SmtpPasswordCiphertext" IS NULL
                        AND "SmtpPasswordNonce" IS NULL
                        AND "SmtpPasswordTag" IS NULL
                        AND "SmtpPasswordKeyVersion" IS NULL
                        AND "SmtpPasswordFormatVersion" IS NULL)
                    OR (
                        "SmtpPasswordCiphertext" IS NOT NULL
                        AND octet_length("SmtpPasswordCiphertext") > 0
                        AND "SmtpPasswordNonce" IS NOT NULL
                        AND octet_length("SmtpPasswordNonce") = 12
                        AND "SmtpPasswordTag" IS NOT NULL
                        AND octet_length("SmtpPasswordTag") = 16
                        AND "SmtpPasswordKeyVersion" IS NOT NULL
                        AND "SmtpPasswordKeyVersion" > 0
                        AND "SmtpPasswordFormatVersion" IS NOT NULL
                        AND "SmtpPasswordFormatVersion" > 0
                    )
                );
                """);

            migrationBuilder.Sql("""
                ALTER TABLE integrations.webhook_endpoints
                ADD CONSTRAINT ck_webhook_signing_secret_envelope CHECK (
                    ("SigningSecretCiphertext" IS NULL
                        AND "SigningSecretNonce" IS NULL
                        AND "SigningSecretTag" IS NULL
                        AND "SigningSecretKeyVersion" IS NULL
                        AND "SigningSecretFormatVersion" IS NULL)
                    OR (
                        "SigningSecretCiphertext" IS NOT NULL
                        AND octet_length("SigningSecretCiphertext") > 0
                        AND "SigningSecretNonce" IS NOT NULL
                        AND octet_length("SigningSecretNonce") = 12
                        AND "SigningSecretTag" IS NOT NULL
                        AND octet_length("SigningSecretTag") = 16
                        AND "SigningSecretKeyVersion" IS NOT NULL
                        AND "SigningSecretKeyVersion" > 0
                        AND "SigningSecretFormatVersion" IS NOT NULL
                        AND "SigningSecretFormatVersion" > 0
                    )
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE ai.settings DROP CONSTRAINT IF EXISTS ck_ai_settings_openrouter_envelope;");
            migrationBuilder.Sql("ALTER TABLE notifications.email_settings DROP CONSTRAINT IF EXISTS ck_email_settings_smtp_envelope;");
            migrationBuilder.Sql("ALTER TABLE integrations.webhook_endpoints DROP CONSTRAINT IF EXISTS ck_webhook_signing_secret_envelope;");

            migrationBuilder.DropTable(
                name: "rate_limit_settings",
                schema: "building_blocks");

            migrationBuilder.DropTable(
                name: "settings",
                schema: "files");

            migrationBuilder.DropColumn(
                name: "SigningSecretCiphertext",
                schema: "integrations",
                table: "webhook_endpoints");

            migrationBuilder.DropColumn(
                name: "SigningSecretFormatVersion",
                schema: "integrations",
                table: "webhook_endpoints");

            migrationBuilder.DropColumn(
                name: "SigningSecretKeyVersion",
                schema: "integrations",
                table: "webhook_endpoints");

            migrationBuilder.DropColumn(
                name: "SigningSecretMaskSuffix",
                schema: "integrations",
                table: "webhook_endpoints");

            migrationBuilder.DropColumn(
                name: "SigningSecretNonce",
                schema: "integrations",
                table: "webhook_endpoints");

            migrationBuilder.DropColumn(
                name: "SigningSecretRotatedAt",
                schema: "integrations",
                table: "webhook_endpoints");

            migrationBuilder.DropColumn(
                name: "SigningSecretTag",
                schema: "integrations",
                table: "webhook_endpoints");

            migrationBuilder.DropColumn(
                name: "OpenRouterApiKeyCiphertext",
                schema: "ai",
                table: "settings");

            migrationBuilder.DropColumn(
                name: "OpenRouterApiKeyFormatVersion",
                schema: "ai",
                table: "settings");

            migrationBuilder.DropColumn(
                name: "OpenRouterApiKeyKeyVersion",
                schema: "ai",
                table: "settings");

            migrationBuilder.DropColumn(
                name: "OpenRouterApiKeyMaskSuffix",
                schema: "ai",
                table: "settings");

            migrationBuilder.DropColumn(
                name: "OpenRouterApiKeyNonce",
                schema: "ai",
                table: "settings");

            migrationBuilder.DropColumn(
                name: "OpenRouterApiKeyRotatedAt",
                schema: "ai",
                table: "settings");

            migrationBuilder.DropColumn(
                name: "OpenRouterApiKeyTag",
                schema: "ai",
                table: "settings");

            migrationBuilder.DropColumn(
                name: "SmtpPasswordCiphertext",
                schema: "notifications",
                table: "email_settings");

            migrationBuilder.DropColumn(
                name: "SmtpPasswordFormatVersion",
                schema: "notifications",
                table: "email_settings");

            migrationBuilder.DropColumn(
                name: "SmtpPasswordKeyVersion",
                schema: "notifications",
                table: "email_settings");

            migrationBuilder.DropColumn(
                name: "SmtpPasswordMaskSuffix",
                schema: "notifications",
                table: "email_settings");

            migrationBuilder.DropColumn(
                name: "SmtpPasswordNonce",
                schema: "notifications",
                table: "email_settings");

            migrationBuilder.DropColumn(
                name: "SmtpPasswordRotatedAt",
                schema: "notifications",
                table: "email_settings");

            migrationBuilder.DropColumn(
                name: "SmtpPasswordTag",
                schema: "notifications",
                table: "email_settings");

            migrationBuilder.AlterColumn<string>(
                name: "Secret",
                schema: "integrations",
                table: "webhook_endpoints",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512,
                oldNullable: true);
        }
    }
}
