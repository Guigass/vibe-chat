-- VibeChat tenant RLS policies.
-- The application should set `app.tenant_id` per request/transaction before querying sensitive tables.
-- Example: SELECT set_config('app.tenant_id', '<tenant-guid>', true);

ALTER TABLE IF EXISTS tenancy.workspaces ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS tenancy.workspace_members ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS conversations.channels ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS conversations.channel_members ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.messages ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.threads ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS files.attachments ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.reactions ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.read_cursors ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.idempotency ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS audit.audit_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS ai.usage_records ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS notifications.preferences ENABLE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation_workspaces ON tenancy.workspaces
    USING (tenant_id = current_setting('app.tenant_id', true)::uuid);

CREATE POLICY tenant_isolation_workspace_members ON tenancy.workspace_members
    USING (tenant_id = current_setting('app.tenant_id', true)::uuid);

CREATE POLICY tenant_isolation_channels ON conversations.channels
    USING (tenant_id = current_setting('app.tenant_id', true)::uuid);

CREATE POLICY tenant_isolation_channel_members ON conversations.channel_members
    USING (tenant_id = current_setting('app.tenant_id', true)::uuid);

CREATE POLICY tenant_isolation_messages ON messaging.messages
    USING (tenant_id = current_setting('app.tenant_id', true)::uuid);

CREATE POLICY tenant_isolation_threads ON messaging.threads
    USING (tenant_id = current_setting('app.tenant_id', true)::uuid);

CREATE POLICY tenant_isolation_attachments ON files.attachments
    USING (tenant_id = current_setting('app.tenant_id', true)::uuid);

CREATE POLICY tenant_isolation_reactions ON messaging.reactions
    USING (tenant_id = current_setting('app.tenant_id', true)::uuid);

CREATE POLICY tenant_isolation_read_cursors ON messaging.read_cursors
    USING (tenant_id = current_setting('app.tenant_id', true)::uuid);

CREATE POLICY tenant_isolation_idempotency ON messaging.idempotency
    USING (tenant_id = current_setting('app.tenant_id', true)::uuid);

CREATE POLICY tenant_isolation_audit_events ON audit.audit_events
    USING (tenant_id = current_setting('app.tenant_id', true)::uuid);

CREATE POLICY tenant_isolation_ai_usage_records ON ai.usage_records
    USING (tenant_id = current_setting('app.tenant_id', true)::uuid);

CREATE POLICY tenant_isolation_notification_preferences ON notifications.preferences
    USING (tenant_id = current_setting('app.tenant_id', true)::uuid);
