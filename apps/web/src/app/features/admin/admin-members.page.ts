import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { HlmSelectImports } from '@spartan-ng/helm/select';
import { ApiService } from '../../core/api/api.service';
import { fillTemplate, ui } from '../../core/i18n/strings';
import { ContactGroup, WorkspaceMember } from '../../shared/models/chat.models';
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
  readonly ui = ui;
  readonly fillTemplate = fillTemplate;

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

  readonly departments = signal<ContactGroup[]>([]);
  readonly departmentBusy = signal(false);
  readonly departmentFeedback = signal<string | null>(null);
  readonly departmentError = signal<string | null>(null);
  readonly departmentDrafts = signal<Record<string, string[]>>({});
  readonly confirmDeleteId = signal<string | null>(null);

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
    if (this.canInvite()) {
      await this.loadDepartments();
    }
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
      this.inviteError.set(ui.adminNeedEmail);
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
        fillTemplate(ui.adminInviteOk, { name: created.displayName, role: created.role }),
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
          ? ui.adminAlreadyMember
          : status === 403
            ? ui.adminInviteForbidden
            : ui.adminInviteError,
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
      this.roleFeedback.set(
        fillTemplate(ui.adminRoleUpdated, { name: updated.displayName, role: updated.role }),
      );
    } catch (err) {
      const status = (err as { status?: number } | null)?.status;
      this.roleFeedback.set(status === 403 ? ui.adminRoleForbidden : ui.adminRoleError);
    } finally {
      this.roleBusyUserId.set(null);
    }
  }

  async onDepartmentSubmit(event: Event): Promise<void> {
    event.preventDefault();
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId || !this.canInvite()) return;
    const form = event.target as HTMLFormElement;
    const name = String(new FormData(form).get('name') ?? '').trim();
    if (!name) return;
    this.departmentBusy.set(true);
    this.departmentError.set(null);
    this.departmentFeedback.set(null);
    try {
      await this.api.createContactGroup(workspaceId, { name, kind: 'department' });
      form.reset();
      await this.loadDepartments();
      this.departmentFeedback.set(ui.adminDepartmentSaved);
    } catch (err) {
      this.departmentError.set(this.departmentErrorMessage(err));
    } finally {
      this.departmentBusy.set(false);
    }
  }

  async renameDepartment(group: ContactGroup, name: string): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    const next = name.trim();
    if (!workspaceId || !next || next === group.name) return;
    this.departmentBusy.set(true);
    this.departmentError.set(null);
    try {
      await this.api.updateContactGroup(workspaceId, group.id, { name: next });
      await this.loadDepartments();
      this.departmentFeedback.set(ui.adminDepartmentSaved);
    } catch (err) {
      this.departmentError.set(this.departmentErrorMessage(err));
    } finally {
      this.departmentBusy.set(false);
    }
  }

  async deleteDepartment(groupId: string): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId) return;
    this.departmentBusy.set(true);
    this.departmentError.set(null);
    try {
      await this.api.deleteContactGroup(workspaceId, groupId);
      this.confirmDeleteId.set(null);
      await this.loadDepartments();
      this.departmentFeedback.set(ui.adminDepartmentDeleted);
    } catch (err) {
      this.departmentError.set(this.departmentErrorMessage(err));
    } finally {
      this.departmentBusy.set(false);
    }
  }

  departmentChecked(group: ContactGroup, userId: string): boolean {
    const draft = this.departmentDrafts()[group.id];
    return (draft ?? group.memberUserIds).includes(userId);
  }

  toggleDepartmentMember(group: ContactGroup, userId: string, checked: boolean): void {
    const current = this.departmentDrafts()[group.id] ?? [...group.memberUserIds];
    const next = checked ? [...current, userId] : current.filter((id) => id !== userId);
    this.departmentDrafts.update((drafts) => ({ ...drafts, [group.id]: [...new Set(next)] }));
  }

  async saveDepartmentMembers(group: ContactGroup): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId) return;
    const userIds = this.departmentDrafts()[group.id] ?? group.memberUserIds;
    this.departmentBusy.set(true);
    this.departmentError.set(null);
    try {
      await this.api.replaceContactGroupMembers(workspaceId, group.id, userIds);
      await this.loadDepartments();
      this.departmentFeedback.set(ui.adminDepartmentSaved);
    } catch (err) {
      this.departmentError.set(this.departmentErrorMessage(err));
    } finally {
      this.departmentBusy.set(false);
    }
  }

  private async loadDepartments(): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId) {
      this.departments.set([]);
      return;
    }
    try {
      const groups = await this.api.getContactGroups(workspaceId);
      this.departments.set(groups.filter((group) => group.kind === 'department'));
      this.departmentDrafts.set({});
    } catch {
      this.departmentError.set(ui.contactsActionError);
    }
  }

  private departmentErrorMessage(err: unknown): string {
    const status = (err as { status?: number } | null)?.status;
    return status === 409 ? ui.contactsNameTaken : ui.contactsActionError;
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
