-- VibeChat tenant RLS catalog (SEC-RLS-RUNTIME / ADR-009).
-- Applied after EF migrations by DatabaseBootstrap (not at docker init — tables may not exist yet).
--
-- Session GUCs (SET LOCAL / set_config is_local preferred inside transactions):
--   app.tenant_id  — required for tenant-scoped reads/writes (fail closed when unset)
--   app.user_id    — bootstrap membership discovery before tenant is known
--   app.job_role   — 'outbox' | 'retention' | 'polls' | 'schedule' | 'digest' | 'tasks' | 'automation' | 'connector' | 'siem' for worker cross-tenant claim paths only
--   app.invite_token_hash — B-040 accept lookup only; reveals the single matching invite row
--   app.integration_token_hash — B-109 token lookup only; reveals the single matching bot token row
--   app.scim_token_hash — B-128 token lookup only; reveals the single matching SCIM token row
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

CREATE OR REPLACE FUNCTION app.current_invite_token_hash()
RETURNS text
LANGUAGE sql
STABLE
AS $$
  SELECT NULLIF(current_setting('app.invite_token_hash', true), '');
$$;

CREATE OR REPLACE FUNCTION app.current_integration_token_hash()
RETURNS text
LANGUAGE sql
STABLE
AS $$
  SELECT NULLIF(current_setting('app.integration_token_hash', true), '');
$$;

