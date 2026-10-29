using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLiveTranscripts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "TranscriptionEnabled",
                schema: "conversations",
                table: "live_policies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "live_note_suggestions",
                schema: "conversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TranscriptId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Text = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    StartMs = table.Column<int>(type: "integer", nullable: false),
                    EndMs = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_live_note_suggestions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "live_transcript_consents",
                schema: "conversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TranscriptId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Decision = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_live_transcript_consents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "live_transcript_segments",
                schema: "conversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TranscriptId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    StartMs = table.Column<int>(type: "integer", nullable: false),
                    EndMs = table.Column<int>(type: "integer", nullable: false),
                    SpeakerLabel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_live_transcript_segments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "live_transcripts",
                schema: "conversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordingId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    FailureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Provider = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ProviderRef = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Confidence = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StartedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OnHold = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    QueuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_live_transcripts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_live_note_suggestions_TenantId_TranscriptId",
                schema: "conversations",
                table: "live_note_suggestions",
                columns: new[] { "TenantId", "TranscriptId" });

            migrationBuilder.CreateIndex(
                name: "IX_live_transcript_consents_TenantId_TranscriptId_UserId",
                schema: "conversations",
                table: "live_transcript_consents",
                columns: new[] { "TenantId", "TranscriptId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_live_transcript_segments_TenantId_TranscriptId_Ordinal",
                schema: "conversations",
                table: "live_transcript_segments",
                columns: new[] { "TenantId", "TranscriptId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_live_transcripts_TenantId_SessionId_IdempotencyKey",
                schema: "conversations",
                table: "live_transcripts",
                columns: new[] { "TenantId", "SessionId", "IdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "live_note_suggestions",
                schema: "conversations");

            migrationBuilder.DropTable(
                name: "live_transcript_consents",
                schema: "conversations");

            migrationBuilder.DropTable(
                name: "live_transcript_segments",
                schema: "conversations");

            migrationBuilder.DropTable(
                name: "live_transcripts",
                schema: "conversations");

            migrationBuilder.DropColumn(
                name: "TranscriptionEnabled",
                schema: "conversations",
                table: "live_policies");
        }
    }
}
