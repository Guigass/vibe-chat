using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
/// <inheritdoc />
public partial class AddMessageSearchVector : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE messaging.messages
                ADD COLUMN IF NOT EXISTS search_vector tsvector;

            UPDATE messaging.messages
            SET search_vector = CASE
                WHEN "DeletedAt" IS NOT NULL THEN NULL
                ELSE to_tsvector('portuguese', coalesce("Body", ''))
            END;

            CREATE INDEX IF NOT EXISTS ix_messages_search_vector
                ON messaging.messages
                USING GIN (search_vector);

            CREATE OR REPLACE FUNCTION messaging.messages_search_vector_update()
            RETURNS trigger AS $$
            BEGIN
                IF NEW."DeletedAt" IS NOT NULL THEN
                    NEW.search_vector := NULL;
                ELSE
                    NEW.search_vector := to_tsvector('portuguese', coalesce(NEW."Body", ''));
                END IF;
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;

            DROP TRIGGER IF EXISTS trg_messages_search_vector ON messaging.messages;
            CREATE TRIGGER trg_messages_search_vector
                BEFORE INSERT OR UPDATE OF "Body", "DeletedAt"
                ON messaging.messages
                FOR EACH ROW
                EXECUTE FUNCTION messaging.messages_search_vector_update();
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS trg_messages_search_vector ON messaging.messages;
            DROP FUNCTION IF EXISTS messaging.messages_search_vector_update();
            DROP INDEX IF EXISTS messaging.ix_messages_search_vector;
            ALTER TABLE messaging.messages DROP COLUMN IF EXISTS search_vector;
            """);
    }
}
}
