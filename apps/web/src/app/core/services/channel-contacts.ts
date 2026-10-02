import { ApiService } from '../api/api.service';
import { ui } from '../i18n/strings';
import { ContactSection, WorkspaceMember } from '../../shared/models/chat.models';
import { idsEqual } from './message-sync';

export function withoutSelfContacts(
  sections: ContactSection[],
  me: string | undefined,
): ContactSection[] {
  return sections.map((section) => ({
    ...section,
    members: section.members.filter((member) => !idsEqual(member.userId, me)),
  }));
}

export function contactDepartmentLabel(
  sections: readonly ContactSection[],
  userId: string,
): string | null {
  const names = sections
    .filter(
      (section) =>
        section.kind === 'department' &&
        section.members.some((member) => idsEqual(member.userId, userId)),
    )
    .map((section) => section.name)
    .filter((name): name is string => !!name);
  return names.length ? names.join(', ') : null;
}

export function contactErrorMessage(err: unknown): string {
  const status = (err as { status?: number } | null)?.status;
  return status === 409 ? ui.contactsNameTaken : ui.contactsActionError;
}

export interface PersonalGroupDeps {
  api: ApiService;
  workspaceId: () => string | null;
  isDemo: () => boolean;
  isGuest: () => boolean;
  profileId: () => string | undefined;
  setError: (message: string | null) => void;
  setMembers: (members: WorkspaceMember[]) => void;
  setSections: (sections: ContactSection[]) => void;
}

async function refreshContacts(deps: PersonalGroupDeps): Promise<void> {
  const workspaceId = deps.workspaceId();
  if (!workspaceId || deps.isDemo() || deps.isGuest()) return;
  const [members, sections] = await Promise.all([
    deps.api.getMembers(workspaceId),
    deps.api.getGroupedContacts(workspaceId),
  ]);
  deps.setMembers(members);
  deps.setSections(withoutSelfContacts(sections, deps.profileId()));
}

export async function createPersonalGroup(deps: PersonalGroupDeps, name: string): Promise<void> {
  const workspaceId = deps.workspaceId();
  if (!workspaceId || deps.isDemo()) return;
  deps.setError(null);
  try {
    await deps.api.createContactGroup(workspaceId, { name, kind: 'personal' });
    await refreshContacts(deps);
  } catch (err) {
    deps.setError(contactErrorMessage(err));
  }
}

export async function renamePersonalGroup(
  deps: PersonalGroupDeps,
  groupId: string,
  name: string,
): Promise<void> {
  const workspaceId = deps.workspaceId();
  if (!workspaceId || deps.isDemo()) return;
  deps.setError(null);
  try {
    await deps.api.updateContactGroup(workspaceId, groupId, { name });
    await refreshContacts(deps);
  } catch (err) {
    deps.setError(contactErrorMessage(err));
  }
}

export async function deletePersonalGroup(deps: PersonalGroupDeps, groupId: string): Promise<void> {
  const workspaceId = deps.workspaceId();
  if (!workspaceId || deps.isDemo()) return;
  deps.setError(null);
  try {
    await deps.api.deleteContactGroup(workspaceId, groupId);
    await refreshContacts(deps);
  } catch (err) {
    deps.setError(contactErrorMessage(err));
  }
}

export async function savePersonalMembers(
  deps: PersonalGroupDeps,
  groupId: string,
  userIds: string[],
): Promise<void> {
  const workspaceId = deps.workspaceId();
  if (!workspaceId || deps.isDemo()) return;
  deps.setError(null);
  try {
    await deps.api.replaceContactGroupMembers(workspaceId, groupId, userIds);
    await refreshContacts(deps);
  } catch (err) {
    deps.setError(contactErrorMessage(err));
  }
}
