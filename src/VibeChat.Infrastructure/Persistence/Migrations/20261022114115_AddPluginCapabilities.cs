using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPluginCapabilities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "CallbackSecretCiphertext",
                schema: "integrations",
                table: "plugins",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "CallbackSecretFormatVersion",
                schema: "integrations",
                table: "plugins",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CallbackSecretKeyVersion",
                schema: "integrations",
                table: "plugins",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CallbackSecretMaskSuffix",
                schema: "integrations",
                table: "plugins",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "CallbackSecretNonce",
                schema: "integrations",
                table: "plugins",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CallbackSecretRotatedAt",
                schema: "integrations",
                table: "plugins",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "CallbackSecretTag",
                schema: "integrations",
                table: "plugins",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CallbackUrl",
                schema: "integrations",
                table: "plugins",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "Hooks",
                schema: "integrations",
                table: "plugins",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<string[]>(
                name: "SubscribedEvents",
                schema: "integrations",
                table: "plugins",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CallbackSecretCiphertext",
                schema: "integrations",
                table: "plugins");

            migrationBuilder.DropColumn(
                name: "CallbackSecretFormatVersion",
                schema: "integrations",
                table: "plugins");

            migrationBuilder.DropColumn(
                name: "CallbackSecretKeyVersion",
                schema: "integrations",
                table: "plugins");

            migrationBuilder.DropColumn(
                name: "CallbackSecretMaskSuffix",
                schema: "integrations",
                table: "plugins");

            migrationBuilder.DropColumn(
                name: "CallbackSecretNonce",
                schema: "integrations",
                table: "plugins");

            migrationBuilder.DropColumn(
                name: "CallbackSecretRotatedAt",
                schema: "integrations",
                table: "plugins");

            migrationBuilder.DropColumn(
                name: "CallbackSecretTag",
                schema: "integrations",
                table: "plugins");

            migrationBuilder.DropColumn(
                name: "CallbackUrl",
                schema: "integrations",
                table: "plugins");

            migrationBuilder.DropColumn(
                name: "Hooks",
                schema: "integrations",
                table: "plugins");

            migrationBuilder.DropColumn(
                name: "SubscribedEvents",
                schema: "integrations",
                table: "plugins");
        }
    }
}
