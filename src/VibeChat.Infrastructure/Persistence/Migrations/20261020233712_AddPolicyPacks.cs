using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPolicyPacks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "policy_assignments",
                schema: "compliance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    PackId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PackVersion = table.Column<int>(type: "integer", nullable: false),
                    OverridesJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    EffectiveJson = table.Column<string>(type: "character varying(12000)", maxLength: 12000, nullable: false),
                    PreviousPackId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PreviousPackVersion = table.Column<int>(type: "integer", nullable: true),
                    PreviousOverridesJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    PreviousEffectiveJson = table.Column<string>(type: "character varying(12000)", maxLength: 12000, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AppliedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    AppliedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_policy_assignments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_policy_assignments_TenantId_WorkspaceId",
                schema: "compliance",
                table: "policy_assignments",
                columns: new[] { "TenantId", "WorkspaceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "policy_assignments",
                schema: "compliance");
        }
    }
}
