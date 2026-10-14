using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelTopics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Layout",
                schema: "conversations",
                table: "channels",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Stream");

            migrationBuilder.CreateTable(
                name: "channel_topics",
                schema: "conversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Tags = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    ParticipantIds = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastActivityAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClosedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    MovedFromChannelId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_channel_topics", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "topic_subscriptions",
                schema: "messaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TopicId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_topic_subscriptions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_channel_topics_TenantId_ChannelId_LastActivityAt",
                schema: "conversations",
                table: "channel_topics",
                columns: new[] { "TenantId", "ChannelId", "LastActivityAt" });

            migrationBuilder.CreateIndex(
                name: "IX_channel_topics_TenantId_ChannelId_Slug",
                schema: "conversations",
                table: "channel_topics",
                columns: new[] { "TenantId", "ChannelId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_topic_subscriptions_TenantId_UserId_TopicId",
                schema: "messaging",
                table: "topic_subscriptions",
                columns: new[] { "TenantId", "UserId", "TopicId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "channel_topics",
                schema: "conversations");

            migrationBuilder.DropTable(
                name: "topic_subscriptions",
                schema: "messaging");

            migrationBuilder.DropColumn(
                name: "Layout",
                schema: "conversations",
                table: "channels");
        }
    }
}
