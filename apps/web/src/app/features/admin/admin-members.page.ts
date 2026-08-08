import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { HlmSelectImports } from '@spartan-ng/helm/select';
import { ApiService } from '../../core/api/api.service';
import { WorkspaceMember } from '../../shared/models/chat.models';
import { Badge } from '../../shared/ui';
import { AdminContextService } from './admin-context.service';
import { AdminAreaId } from './admin-permissions';

const PROTECTED_ROLES = new Set(['PlatformOwner', 'WorkspaceOwner', 'Guest', 'Bot']);
const MANAGER_ROLES = new Set(['PlatformOwner', 'WorkspaceOwner', 'Admin']);

type MemberStatusFilter = 'all' | 'active' | 'pending';

function isPendingMember(member: WorkspaceMember): boolean {
  const name = member.displayName.toLowerCase();
  return name.includes('pending') || name === member.email.toLowerCase();
}

@Component({
  selector: 'vc-admin-members',
  standalone: true,
  imports: [Badge, ...HlmSelectImports],
  templateUrl: './admin-members.page.html',
  styleUrl: './admin-shared.scss',
})
export class AdminMembersPage implements OnInit {
  readonly areaId: AdminAreaId = 'members';

  private readonly api = inject(ApiService);
  readonly ctx = inject(AdminContextService);

  readonly loading = signal(true);
  readonly loadError = signal(false);
  readonly members = signal<WorkspaceMember[]>([]);
  readonly assignableRoles = signal<string[]>(['Member', 'Moderator', 'Auditor', 'Admin']);
  readonly roleBusyUserId = signal<string | null>(null);
  readonly roleFeedback = signal<string | null>(null);
  readonly inviteBusy = signal(false);
  readonly inviteFeedback = signal<string | null>(null);
  readonly inviteError = signal<string | null>(null);

  readonly searchQuery = signal('');
  readonly roleFilter = signal('all');
  readonly statusFilter = signal<MemberStatusFilter>('all');

  readonly filteredMembers = computed(() => {
    const q = this.searchQuery().trim().toLowerCase();
    const role = this.roleFilter();
    const status = this.statusFilter();

    return this.members().filter((member) => {
      if (role !== 'all' && member.role !== role) {
        return false;
      }
      const pending = isPendingMember(member);
      if (status === 'active' && pending) {
        return false;
      }
      if (status === 'pending' && !pending) {
        return false;
      }
      if (!q) {
        return true;
      }
      return (
        member.displayName.toLowerCase().includes(q) ||
        member.email.toLowerCase().includes(q)
      );
    });
  });

  readonly roleOptions = computed(() => {
    const roles = new Set(this.members().map((m) => m.role));
    return ['all', ...Array.from(roles).sort()];
  });

  async ngOnInit(): Promise<void> {
    await this.ctx.ensureReady();
    await this.loadMembers();
    this.loading.set(false);
  }

  canInvite(): boolean {
    return this.ctx.canInvite();
  }

  canEditRole(member: WorkspaceMember): boolean {
    const role = this.ctx.role();
    if (!role || !MANAGER_ROLES.has(role)) {
      return false;
    }
    if (PROTECTED_ROLES.has(member.role)) {
      return false;
    }
    if (member.userId === this.ctx.currentUserId()) {
      return false;
    }
    return true;
  }

  async onInviteSubmit(event: Event): Promise<void> {
    event.preventDefault();
    const workspaceId = this.ctx.workspace()?.id;
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

  async onRoleChange(member: WorkspaceMember, nextRole: string | null | undefined): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
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

  private async loadMembers(): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId) {
      this.members.set([]);
      return;
    }

    try {
      const [members, roles] = await Promise.all([
        this.api.getMembers(workspaceId),
        this.ctx.canInvite()
          ? this.api.getAssignableRoles(workspaceId).catch(() => this.assignableRoles())
          : Promise.resolve(this.assignableRoles()),
      ]);
      this.members.set(members);
      this.assignableRoles.set(roles.length ? roles : this.assignableRoles());
      this.loadError.set(false);
    } catch {
      this.loadError.set(true);
      this.members.set([]);
    }
  }
}
