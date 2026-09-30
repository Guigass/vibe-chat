using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VibeChat.Infrastructure;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// B-108: several webhook endpoints per tenant. Existing singleton rows keep
    /// their secret and subscribe only to MessageCreated.
    /// </summary>
    [DbContext(typeof(VibeChatDbContext))]
    [Migration("20260930140000_ExtendOutboundWebhooks")]
    public partial class ExtendOutboundWebhooks : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE integrations.webhook_endpoints ADD COLUMN "Id" uuid;
                UPDATE integrations.webhook_endpoints SET "Id" = gen_random_uuid() WHERE "Id" IS NULL;
                ALTER TABLE integrations.webhook_endpoints ALTER COLUMN "Id" SET NOT NULL;
                ALTER TABLE integrations.webhook_endpoints DROP CONSTRAINT "PK_webhook_endpoints";
                ALTER TABLE integrations.webhook_endpoints ADD CONSTRAINT "PK_webhook_endpoints" PRIMARY KEY ("Id");

                ALTER TABLE integrations.webhook_endpoints
                    ADD COLUMN "Name" character varying(80) NOT NULL DEFAULT 'default';
                ALTER TABLE integrations.webhook_endpoints
                    ADD COLUMN "SubscribedEvents" text[] NOT NULL DEFAULT ARRAY['MessageCreated']::text[];
                ALTER TABLE integrations.webhook_endpoints
                    ADD COLUMN "ChannelFilter" uuid[] NOT NULL DEFAULT ARRAY[]::uuid[];
                ALTER TABLE integrations.webhook_endpoints
                    ADD COLUMN "LastDeliveryAt" timestamp with time zone NULL;
                ALTER TABLE integrations.webhook_endpoints
                    ADD COLUMN "LastStatusCode" integer NULL;
                ALTER TABLE integrations.webhook_endpoints
                    ADD COLUMN "LastError" character varying(500) NULL;

                ALTER TABLE integrations.webhook_endpoints ALTER COLUMN "Name" DROP DEFAULT;
                ALTER TABLE integrations.webhook_endpoints ALTER COLUMN "SubscribedEvents" DROP DEFAULT;
                ALTER TABLE integrations.webhook_endpoints ALTER COLUMN "ChannelFilter" DROP DEFAULT;

                CREATE INDEX "IX_webhook_endpoints_TenantId" ON integrations.webhook_endpoints ("TenantId");
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                  IF EXISTS (
                    SELECT 1
                    FROM integrations.webhook_endpoints
                    GROUP BY "TenantId"
                    HAVING COUNT(*) > 1
                  ) THEN
                    RAISE EXCEPTION 'webhook_endpoints rollback requires at most one row per tenant';
                  END IF;
                END $$;

                DROP INDEX IF EXISTS integrations."IX_webhook_endpoints_TenantId";
                ALTER TABLE integrations.webhook_endpoints DROP COLUMN "LastError";
                ALTER TABLE integrations.webhook_endpoints DROP COLUMN "LastStatusCode";
                ALTER TABLE integrations.webhook_endpoints DROP COLUMN "LastDeliveryAt";
                ALTER TABLE integrations.webhook_endpoints DROP COLUMN "ChannelFilter";
                ALTER TABLE integrations.webhook_endpoints DROP COLUMN "SubscribedEvents";
                ALTER TABLE integrations.webhook_endpoints DROP COLUMN "Name";
                ALTER TABLE integrations.webhook_endpoints DROP CONSTRAINT "PK_webhook_endpoints";
                ALTER TABLE integrations.webhook_endpoints ADD CONSTRAINT "PK_webhook_endpoints" PRIMARY KEY ("TenantId");
                ALTER TABLE integrations.webhook_endpoints DROP COLUMN "Id";
                """);
        }
    }
}
