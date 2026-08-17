-- VibeChat tenant RLS catalog (SEC-RLS-RUNTIME / ADR-009).
-- Applied after EF migrations by DatabaseBootstrap (not at docker init — tables may not exist yet).
--
-- Session GUCs (SET LOCAL / set_config is_local preferred inside transactions):
--   app.tenant_id  — required for tenant-scoped reads/writes (fail closed when unset)
--   app.user_id    — bootstrap membership discovery before tenant is known
--   app.job_role   — 'outbox' | 'retention' for worker cross-tenant claim paths only
--
-- Column names match EF defaults ("TenantId").

CREATE SCHEMA IF NOT EXISTS app;

CREATE OR REPLACE FUNCTION app.current_tenant_id()
RETURNS uuid
LANGUAGE sql
STABLE
AS $$
  SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid;
$$;

CREATE OR REPLACE FUNCTION app.current_user_id()
RETURNS uuid
LANGUAGE sql
STABLE
AS $$
  SELECT NULLIF(current_setting('app.user_id', true), '')::uuid;
$$;

CREATE OR REPLACE FUNCTION app.current_job_role()
RETURNS text
LANGUAGE sql
STABLE
AS $$
  SELECT NULLIF(current_setting('app.job_role', true), '');
$$;

-- ---------------------------------------------------------------------------
-- ENABLE + FORCE on every tenant-aware business table
-- ---------------------------------------------------------------------------
ALTER TABLE IF EXISTS tenancy.workspaces ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS tenancy.workspaces FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS tenancy.workspace_members ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS tenancy.workspace_members FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS directory.spaces ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS directory.spaces FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS conversations.channels ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS conversations.channels FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS conversations.channel_members ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS conversations.channel_members FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.messages ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.messages FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.threads ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.threads FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS files.attachments ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS files.attachments FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.reactions ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.reactions FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.message_mentions ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.message_mentions FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.read_cursors ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.read_cursors FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.conversation_sequences ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.conversation_sequences FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.idempotency ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.idempotency FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.message_retention_settings ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.message_retention_settings FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.link_previews ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.link_previews FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.message_link_previews ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.message_link_previews FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.link_preview_settings ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.link_preview_settings FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS building_blocks.outbox_messages ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS building_blocks.outbox_messages FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS audit.audit_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS audit.audit_events FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS ai.usage_records ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS ai.usage_records FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS ai.settings ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS ai.settings FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS notifications.preferences ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS notifications.preferences FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS notifications.email_settings ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS notifications.email_settings FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS notifications.push_subscriptions ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS notifications.push_subscriptions FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS integrations.webhook_endpoints ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS integrations.webhook_endpoints FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS files.settings ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS files.settings FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS building_blocks.rate_limit_settings ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS building_blocks.rate_limit_settings FORCE ROW LEVEL SECURITY;

-- ---------------------------------------------------------------------------
-- Policies: USING + WITH CHECK (writes require tenant context)
-- ---------------------------------------------------------------------------

