using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiBotCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bot_channel_scopes",
                schema: "ai",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    BotId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_bot_channel_scopes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "bot_definitions",
                schema: "ai",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    IntegrationBotId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrincipalUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ActiveVersionNumber = table.Column<int>(type: "integer", nullable: true),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "character varying(280)", maxLength: 280, nullable: false),
                    AvatarKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AllowDms = table.Column<bool>(type: "boolean", nullable: false),
                    SystemPrompt = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    SkillVersionIds = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    KnowledgeGrantIds = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    McpToolGrantIds = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    GuardrailPolicyVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MaxTurns = table.Column<int>(type: "integer", nullable: false),
                    MaxInputTokens = table.Column<int>(type: "integer", nullable: false),
                    MaxOutputTokens = table.Column<int>(type: "integer", nullable: false),
                    DailyBudgetUsd = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bot_definitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "bot_versions",
                schema: "ai",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    BotId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "character varying(280)", maxLength: 280, nullable: false),
                    AvatarKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AllowDms = table.Column<bool>(type: "boolean", nullable: false),
                    SystemPrompt = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    PromptSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SkillVersionIds = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    KnowledgeGrantIds = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    McpToolGrantIds = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    GuardrailPolicyVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MaxTurns = table.Column<int>(type: "integer", nullable: false),
                    MaxInputTokens = table.Column<int>(type: "integer", nullable: false),
                    MaxOutputTokens = table.Column<int>(type: "integer", nullable: false),
                    DailyBudgetUsd = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    PublishedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bot_versions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "model_bindings",
                schema: "ai",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    BotId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    Provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ModelId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ParametersJson = table.Column<string>(type: "jsonb", nullable: false),
                    MaxTokens = table.Column<int>(type: "integer", nullable: false),
                    TimeoutMs = table.Column<int>(type: "integer", nullable: false),
                    FallbackModelId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    DataClass = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_model_bindings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_bot_channel_scopes_TenantId_BotId_VersionNumber_ChannelId",
                schema: "ai",
                table: "bot_channel_scopes",
                columns: new[] { "TenantId", "BotId", "VersionNumber", "ChannelId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bot_definitions_TenantId_WorkspaceId",
                schema: "ai",
                table: "bot_definitions",
                columns: new[] { "TenantId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_bot_versions_TenantId_BotId_VersionNumber",
                schema: "ai",
                table: "bot_versions",
                columns: new[] { "TenantId", "BotId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_model_bindings_TenantId_BotId_VersionNumber",
                schema: "ai",
                table: "model_bindings",
                columns: new[] { "TenantId", "BotId", "VersionNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bot_channel_scopes",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "bot_definitions",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "bot_versions",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "model_bindings",
                schema: "ai");
        }
    }
}
