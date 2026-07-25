-- VibeChat tenant RLS policies.
-- The application should set `app.tenant_id` per request/transaction before querying sensitive tables.
-- Example: SELECT set_config('app.tenant_id', '<tenant-guid>', true);
--
-- Column names match EF defaults (`"TenantId"`). Apply after migrations create the tables
-- (init-time ALTER TABLE IF EXISTS is a no-op when schemas are empty).

ALTER TABLE IF EXISTS tenancy.workspaces ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS tenancy.workspace_members ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS directory.spaces ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS conversations.channels ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS conversations.channel_members ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.messages ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.threads ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS files.attachments ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.reactions ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.read_cursors ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.idempotency ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.message_retention_settings ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS audit.audit_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS ai.usage_records ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS notifications.preferences ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS integrations.webhook_endpoints ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_isolation_workspaces ON tenancy.workspaces;
CREATE POLICY tenant_isolation_workspaces ON tenancy.workspaces
    USING ("TenantId" = current_setting('app.tenant_id', true)::uuid);

DROP POLICY IF EXISTS tenant_isolation_workspace_members ON tenancy.workspace_members;
CREATE POLICY tenant_isolation_workspace_members ON tenancy.workspace_members
    USING ("TenantId" = current_setting('app.tenant_id', true)::uuid);

DROP POLICY IF EXISTS tenant_isolation_spaces ON directory.spaces;
CREATE POLICY tenant_isolation_spaces ON directory.spaces
    USING ("TenantId" = current_setting('app.tenant_id', true)::uuid);

DROP POLICY IF EXISTS tenant_isolation_channels ON conversations.channels;
CREATE POLICY tenant_isolation_channels ON conversations.channels
    USING ("TenantId" = current_setting('app.tenant_id', true)::uuid);

DROP POLICY IF EXISTS tenant_isolation_channel_members ON conversations.channel_members;
CREATE POLICY tenant_isolation_channel_members ON conversations.channel_members
    USING ("TenantId" = current_setting('app.tenant_id', true)::uuid);

DROP POLICY IF EXISTS tenant_isolation_messages ON messaging.messages;
CREATE POLICY tenant_isolation_messages ON messaging.messages
    USING ("TenantId" = current_setting('app.tenant_id', true)::uuid);

DROP POLICY IF EXISTS tenant_isolation_threads ON messaging.threads;
CREATE POLICY tenant_isolation_threads ON messaging.threads
    USING ("TenantId" = current_setting('app.tenant_id', true)::uuid);

DROP POLICY IF EXISTS tenant_isolation_attachments ON files.attachments;
CREATE POLICY tenant_isolation_attachments ON files.attachments
    USING ("TenantId" = current_setting('app.tenant_id', true)::uuid);

DROP POLICY IF EXISTS tenant_isolation_reactions ON messaging.reactions;
CREATE POLICY tenant_isolation_reactions ON messaging.reactions
    USING ("TenantId" = current_setting('app.tenant_id', true)::uuid);

DROP POLICY IF EXISTS tenant_isolation_read_cursors ON messaging.read_cursors;
CREATE POLICY tenant_isolation_read_cursors ON messaging.read_cursors
    USING ("TenantId" = current_setting('app.tenant_id', true)::uuid);

DROP POLICY IF EXISTS tenant_isolation_idempotency ON messaging.idempotency;
CREATE POLICY tenant_isolation_idempotency ON messaging.idempotency
    USING ("TenantId" = current_setting('app.tenant_id', true)::uuid);

DROP POLICY IF EXISTS tenant_isolation_message_retention_settings ON messaging.message_retention_settings;
CREATE POLICY tenant_isolation_message_retention_settings ON messaging.message_retention_settings
    USING ("TenantId" = current_setting('app.tenant_id', true)::uuid);

DROP POLICY IF EXISTS tenant_isolation_audit_events ON audit.audit_events;
CREATE POLICY tenant_isolation_audit_events ON audit.audit_events
    USING ("TenantId" = current_setting('app.tenant_id', true)::uuid);

DROP POLICY IF EXISTS tenant_isolation_ai_usage_records ON ai.usage_records;
CREATE POLICY tenant_isolation_ai_usage_records ON ai.usage_records
    USING ("TenantId" = current_setting('app.tenant_id', true)::uuid);

DROP POLICY IF EXISTS tenant_isolation_notification_preferences ON notifications.preferences;
CREATE POLICY tenant_isolation_notification_preferences ON notifications.preferences
    USING ("TenantId" = current_setting('app.tenant_id', true)::uuid);

DROP POLICY IF EXISTS tenant_isolation_webhook_endpoints ON integrations.webhook_endpoints;
CREATE POLICY tenant_isolation_webhook_endpoints ON integrations.webhook_endpoints
    USING ("TenantId" = current_setting('app.tenant_id', true)::uuid);
