import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { environment } from '../../../environments/environment';
import { ApiService } from '../../core/api/api.service';
import { AuthService } from '../../core/auth/auth.service';
import { AdminStats, AuditEventItem, Workspace, WorkspaceMember } from '../../shared/models/chat.models';
import { Skeleton, ThemeToggle } from '../../shared/ui';

const MANAGER_ROLES = new Set(['PlatformOwner', 'WorkspaceOwner', 'Admin']);
const PROTECTED_ROLES = new Set(['PlatformOwner', 'WorkspaceOwner', 'Guest', 'Bot']);

@Component({
  selector: 'vc-admin-page',
  standalone: true,
  imports: [RouterLink, Skeleton, ThemeToggle, DatePipe],
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
  readonly currentUserId = signal<string | null>(null);

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
    this.loading.set(false);
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
}