DROP POLICY IF EXISTS tenant_isolation_workspaces ON tenancy.workspaces;
CREATE POLICY tenant_isolation_workspaces ON tenancy.workspaces
    USING (
        "TenantId" = app.current_tenant_id()
        OR (
            app.current_tenant_id() IS NULL
            AND app.current_user_id() IS NOT NULL
            AND EXISTS (
                SELECT 1
                FROM tenancy.workspace_members m
                WHERE m."WorkspaceId" = workspaces."Id"
                  AND m."UserId" = app.current_user_id()
            )
        )
    )
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_workspace_members ON tenancy.workspace_members;
CREATE POLICY tenant_isolation_workspace_members ON tenancy.workspace_members
    USING (
        "TenantId" = app.current_tenant_id()
        OR (
            app.current_tenant_id() IS NULL
            AND "UserId" = app.current_user_id()
        )
    )
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_spaces ON directory.spaces;
CREATE POLICY tenant_isolation_spaces ON directory.spaces
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_channels ON conversations.channels;
CREATE POLICY tenant_isolation_channels ON conversations.channels
    USING (
        "TenantId" = app.current_tenant_id()
        OR (
            app.current_tenant_id() IS NULL
            AND app.current_user_id() IS NOT NULL
            AND EXISTS (
                SELECT 1
                FROM tenancy.workspace_members m
                WHERE m."WorkspaceId" = channels."WorkspaceId"
                  AND m."UserId" = app.current_user_id()
            )
        )
    )
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_channel_members ON conversations.channel_members;
CREATE POLICY tenant_isolation_channel_members ON conversations.channel_members
    USING (
        "TenantId" = app.current_tenant_id()
        OR (
            app.current_tenant_id() IS NULL
            AND "UserId" = app.current_user_id()
        )
    )
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_messages ON messaging.messages;
CREATE POLICY tenant_isolation_messages ON messaging.messages
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_threads ON messaging.threads;
CREATE POLICY tenant_isolation_threads ON messaging.threads
    USING (
        "TenantId" = app.current_tenant_id()
        OR (
            app.current_tenant_id() IS NULL
            AND app.current_user_id() IS NOT NULL
            AND EXISTS (
                SELECT 1
                FROM conversations.channels c
                JOIN tenancy.workspace_members m ON m."WorkspaceId" = c."WorkspaceId"
                WHERE c."Id" = threads."ChannelId"
                  AND m."UserId" = app.current_user_id()
            )
        )
    )
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_attachments ON files.attachments;
CREATE POLICY tenant_isolation_attachments ON files.attachments
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_reactions ON messaging.reactions;
CREATE POLICY tenant_isolation_reactions ON messaging.reactions
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

ALTER TABLE IF EXISTS messaging.pinned_messages ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.pinned_messages FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_isolation_pinned_messages ON messaging.pinned_messages;
CREATE POLICY tenant_isolation_pinned_messages ON messaging.pinned_messages
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

ALTER TABLE IF EXISTS messaging.saved_messages ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.saved_messages FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_isolation_saved_messages ON messaging.saved_messages;
CREATE POLICY tenant_isolation_saved_messages ON messaging.saved_messages
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_message_mentions ON messaging.message_mentions;
CREATE POLICY tenant_isolation_message_mentions ON messaging.message_mentions
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_read_cursors ON messaging.read_cursors;
CREATE POLICY tenant_isolation_read_cursors ON messaging.read_cursors
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_conversation_sequences ON messaging.conversation_sequences;
CREATE POLICY tenant_isolation_conversation_sequences ON messaging.conversation_sequences
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_idempotency ON messaging.idempotency;
CREATE POLICY tenant_isolation_idempotency ON messaging.idempotency
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_message_retention_settings ON messaging.message_retention_settings;
CREATE POLICY tenant_isolation_message_retention_settings ON messaging.message_retention_settings
    USING (
        "TenantId" = app.current_tenant_id()
        OR app.current_job_role() = 'retention'
    )
    WITH CHECK ("TenantId" = app.current_tenant_id());

ALTER TABLE IF EXISTS messaging.link_previews ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.link_previews FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.message_link_previews ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.message_link_previews FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.link_preview_settings ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.link_preview_settings FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_isolation_link_previews ON messaging.link_previews;
CREATE POLICY tenant_isolation_link_previews ON messaging.link_previews
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_message_link_previews ON messaging.message_link_previews;
CREATE POLICY tenant_isolation_message_link_previews ON messaging.message_link_previews
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_link_preview_settings ON messaging.link_preview_settings;
CREATE POLICY tenant_isolation_link_preview_settings ON messaging.link_preview_settings
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_outbox_messages ON building_blocks.outbox_messages;
CREATE POLICY tenant_isolation_outbox_messages ON building_blocks.outbox_messages
    USING (
        "TenantId" = app.current_tenant_id()
        OR app.current_job_role() = 'outbox'
    )
    WITH CHECK (
        "TenantId" = app.current_tenant_id()
        OR app.current_job_role() = 'outbox'
    );

