using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageThreads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "threads",
                schema: "messaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentMessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_threads", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_messages_ThreadId",
                schema: "messaging",
                table: "messages",
                column: "ThreadId");

            migrationBuilder.CreateIndex(
                name: "IX_threads_TenantId_ChannelId",
                schema: "messaging",
                table: "threads",
                columns: new[] { "TenantId", "ChannelId" });

            migrationBuilder.CreateIndex(
                name: "IX_threads_TenantId_ParentMessageId",
                schema: "messaging",
                table: "threads",
                columns: new[] { "TenantId", "ParentMessageId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "threads",
                schema: "messaging");

            migrationBuilder.DropIndex(
                name: "IX_messages_ThreadId",
                schema: "messaging",
                table: "messages");
        }
    }
}
