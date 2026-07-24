using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.EnsureSchema(
                name: "conversations");

            migrationBuilder.EnsureSchema(
                name: "messaging");

            migrationBuilder.EnsureSchema(
                name: "building_blocks");

            migrationBuilder.EnsureSchema(
                name: "notifications");

            migrationBuilder.EnsureSchema(
                name: "ai");

            migrationBuilder.EnsureSchema(
                name: "directory");

            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.EnsureSchema(
                name: "tenancy");

            migrationBuilder.CreateTable(
                name: "audit_events",
                schema: "audit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<string>(type: "text", nullable: true),
                    MetadataJson = table.Column<string>(type: "text", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "channel_members",
                schema: "conversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_channel_members", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "channels",
                schema: "conversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_channels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "conversation_sequences",
                schema: "messaging",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastSequence = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversation_sequences", x => new { x.TenantId, x.ConversationId });
                });

            migrationBuilder.CreateTable(
                name: "idempotency",
                schema: "messaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    ResultJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                schema: "messaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    AuthorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Body = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    ReplyToMessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    ThreadId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EditedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "building_blocks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "preferences",
                schema: "notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmailEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    PushEnabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_preferences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "reactions",
                schema: "messaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Emoji = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "read_cursors",
                schema: "messaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastReadSequence = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_read_cursors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "settings",
                schema: "ai",
                columns: table => new
                {
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Provider = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_settings", x => x.WorkspaceId);
                });

            migrationBuilder.CreateTable(
                name: "spaces",
                schema: "directory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_spaces", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "usage_records",
                schema: "ai",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    PromptTokens = table.Column<int>(type: "integer", nullable: false),
                    CompletionTokens = table.Column<int>(type: "integer", nullable: false),
                    CostUsd = table.Column<decimal>(type: "numeric", nullable: false),
                    LatencyMs = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_usage_records", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "user_profiles",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_profiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "workspace_members",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workspace_members", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "workspaces",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Slug = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    AiEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workspaces", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_channel_members_ChannelId_UserId",
                schema: "conversations",
                table: "channel_members",
                columns: new[] { "ChannelId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_channels_WorkspaceId_Name",
                schema: "conversations",
                table: "channels",
                columns: new[] { "WorkspaceId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_idempotency_TenantId_Key",
                schema: "messaging",
                table: "idempotency",
                columns: new[] { "TenantId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_messages_ConversationId_Sequence",
                schema: "messaging",
                table: "messages",
                columns: new[] { "ConversationId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_ProcessedAt_OccurredAt",
                schema: "building_blocks",
                table: "outbox_messages",
                columns: new[] { "ProcessedAt", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_read_cursors_ChannelId_UserId",
                schema: "messaging",
                table: "read_cursors",
                columns: new[] { "ChannelId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_profiles_Email",
                schema: "identity",
                table: "user_profiles",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_user_profiles_Subject",
                schema: "identity",
                table: "user_profiles",
                column: "Subject",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workspace_members_TenantId_UserId",
                schema: "tenancy",
                table: "workspace_members",
                columns: new[] { "TenantId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_workspace_members_WorkspaceId_UserId",
                schema: "tenancy",
                table: "workspace_members",
                columns: new[] { "WorkspaceId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workspaces_Slug",
                schema: "tenancy",
                table: "workspaces",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_events",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "channel_members",
                schema: "conversations");

            migrationBuilder.DropTable(
                name: "channels",
                schema: "conversations");

            migrationBuilder.DropTable(
                name: "conversation_sequences",
                schema: "messaging");

            migrationBuilder.DropTable(
                name: "idempotency",
                schema: "messaging");

            migrationBuilder.DropTable(
                name: "messages",
                schema: "messaging");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "building_blocks");

            migrationBuilder.DropTable(
                name: "preferences",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "reactions",
                schema: "messaging");

            migrationBuilder.DropTable(
                name: "read_cursors",
                schema: "messaging");

            migrationBuilder.DropTable(
                name: "settings",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "spaces",
                schema: "directory");

            migrationBuilder.DropTable(
                name: "usage_records",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "user_profiles",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "workspace_members",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "workspaces",
                schema: "tenancy");
        }
    }
}