DROP POLICY IF EXISTS tenant_isolation_audit_events ON audit.audit_events;
CREATE POLICY tenant_isolation_audit_events ON audit.audit_events
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_ai_usage_records ON ai.usage_records;
CREATE POLICY tenant_isolation_ai_usage_records ON ai.usage_records
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_ai_settings ON ai.settings;
CREATE POLICY tenant_isolation_ai_settings ON ai.settings
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_notification_preferences ON notifications.preferences;
CREATE POLICY tenant_isolation_notification_preferences ON notifications.preferences
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_email_settings ON notifications.email_settings;
CREATE POLICY tenant_isolation_email_settings ON notifications.email_settings
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_push_subscriptions ON notifications.push_subscriptions;
CREATE POLICY tenant_isolation_push_subscriptions ON notifications.push_subscriptions
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_webhook_endpoints ON integrations.webhook_endpoints;
CREATE POLICY tenant_isolation_webhook_endpoints ON integrations.webhook_endpoints
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_files_settings ON files.settings;
CREATE POLICY tenant_isolation_files_settings ON files.settings
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_rate_limit_settings ON building_blocks.rate_limit_settings;
CREATE POLICY tenant_isolation_rate_limit_settings ON building_blocks.rate_limit_settings
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

-- ---------------------------------------------------------------------------
-- Grants for runtime / backup (migrator owns objects via migration connection)
-- ---------------------------------------------------------------------------
DO $$
DECLARE
  app_role text := NULLIF(current_setting('vibechat.app_role', true), '');
  backup_role text := NULLIF(current_setting('vibechat.backup_role', true), '');
  sch text;
BEGIN
  IF app_role IS NULL THEN
    app_role := 'vibechat_app';
  END IF;
  IF backup_role IS NULL THEN
    backup_role := 'vibechat_backup';
  END IF;

  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = app_role) THEN
    FOREACH sch IN ARRAY ARRAY[
      'tenancy', 'directory', 'conversations', 'messaging', 'files',
      'building_blocks', 'audit', 'ai', 'notifications', 'integrations',
      'identity', 'administration', 'app', 'public'
    ]
    LOOP
      EXECUTE format('GRANT USAGE ON SCHEMA %I TO %I', sch, app_role);
      EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA %I TO %I', sch, app_role);
      EXECUTE format('GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA %I TO %I', sch, app_role);
      EXECUTE format('ALTER DEFAULT PRIVILEGES IN SCHEMA %I GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO %I', sch, app_role);
      EXECUTE format('ALTER DEFAULT PRIVILEGES IN SCHEMA %I GRANT USAGE, SELECT ON SEQUENCES TO %I', sch, app_role);
    END LOOP;
    GRANT EXECUTE ON FUNCTION app.current_tenant_id() TO CURRENT_USER;
    EXECUTE format('GRANT EXECUTE ON FUNCTION app.current_tenant_id() TO %I', app_role);
    EXECUTE format('GRANT EXECUTE ON FUNCTION app.current_user_id() TO %I', app_role);
    EXECUTE format('GRANT EXECUTE ON FUNCTION app.current_job_role() TO %I', app_role);
  END IF;

  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = backup_role) THEN
    FOREACH sch IN ARRAY ARRAY[
      'tenancy', 'directory', 'conversations', 'messaging', 'files',
      'building_blocks', 'audit', 'ai', 'notifications', 'integrations',
      'identity', 'administration', 'app', 'public'
    ]
    LOOP
      EXECUTE format('GRANT USAGE ON SCHEMA %I TO %I', sch, backup_role);
      EXECUTE format('GRANT SELECT ON ALL TABLES IN SCHEMA %I TO %I', sch, backup_role);
    END LOOP;
  END IF;
END
$$;