CREATE OR REPLACE FUNCTION app.current_scim_token_hash()
RETURNS text
LANGUAGE sql
STABLE
AS $$
  SELECT NULLIF(current_setting('app.scim_token_hash', true), '');
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
ALTER TABLE IF EXISTS conversations.channel_topics ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS conversations.channel_topics FORCE ROW LEVEL SECURITY;
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
ALTER TABLE IF EXISTS messaging.message_lifecycle_policies ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.message_lifecycle_policies FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.message_versions ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.message_versions FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.message_moves ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.message_moves FORCE ROW LEVEL SECURITY;
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
ALTER TABLE IF EXISTS audit.content_audit_settings ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS audit.content_audit_settings FORCE ROW LEVEL SECURITY;
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
ALTER TABLE IF EXISTS notifications.channel_preferences ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS notifications.channel_preferences FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS integrations.webhook_endpoints ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS integrations.webhook_endpoints FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS files.settings ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS files.settings FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS building_blocks.rate_limit_settings ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS building_blocks.rate_limit_settings FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS identity.user_statuses ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS identity.user_statuses FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS identity.member_profiles ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS identity.member_profiles FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS import.jobs ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS import.jobs FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS import.id_map ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS import.id_map FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS import.historical_principals ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS import.historical_principals FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS support.bundles ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS support.bundles FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS support.repair_jobs ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS support.repair_jobs FORCE ROW LEVEL SECURITY;

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
        OR (
            app.current_tenant_id() IS NULL
            AND app.current_user_id() IS NOT NULL
            AND EXISTS (
                SELECT 1
                FROM conversations.channel_members cm
                JOIN conversations.channels c ON c."Id" = cm."ChannelId"
                WHERE c."WorkspaceId" = workspaces."Id"
                  AND cm."UserId" = app.current_user_id()
                  AND cm."LeftAt" IS NULL
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
        OR (
            app.current_tenant_id() IS NULL
            AND app.current_user_id() IS NOT NULL
            AND EXISTS (
                SELECT 1
                FROM conversations.channel_members cm
                WHERE cm."ChannelId" = channels."Id"
                  AND cm."UserId" = app.current_user_id()
                  AND cm."LeftAt" IS NULL
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

ALTER TABLE IF EXISTS directory.contact_groups ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS directory.contact_groups FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS directory.contact_group_members ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS directory.contact_group_members FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_isolation_contact_groups ON directory.contact_groups;
CREATE POLICY tenant_isolation_contact_groups ON directory.contact_groups
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_contact_group_members ON directory.contact_group_members;
CREATE POLICY tenant_isolation_contact_group_members ON directory.contact_group_members
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

ALTER TABLE IF EXISTS directory.channel_invites ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS directory.channel_invites FORCE ROW LEVEL SECURITY;

ALTER TABLE IF EXISTS directory.workspace_templates ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS directory.workspace_templates FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS directory.workspace_onboarding ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS directory.workspace_onboarding FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS directory.template_applications ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS directory.template_applications FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_isolation_workspace_templates ON directory.workspace_templates;
CREATE POLICY tenant_isolation_workspace_templates ON directory.workspace_templates
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_workspace_onboarding ON directory.workspace_onboarding;
CREATE POLICY tenant_isolation_workspace_onboarding ON directory.workspace_onboarding
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_template_applications ON directory.template_applications;
CREATE POLICY tenant_isolation_template_applications ON directory.template_applications
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_channel_invites ON directory.channel_invites;
CREATE POLICY tenant_isolation_channel_invites ON directory.channel_invites
    USING (
        "TenantId" = app.current_tenant_id()
        OR (
            app.current_invite_token_hash() IS NOT NULL
            AND "TokenHash" = app.current_invite_token_hash()
        )
    )
    WITH CHECK ("TenantId" = app.current_tenant_id());

ALTER TABLE IF EXISTS messaging.saved_messages ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.saved_messages FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.inbox_snoozes ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.inbox_snoozes FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.thread_subscriptions ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.thread_subscriptions FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.topic_subscriptions ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.topic_subscriptions FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.polls ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.polls FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.poll_options ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.poll_options FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.poll_votes ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.poll_votes FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.announcements ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.announcements FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.announcement_acknowledgements ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.announcement_acknowledgements FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.commitments ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.commitments FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.commitment_changes ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.commitment_changes FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_isolation_commitments ON messaging.commitments;
CREATE POLICY tenant_isolation_commitments ON messaging.commitments
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_commitment_changes ON messaging.commitment_changes;
CREATE POLICY tenant_isolation_commitment_changes ON messaging.commitment_changes
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

ALTER TABLE IF EXISTS messaging.tasks ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.tasks FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.task_assignees ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.task_assignees FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.task_checklist_items ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.task_checklist_items FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_isolation_tasks ON messaging.tasks;
CREATE POLICY tenant_isolation_tasks ON messaging.tasks
    USING (
        "TenantId" = app.current_tenant_id()
        OR app.current_job_role() = 'tasks'
    )
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_task_assignees ON messaging.task_assignees;
CREATE POLICY tenant_isolation_task_assignees ON messaging.task_assignees
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_task_checklist_items ON messaging.task_checklist_items;
CREATE POLICY tenant_isolation_task_checklist_items ON messaging.task_checklist_items
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

ALTER TABLE IF EXISTS messaging.forms ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.forms FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.form_versions ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.form_versions FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.form_submissions ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.form_submissions FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.form_decisions ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.form_decisions FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_isolation_forms ON messaging.forms;
CREATE POLICY tenant_isolation_forms ON messaging.forms
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_form_versions ON messaging.form_versions;
CREATE POLICY tenant_isolation_form_versions ON messaging.form_versions
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_form_submissions ON messaging.form_submissions;
CREATE POLICY tenant_isolation_form_submissions ON messaging.form_submissions
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_form_decisions ON messaging.form_decisions;
CREATE POLICY tenant_isolation_form_decisions ON messaging.form_decisions
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

CREATE SCHEMA IF NOT EXISTS automation;

ALTER TABLE IF EXISTS automation.definitions ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS automation.definitions FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS automation.versions ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS automation.versions FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS automation.runs ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS automation.runs FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS automation.steps ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS automation.steps FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_isolation_automation_definitions ON automation.definitions;
CREATE POLICY tenant_isolation_automation_definitions ON automation.definitions
    USING (
        "TenantId" = app.current_tenant_id()
        OR app.current_job_role() = 'automation'
    )
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_automation_versions ON automation.versions;
CREATE POLICY tenant_isolation_automation_versions ON automation.versions
    USING (
        "TenantId" = app.current_tenant_id()
        OR app.current_job_role() = 'automation'
    )
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_automation_runs ON automation.runs;
CREATE POLICY tenant_isolation_automation_runs ON automation.runs
    USING (
        "TenantId" = app.current_tenant_id()
        OR app.current_job_role() = 'automation'
    )
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_automation_steps ON automation.steps;
CREATE POLICY tenant_isolation_automation_steps ON automation.steps
    USING (
        "TenantId" = app.current_tenant_id()
        OR app.current_job_role() = 'automation'
    )
    WITH CHECK ("TenantId" = app.current_tenant_id());

CREATE SCHEMA IF NOT EXISTS incident;

ALTER TABLE IF EXISTS incident.rooms ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS incident.rooms FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS incident.responders ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS incident.responders FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS incident.steps ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS incident.steps FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS incident.timeline ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS incident.timeline FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_isolation_incident_rooms ON incident.rooms;
CREATE POLICY tenant_isolation_incident_rooms ON incident.rooms
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_incident_responders ON incident.responders;
CREATE POLICY tenant_isolation_incident_responders ON incident.responders
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_incident_steps ON incident.steps;
CREATE POLICY tenant_isolation_incident_steps ON incident.steps
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_incident_timeline ON incident.timeline;
CREATE POLICY tenant_isolation_incident_timeline ON incident.timeline
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

ALTER TABLE IF EXISTS connectors.installations ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS connectors.installations FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS connectors.effects ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS connectors.effects FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS connectors.deliveries ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS connectors.deliveries FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS connectors.checkpoints ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS connectors.checkpoints FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_isolation_connector_installations ON connectors.installations;
CREATE POLICY tenant_isolation_connector_installations ON connectors.installations
    USING (
        "TenantId" = app.current_tenant_id()
        OR app.current_job_role() = 'connector'
    )
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_connector_effects ON connectors.effects;
CREATE POLICY tenant_isolation_connector_effects ON connectors.effects
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_connector_deliveries ON connectors.deliveries;
CREATE POLICY tenant_isolation_connector_deliveries ON connectors.deliveries
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_connector_checkpoints ON connectors.checkpoints;
CREATE POLICY tenant_isolation_connector_checkpoints ON connectors.checkpoints
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

ALTER TABLE IF EXISTS knowledge.collections ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS knowledge.collections FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS knowledge.pages ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS knowledge.pages FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS knowledge.page_versions ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS knowledge.page_versions FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_isolation_knowledge_collections ON knowledge.collections;
CREATE POLICY tenant_isolation_knowledge_collections ON knowledge.collections
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_knowledge_pages ON knowledge.pages;
CREATE POLICY tenant_isolation_knowledge_pages ON knowledge.pages
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_knowledge_page_versions ON knowledge.page_versions;
CREATE POLICY tenant_isolation_knowledge_page_versions ON knowledge.page_versions
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

ALTER TABLE IF EXISTS search.semantic_chunks ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS search.semantic_chunks FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS search.semantic_settings ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS search.semantic_settings FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS search.semantic_usage ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS search.semantic_usage FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_isolation_semantic_chunks ON search.semantic_chunks;
CREATE POLICY tenant_isolation_semantic_chunks ON search.semantic_chunks
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_semantic_settings ON search.semantic_settings;
CREATE POLICY tenant_isolation_semantic_settings ON search.semantic_settings
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_semantic_usage ON search.semantic_usage;
CREATE POLICY tenant_isolation_semantic_usage ON search.semantic_usage
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_saved_messages ON messaging.saved_messages;
CREATE POLICY tenant_isolation_saved_messages ON messaging.saved_messages
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_inbox_snoozes ON messaging.inbox_snoozes;
CREATE POLICY tenant_isolation_inbox_snoozes ON messaging.inbox_snoozes
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_thread_subscriptions ON messaging.thread_subscriptions;
CREATE POLICY tenant_isolation_thread_subscriptions ON messaging.thread_subscriptions
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_channel_topics ON conversations.channel_topics;
CREATE POLICY tenant_isolation_channel_topics ON conversations.channel_topics
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_topic_subscriptions ON messaging.topic_subscriptions;
CREATE POLICY tenant_isolation_topic_subscriptions ON messaging.topic_subscriptions
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_polls ON messaging.polls;
CREATE POLICY tenant_isolation_polls ON messaging.polls
    USING (
        "TenantId" = app.current_tenant_id()
        OR app.current_job_role() = 'polls'
    )
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_poll_options ON messaging.poll_options;
CREATE POLICY tenant_isolation_poll_options ON messaging.poll_options
    USING (
        "TenantId" = app.current_tenant_id()
        OR app.current_job_role() = 'polls'
    )
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_poll_votes ON messaging.poll_votes;
CREATE POLICY tenant_isolation_poll_votes ON messaging.poll_votes
    USING (
        "TenantId" = app.current_tenant_id()
        OR app.current_job_role() = 'polls'
    )
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_announcements ON messaging.announcements;
CREATE POLICY tenant_isolation_announcements ON messaging.announcements
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_announcement_acknowledgements ON messaging.announcement_acknowledgements;
CREATE POLICY tenant_isolation_announcement_acknowledgements ON messaging.announcement_acknowledgements
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

ALTER TABLE IF EXISTS messaging.scheduled_messages ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.scheduled_messages FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.reminders ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS messaging.reminders FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_isolation_scheduled_messages ON messaging.scheduled_messages;
CREATE POLICY tenant_isolation_scheduled_messages ON messaging.scheduled_messages
    USING (
        "TenantId" = app.current_tenant_id()
        OR app.current_job_role() = 'schedule'
    )
    WITH CHECK (
        "TenantId" = app.current_tenant_id()
        OR app.current_job_role() = 'schedule'
    );

DROP POLICY IF EXISTS tenant_isolation_reminders ON messaging.reminders;
CREATE POLICY tenant_isolation_reminders ON messaging.reminders
    USING (
        "TenantId" = app.current_tenant_id()
        OR app.current_job_role() = 'schedule'
    )
    WITH CHECK (
        "TenantId" = app.current_tenant_id()
        OR app.current_job_role() = 'schedule'
    );

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

DROP POLICY IF EXISTS tenant_isolation_message_versions ON messaging.message_versions;
CREATE POLICY tenant_isolation_message_versions ON messaging.message_versions
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_message_moves ON messaging.message_moves;
CREATE POLICY tenant_isolation_message_moves ON messaging.message_moves
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_message_lifecycle_policies ON messaging.message_lifecycle_policies;
CREATE POLICY tenant_isolation_message_lifecycle_policies ON messaging.message_lifecycle_policies
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

ALTER TABLE IF EXISTS audit.content_audit_settings ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS audit.content_audit_settings FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation_content_audit_settings ON audit.content_audit_settings;
CREATE POLICY tenant_isolation_content_audit_settings ON audit.content_audit_settings
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

DROP POLICY IF EXISTS tenant_isolation_channel_preferences ON notifications.channel_preferences;
CREATE POLICY tenant_isolation_channel_preferences ON notifications.channel_preferences
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

ALTER TABLE IF EXISTS notifications.digest_schedules ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS notifications.digest_schedules FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS notifications.digest_runs ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS notifications.digest_runs FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS notifications.digest_items ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS notifications.digest_items FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_isolation_digest_schedules ON notifications.digest_schedules;
CREATE POLICY tenant_isolation_digest_schedules ON notifications.digest_schedules
    USING (
        "TenantId" = app.current_tenant_id()
        OR app.current_job_role() = 'digest'
    )
    WITH CHECK (
        "TenantId" = app.current_tenant_id()
        OR app.current_job_role() = 'digest'
    );

DROP POLICY IF EXISTS tenant_isolation_digest_runs ON notifications.digest_runs;
CREATE POLICY tenant_isolation_digest_runs ON notifications.digest_runs
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_digest_items ON notifications.digest_items;
CREATE POLICY tenant_isolation_digest_items ON notifications.digest_items
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_webhook_endpoints ON integrations.webhook_endpoints;
CREATE POLICY tenant_isolation_webhook_endpoints ON integrations.webhook_endpoints
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

ALTER TABLE IF EXISTS integrations.bots ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS integrations.bots FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS integrations.bot_tokens ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS integrations.bot_tokens FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS integrations.bot_channel_scopes ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS integrations.bot_channel_scopes FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_isolation_bots ON integrations.bots;
CREATE POLICY tenant_isolation_bots ON integrations.bots
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_bot_tokens ON integrations.bot_tokens;
CREATE POLICY tenant_isolation_bot_tokens ON integrations.bot_tokens
    USING (
        "TenantId" = app.current_tenant_id()
        OR (
            app.current_integration_token_hash() IS NOT NULL
            AND "TokenHash" = app.current_integration_token_hash()
        )
    )
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_bot_channel_scopes ON integrations.bot_channel_scopes;
CREATE POLICY tenant_isolation_bot_channel_scopes ON integrations.bot_channel_scopes
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

ALTER TABLE IF EXISTS identity.visual_preferences ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS identity.visual_preferences FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS integrations.plugins ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS integrations.plugins FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS integrations.plugin_history_grants ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS integrations.plugin_history_grants FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS integrations.plugin_interactive_offers ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS integrations.plugin_interactive_offers FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS integrations.plugin_action_receipts ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS integrations.plugin_action_receipts FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS integrations.plugin_quota_counters ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS integrations.plugin_quota_counters FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS integrations.plugin_registry_installs ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS integrations.plugin_registry_installs FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS integrations.plugin_registry_quarantine ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS integrations.plugin_registry_quarantine FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS integrations.bridges ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS integrations.bridges FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS integrations.bridge_identities ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS integrations.bridge_identities FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS integrations.bridge_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS integrations.bridge_events FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_isolation_visual_preferences ON identity.visual_preferences;
CREATE POLICY tenant_isolation_visual_preferences ON identity.visual_preferences
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_plugins ON integrations.plugins;
CREATE POLICY tenant_isolation_plugins ON integrations.plugins
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_plugin_history_grants ON integrations.plugin_history_grants;
CREATE POLICY tenant_isolation_plugin_history_grants ON integrations.plugin_history_grants
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_plugin_interactive_offers ON integrations.plugin_interactive_offers;
CREATE POLICY tenant_isolation_plugin_interactive_offers ON integrations.plugin_interactive_offers
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_plugin_action_receipts ON integrations.plugin_action_receipts;
CREATE POLICY tenant_isolation_plugin_action_receipts ON integrations.plugin_action_receipts
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_plugin_quota_counters ON integrations.plugin_quota_counters;
CREATE POLICY tenant_isolation_plugin_quota_counters ON integrations.plugin_quota_counters
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_plugin_registry_installs ON integrations.plugin_registry_installs;
CREATE POLICY tenant_isolation_plugin_registry_installs ON integrations.plugin_registry_installs
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_plugin_registry_quarantine ON integrations.plugin_registry_quarantine;
CREATE POLICY tenant_isolation_plugin_registry_quarantine ON integrations.plugin_registry_quarantine
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_bridges ON integrations.bridges;
CREATE POLICY tenant_isolation_bridges ON integrations.bridges
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_bridge_identities ON integrations.bridge_identities;
CREATE POLICY tenant_isolation_bridge_identities ON integrations.bridge_identities
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_bridge_events ON integrations.bridge_events;
CREATE POLICY tenant_isolation_bridge_events ON integrations.bridge_events
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

DROP POLICY IF EXISTS tenant_isolation_user_statuses ON identity.user_statuses;
CREATE POLICY tenant_isolation_user_statuses ON identity.user_statuses
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_member_profiles ON identity.member_profiles;
CREATE POLICY tenant_isolation_member_profiles ON identity.member_profiles
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_import_jobs ON import.jobs;
CREATE POLICY tenant_isolation_import_jobs ON import.jobs
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_import_id_map ON import.id_map;
CREATE POLICY tenant_isolation_import_id_map ON import.id_map
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_import_historical_principals ON import.historical_principals;
CREATE POLICY tenant_isolation_import_historical_principals ON import.historical_principals
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_support_bundles ON support.bundles;
CREATE POLICY tenant_isolation_support_bundles ON support.bundles
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_support_repair_jobs ON support.repair_jobs;
CREATE POLICY tenant_isolation_support_repair_jobs ON support.repair_jobs
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

ALTER TABLE IF EXISTS identity.scim_tokens ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS identity.scim_tokens FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS identity.scim_principals ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS identity.scim_principals FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS identity.scim_groups ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS identity.scim_groups FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS identity.scim_group_members ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS identity.scim_group_members FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS identity.scim_group_mappings ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS identity.scim_group_mappings FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS identity.scim_membership_grants ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS identity.scim_membership_grants FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS identity.access_revocations ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS identity.access_revocations FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_isolation_scim_tokens ON identity.scim_tokens;
CREATE POLICY tenant_isolation_scim_tokens ON identity.scim_tokens
    USING (
        "TenantId" = app.current_tenant_id()
        OR (
            app.current_scim_token_hash() IS NOT NULL
            AND "TokenHash" = app.current_scim_token_hash()
        )
    )
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_scim_principals ON identity.scim_principals;
CREATE POLICY tenant_isolation_scim_principals ON identity.scim_principals
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_scim_groups ON identity.scim_groups;
CREATE POLICY tenant_isolation_scim_groups ON identity.scim_groups
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_scim_group_members ON identity.scim_group_members;
CREATE POLICY tenant_isolation_scim_group_members ON identity.scim_group_members
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_scim_group_mappings ON identity.scim_group_mappings;
CREATE POLICY tenant_isolation_scim_group_mappings ON identity.scim_group_mappings
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_scim_membership_grants ON identity.scim_membership_grants;
CREATE POLICY tenant_isolation_scim_membership_grants ON identity.scim_membership_grants
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

ALTER TABLE IF EXISTS compliance.grants ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS compliance.grants FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS compliance.legal_holds ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS compliance.legal_holds FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS compliance.discovery_cases ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS compliance.discovery_cases FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS compliance.discovery_exports ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS compliance.discovery_exports FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS compliance.custody_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS compliance.custody_events FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_isolation_access_revocations ON identity.access_revocations;
CREATE POLICY tenant_isolation_access_revocations ON identity.access_revocations
    USING (
        "TenantId" = app.current_tenant_id()
        OR (
            app.current_tenant_id() IS NULL
            AND "UserId" = app.current_user_id()
        )
    )
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_compliance_grants ON compliance.grants;
CREATE POLICY tenant_isolation_compliance_grants ON compliance.grants
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_legal_holds ON compliance.legal_holds;
CREATE POLICY tenant_isolation_legal_holds ON compliance.legal_holds
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_discovery_cases ON compliance.discovery_cases;
CREATE POLICY tenant_isolation_discovery_cases ON compliance.discovery_cases
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_discovery_exports ON compliance.discovery_exports;
CREATE POLICY tenant_isolation_discovery_exports ON compliance.discovery_exports
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_custody_events ON compliance.custody_events;
CREATE POLICY tenant_isolation_custody_events ON compliance.custody_events
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

ALTER TABLE IF EXISTS compliance.dlp_policies ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS compliance.dlp_policies FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS compliance.data_labels ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS compliance.data_labels FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS compliance.label_assignments ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS compliance.label_assignments FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS compliance.dlp_rules ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS compliance.dlp_rules FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS compliance.dlp_detectors ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS compliance.dlp_detectors FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS compliance.dlp_exceptions ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS compliance.dlp_exceptions FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS compliance.dlp_false_positives ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS compliance.dlp_false_positives FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS compliance.dlp_decisions ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS compliance.dlp_decisions FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_isolation_dlp_policies ON compliance.dlp_policies;
CREATE POLICY tenant_isolation_dlp_policies ON compliance.dlp_policies
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_data_labels ON compliance.data_labels;
CREATE POLICY tenant_isolation_data_labels ON compliance.data_labels
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_label_assignments ON compliance.label_assignments;
CREATE POLICY tenant_isolation_label_assignments ON compliance.label_assignments
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_dlp_rules ON compliance.dlp_rules;
CREATE POLICY tenant_isolation_dlp_rules ON compliance.dlp_rules
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_dlp_detectors ON compliance.dlp_detectors;
CREATE POLICY tenant_isolation_dlp_detectors ON compliance.dlp_detectors
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_dlp_exceptions ON compliance.dlp_exceptions;
CREATE POLICY tenant_isolation_dlp_exceptions ON compliance.dlp_exceptions
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_dlp_false_positives ON compliance.dlp_false_positives;
CREATE POLICY tenant_isolation_dlp_false_positives ON compliance.dlp_false_positives
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_dlp_decisions ON compliance.dlp_decisions;
CREATE POLICY tenant_isolation_dlp_decisions ON compliance.dlp_decisions
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

ALTER TABLE IF EXISTS compliance.siem_destinations ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS compliance.siem_destinations FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS compliance.siem_deliveries ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS compliance.siem_deliveries FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_isolation_siem_destinations ON compliance.siem_destinations;
CREATE POLICY tenant_isolation_siem_destinations ON compliance.siem_destinations
    USING (
        "TenantId" = app.current_tenant_id()
        OR app.current_job_role() = 'siem'
    )
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_siem_deliveries ON compliance.siem_deliveries;
CREATE POLICY tenant_isolation_siem_deliveries ON compliance.siem_deliveries
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

ALTER TABLE IF EXISTS compliance.policy_assignments ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS compliance.policy_assignments FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_isolation_policy_assignments ON compliance.policy_assignments;
CREATE POLICY tenant_isolation_policy_assignments ON compliance.policy_assignments
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

ALTER TABLE IF EXISTS compliance.scoped_role_assignments ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS compliance.scoped_role_assignments FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS compliance.quotas ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS compliance.quotas FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS compliance.quota_counters ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS compliance.quota_counters FORCE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS compliance.quota_alerts ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS compliance.quota_alerts FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_isolation_scoped_role_assignments ON compliance.scoped_role_assignments;
CREATE POLICY tenant_isolation_scoped_role_assignments ON compliance.scoped_role_assignments
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_quotas ON compliance.quotas;
CREATE POLICY tenant_isolation_quotas ON compliance.quotas
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_quota_counters ON compliance.quota_counters;
CREATE POLICY tenant_isolation_quota_counters ON compliance.quota_counters
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

DROP POLICY IF EXISTS tenant_isolation_quota_alerts ON compliance.quota_alerts;
CREATE POLICY tenant_isolation_quota_alerts ON compliance.quota_alerts
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

ALTER TABLE IF EXISTS directory.brand_settings ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS directory.brand_settings FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation_brand_settings ON directory.brand_settings;
CREATE POLICY tenant_isolation_brand_settings ON directory.brand_settings
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

ALTER TABLE IF EXISTS identity.desktop_devices ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS identity.desktop_devices FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation_desktop_devices ON identity.desktop_devices;
CREATE POLICY tenant_isolation_desktop_devices ON identity.desktop_devices
    USING ("TenantId" = app.current_tenant_id())
    WITH CHECK ("TenantId" = app.current_tenant_id());

CREATE SCHEMA IF NOT EXISTS compliance;

CREATE OR REPLACE FUNCTION compliance.reject_custody_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
  RAISE EXCEPTION 'custody_events are append-only'
    USING ERRCODE = '55000';
END;
$$;

DO $$
BEGIN
  IF to_regclass('compliance.custody_events') IS NOT NULL THEN
    DROP TRIGGER IF EXISTS custody_events_append_only ON compliance.custody_events;
    CREATE TRIGGER custody_events_append_only
      BEFORE UPDATE OR DELETE ON compliance.custody_events
      FOR EACH ROW EXECUTE FUNCTION compliance.reject_custody_mutation();
  END IF;
END
$$;

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
      'identity', 'administration', 'import', 'support', 'knowledge', 'search', 'automation', 'incident', 'connectors', 'compliance', 'app', 'public'
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
    EXECUTE format('GRANT EXECUTE ON FUNCTION app.current_invite_token_hash() TO %I', app_role);
    EXECUTE format('GRANT EXECUTE ON FUNCTION app.current_integration_token_hash() TO %I', app_role);
    EXECUTE format('GRANT EXECUTE ON FUNCTION app.current_scim_token_hash() TO %I', app_role);
    IF to_regclass('compliance.custody_events') IS NOT NULL THEN
      EXECUTE format('REVOKE UPDATE, DELETE ON compliance.custody_events FROM %I', app_role);
    END IF;
  END IF;

  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = backup_role) THEN
    FOREACH sch IN ARRAY ARRAY[
      'tenancy', 'directory', 'conversations', 'messaging', 'files',
      'building_blocks', 'audit', 'ai', 'notifications', 'integrations',
      'identity', 'administration', 'import', 'support', 'knowledge', 'search', 'automation', 'incident', 'connectors', 'compliance', 'app', 'public'
    ]
    LOOP
      EXECUTE format('GRANT USAGE ON SCHEMA %I TO %I', sch, backup_role);
      EXECUTE format('GRANT SELECT ON ALL TABLES IN SCHEMA %I TO %I', sch, backup_role);
    END LOOP;
  END IF;
END
$$;
