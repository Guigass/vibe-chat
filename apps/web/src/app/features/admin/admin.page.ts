import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TableModule } from 'primeng/table';
import { SelectModule } from 'primeng/select';
import { TagModule } from 'primeng/tag';
import { environment } from '../../../environments/environment';
import { ApiService } from '../../core/api/api.service';
import { AuthService } from '../../core/auth/auth.service';
import {
  AdminConversationItem,
  AdminConversationMessageItem,
  AdminStats,
  AuditEventItem,
  SensitiveSettings,
  Workspace,
  WorkspaceMember,
} from '../../shared/models/chat.models';
import { Skeleton, ThemeToggle } from '../../shared/ui';

const MANAGER_ROLES = new Set(['PlatformOwner', 'WorkspaceOwner', 'Admin']);
const PROTECTED_ROLES = new Set(['PlatformOwner', 'WorkspaceOwner', 'Guest', 'Bot']);

interface ConversationOption {
  id: string;
  label: string;
}

@Component({
  selector: 'vc-admin-page',
  standalone: true,
  imports: [
    RouterLink,
    FormsModule,
    Skeleton,
    ThemeToggle,
    DatePipe,
    TableModule,
    SelectModule,
    TagModule,
  ],
  templateUrl: './admin.page.html',
  styleUrl: './admin.page.scss',
})
export class AdminPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);

  readonly loading = signal(true);
  readonly stats = signal<AdminStats | null>(null);
  readonly usingDemo = signal(false);
  readonly auditEvents = signal<AuditEventItem[]>([]);
  readonly auditForbidden = signal(false);
  readonly auditError = signal(false);

  readonly workspace = signal<Workspace | null>(null);
  readonly members = signal<WorkspaceMember[]>([]);
  readonly assignableRoles = signal<string[]>(['Member', 'Moderator', 'Auditor', 'Admin']);
  readonly membersForbidden = signal(false);
  readonly membersError = signal(false);
  readonly roleBusyUserId = signal<string | null>(null);
  readonly roleFeedback = signal<string | null>(null);
  readonly inviteBusy = signal(false);
  readonly inviteFeedback = signal<string | null>(null);
  readonly inviteError = signal<string | null>(null);
  readonly currentUserId = signal<string | null>(null);

  readonly settings = signal<SensitiveSettings | null>(null);
  readonly settingsForbidden = signal(false);
  readonly settingsError = signal(false);
  readonly settingsBusy = signal(false);
  readonly settingsFeedback = signal<string | null>(null);
  readonly settingsErrorMessage = signal<string | null>(null);

  readonly conversations = signal<AdminConversationItem[]>([]);
  readonly conversationsForbidden = signal(false);
  readonly conversationsError = signal(false);
  readonly selectedConversationId = signal<string | null>(null);
  readonly conversationMessages = signal<AdminConversationMessageItem[]>([]);
  readonly conversationMessagesBusy = signal(false);
  readonly conversationMessagesError = signal(false);
  readonly activeThreadId = signal<string | null>(null);
  readonly threadMessages = signal<AdminConversationMessageItem[]>([]);
  readonly threadBusy = signal(false);

  readonly conversationOptions = computed<ConversationOption[]>(() =>
    this.conversations().map((c) => ({ id: c.id, label: this.conversationLabel(c) })),
  );

  canInvite(): boolean {
    const role = this.workspace()?.role;
    return !!role && MANAGER_ROLES.has(role);
  }

  messageStatusSeverity(
    m: AdminConversationMessageItem,
  ): 'success' | 'warn' | 'danger' | 'secondary' {
    if (m.deletedAt) {
      return 'danger';
    }
    if (m.editedAt) {
      return 'warn';
    }
    return 'success';
  }

  messageStatusLabel(m: AdminConversationMessageItem): string {
    if (m.deletedAt) {
      return m.deletedByName ? `soft-delete · ${m.deletedByName}` : 'soft-delete';
    }
    if (m.editedAt) {
      return 'editado';
    }
    return 'ok';
  }

  async ngOnInit(): Promise<void> {
    this.currentUserId.set(this.auth.profile()?.id ?? null);

    try {
      const stats = await this.api.getAdminStats();
      this.stats.set(stats);
    } catch {
      this.usingDemo.set(true);
      this.stats.set({
        users: 128,
        onlineUsers: 17,
        workspaces: 6,
        channels: 42,
        messages: 18420,
        realtimeConnections: 23,
        outboxPending: 2,
        processingFailures: 0,
        health: {
          postgres: 'up',
          redis: 'up',
          storage: 'up',
        },
        appVersion: environment.appVersion,
        grafanaUrl: environment.grafanaUrl,
      });
    }

    try {
      const events = await this.api.getAdminAuditEvents(40);
      this.auditEvents.set(events);
      this.auditForbidden.set(false);
      this.auditError.set(false);
    } catch (err) {
      const status = (err as { status?: number } | null)?.status;
      this.auditForbidden.set(status === 403);
      this.auditError.set(status !== 403);
      this.auditEvents.set([]);
    }

    await this.loadMembersSection();
    await this.loadSettingsSection();
    await this.loadConversationsSection();
    this.loading.set(false);
  }

  conversationLabel(row: AdminConversationItem): string {
    const prefix =
      row.type === 'Direct' ? 'DM' : row.type === 'Private' ? 'Private' : row.type === 'Group' ? 'Group' : '#';
    return `${prefix} ${row.name}`;
  }

  async onConversationSelected(channelId: string | null): Promise<void> {
    this.selectedConversationId.set(channelId);
    this.activeThreadId.set(null);
    this.threadMessages.set([]);
    this.conversationMessages.set([]);
    this.conversationMessagesError.set(false);
    if (!channelId) {
      return;
    }

    this.conversationMessagesBusy.set(true);
    try {
      const rows = await this.api.getAdminConversationMessages(channelId, { limit: 80 });
      this.conversationMessages.set(rows);
    } catch {
      this.conversationMessagesError.set(true);
      this.conversationMessages.set([]);
    } finally {
      this.conversationMessagesBusy.set(false);
    }
  }

  async openThread(threadId: string): Promise<void> {
    if (!threadId) {
      return;
    }
    this.activeThreadId.set(threadId);
    this.threadBusy.set(true);
    try {
      const rows = await this.api.getAdminThreadMessages(threadId, { limit: 80 });
      this.threadMessages.set(rows);
    } catch {
      this.threadMessages.set([]);
    } finally {
      this.threadBusy.set(false);
    }
  }

  closeThread(): void {
    this.activeThreadId.set(null);
    this.threadMessages.set([]);
  }

  canManageSettings(): boolean {
    return !!this.settings() && !this.settingsForbidden();
  }

  async onSettingsSubmit(event: Event): Promise<void> {
    event.preventDefault();
    const current = this.settings();
    const workspaceId = this.workspace()?.id ?? current?.workspaceId;
    if (!workspaceId || !this.canManageSettings() || !current) {
      return;
    }

    const form = event.target as HTMLFormElement;
    const data = new FormData(form);
    const workspaceEnabled = data.get('aiWorkspaceEnabled') === 'on';
    const provider = String(data.get('aiProvider') ?? current.ai.provider).trim();
    const emailEnabled = data.get('emailEnabled') === 'on';
    const smtpHost = String(data.get('smtpHost') ?? '').trim();
    const smtpPort = Number(data.get('smtpPort') ?? current.email.smtpPort);
    const smtpUsername = String(data.get('smtpUsername') ?? '').trim();
    const smtpFrom = String(data.get('smtpFrom') ?? '').trim();
    const useStartTls = data.get('useStartTls') === 'on';

    this.settingsBusy.set(true);
    this.settingsFeedback.set(null);
    this.settingsErrorMessage.set(null);
    try {
      const updated = await this.api.updateAdminSensitiveSettings({
        workspaceId,
        ai: { workspaceEnabled, provider },
        email: {
          enabled: emailEnabled,
          smtpHost,
          smtpPort: Number.isFinite(smtpPort) ? smtpPort : current.email.smtpPort,
          smtpUsername,
          smtpFrom,
          useStartTls,
        },
      });
      this.settings.set(updated);
      this.settingsFeedback.set('Configurações atualizadas (secrets continuam só via env).');
    } catch (err) {
      const status = (err as { status?: number } | null)?.status;
      this.settingsErrorMessage.set(
        status === 403
          ? 'Sem permissão para alterar settings sensíveis.'
          : 'Não foi possível salvar as configurações.',
      );
    } finally {
      this.settingsBusy.set(false);
    }
  }

  canEditRole(member: WorkspaceMember): boolean {
    const ws = this.workspace();
    if (!ws?.role || !MANAGER_ROLES.has(ws.role)) {
      return false;
    }
    if (PROTECTED_ROLES.has(member.role)) {
      return false;
    }
    if (member.userId === this.currentUserId()) {
      return false;
    }
    return true;
  }

  async onInviteSubmit(event: Event): Promise<void> {
    event.preventDefault();
    const workspaceId = this.workspace()?.id;
    if (!workspaceId || !this.canInvite()) {
      return;
    }

    const form = event.target as HTMLFormElement;
    const data = new FormData(form);
    const email = String(data.get('email') ?? '').trim();
    const displayName = String(data.get('displayName') ?? '').trim();
    const role = String(data.get('role') ?? 'Member').trim() || 'Member';
    if (!email) {
      this.inviteError.set('Informe um e-mail válido.');
      return;
    }

    this.inviteBusy.set(true);
    this.inviteError.set(null);
    this.inviteFeedback.set(null);
    try {
      const created = await this.api.inviteMember(workspaceId, {
        email,
        displayName: displayName || undefined,
        role,
      });
      this.members.update((rows) =>
        [...rows.filter((row) => row.userId !== created.userId), created].sort((a, b) =>
          a.displayName.localeCompare(b.displayName),
        ),
      );
      this.inviteFeedback.set(
        `${created.displayName} provisionado como ${created.role}. SSO com este e-mail vincula a membership.`,
      );
      form.reset();
      const roleSelect = form.elements.namedItem('role') as HTMLSelectElement | null;
      if (roleSelect) {
        roleSelect.value = 'Member';
      }
    } catch (err) {
      const status = (err as { status?: number } | null)?.status;
      this.inviteError.set(
        status === 409
          ? 'Este e-mail já é membro do workspace.'
          : status === 403
            ? 'Sem permissão para convidar membros.'
            : 'Não foi possível convidar o membro.',
      );
    } finally {
      this.inviteBusy.set(false);
    }
  }

  async onRoleChange(member: WorkspaceMember, event: Event): Promise<void> {
    const select = event.target as HTMLSelectElement;
    const nextRole = select.value;
    const workspaceId = this.workspace()?.id;
    if (!workspaceId || !nextRole || nextRole === member.role) {
      return;
    }

    this.roleBusyUserId.set(member.userId);
    this.roleFeedback.set(null);
    try {
      const updated = await this.api.updateMemberRole(workspaceId, member.userId, nextRole);
      this.members.update((rows) =>
        rows.map((row) => (row.userId === updated.userId ? updated : row)),
      );
      this.roleFeedback.set(`Papel de ${updated.displayName} atualizado para ${updated.role}.`);
    } catch (err) {
      select.value = member.role;
      const status = (err as { status?: number } | null)?.status;
      this.roleFeedback.set(
        status === 403
          ? 'Sem permissão para alterar este papel.'
          : 'Não foi possível alterar o papel.',
      );
    } finally {
      this.roleBusyUserId.set(null);
    }
  }

  private async loadMembersSection(): Promise<void> {
    try {
      const workspaces = await this.api.getWorkspaces();
      const managed =
        workspaces.find((w) => w.role && MANAGER_ROLES.has(w.role)) ?? workspaces[0] ?? null;
      this.workspace.set(managed);
      if (!managed) {
        this.members.set([]);
        return;
      }

      const [members, roles] = await Promise.all([
        this.api.getMembers(managed.id),
        managed.role && MANAGER_ROLES.has(managed.role)
          ? this.api.getAssignableRoles(managed.id).catch(() => this.assignableRoles())
          : Promise.resolve(this.assignableRoles()),
      ]);
      this.members.set(members);
      this.assignableRoles.set(roles.length ? roles : this.assignableRoles());
      this.membersForbidden.set(false);
      this.membersError.set(false);
    } catch (err) {
      const status = (err as { status?: number } | null)?.status;
      this.membersForbidden.set(status === 403);
      this.membersError.set(status !== 403);
      this.members.set([]);
    }
  }

  private async loadSettingsSection(): Promise<void> {
    const workspaceId = this.workspace()?.id;
    try {
      const settings = await this.api.getAdminSensitiveSettings(workspaceId);
      this.settings.set(settings);
      this.settingsForbidden.set(false);
      this.settingsError.set(false);
    } catch (err) {
      const status = (err as { status?: number } | null)?.status;
      this.settingsForbidden.set(status === 403);
      this.settingsError.set(status !== 403);
      this.settings.set(null);
    }
  }

  private async loadConversationsSection(): Promise<void> {
    try {
      const rows = await this.api.getAdminConversations({
        workspaceId: this.workspace()?.id,
        limit: 100,
      });
      this.conversations.set(rows);
      this.conversationsForbidden.set(false);
      this.conversationsError.set(false);
    } catch (err) {
      const status = (err as { status?: number } | null)?.status;
      this.conversationsForbidden.set(status === 403);
      this.conversationsError.set(status !== 403);
      this.conversations.set([]);
    }
  }
}
