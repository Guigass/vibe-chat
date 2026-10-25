export type OfflineCommandKind = 'send' | 'edit' | 'reaction' | 'task';
export type OfflineCommandState = 'queued' | 'sending' | 'acked' | 'conflict' | 'failed' | 'cancelled';

export interface OfflineTaskPayload {
  title: string;
  description?: string | null;
  priority: string;
  assigneeUserIds: string[];
  dueAt?: string | null;
  visibility: string;
  checklist: { text: string; done: boolean }[];
}

export interface OfflineCommand {
  id: string;
  kind: OfflineCommandKind;
  idempotencyKey: string;
  clientId: string;
  state: OfflineCommandState;
  channelId: string;
  threadId?: string | null;
  workspaceId?: string | null;
  messageId?: string | null;
  body?: string | null;
  emoji?: string | null;
  intent?: 'add' | 'remove' | null;
  expectedVersion?: number | null;
  task?: OfflineTaskPayload | null;
  attempts: number;
  error?: string | null;
  serverBody?: string | null;
}

export interface CachedEntry {
  kind: 'message' | 'task';
  conversationId: string;
  channelId: string;
  id: string;
  seq: number;
  version: number;
  body: string | null;
}

export interface OfflinePartitionState {
  commands: OfflineCommand[];
  entries: CachedEntry[];
  blocked: boolean;
}

export interface OfflineChange {
  kind: 'message' | 'task';
  conversationId: string;
  channelId: string;
  id: string;
  seq: number;
  version: number;
  body: string | null;
}

export interface OfflineTombstone {
  kind: 'message' | 'task';
  conversationId: string;
  id: string;
  reason: string;
}

export interface OfflinePage {
  rebuild: string | null;
  changes: OfflineChange[];
  tombstones: OfflineTombstone[];
  authorizedConversationIds: string[];
  conversationCoverage: 'complete' | 'partial';
}

export interface OfflineEnqueue {
  kind: OfflineCommandKind;
  idempotencyKey: string;
  clientId: string;
  channelId: string;
  threadId?: string | null;
  workspaceId?: string | null;
  messageId?: string | null;
  body?: string | null;
  emoji?: string | null;
  intent?: 'add' | 'remove' | null;
  expectedVersion?: number | null;
  task?: OfflineTaskPayload | null;
}

const MAX_CONVERSATIONS = 8;
const MAX_ENTRIES = 40;

export interface OfflineQueuePort {
  shouldQueue(): boolean;
  enqueue(input: OfflineEnqueue): Promise<boolean>;
}

export function emptyPartition(): OfflinePartitionState {
  return { commands: [], entries: [], blocked: false };
}

export function partitionKey(accountId: string, tenantId: string): string {
  return `${accountId}|${tenantId}`;
}

export function versionOf(editedAt: string | null | undefined): number {
  if (!editedAt) return 0;
  const ms = Date.parse(editedAt);
  return Number.isFinite(ms) ? ms : 0;
}

export function enqueueCommand(
  state: OfflinePartitionState,
  input: OfflineEnqueue,
): { state: OfflinePartitionState; duplicate: boolean } {
  const duplicate = state.commands.some(
    (command) => command.idempotencyKey === input.idempotencyKey && command.kind === input.kind,
  );
  if (duplicate) return { state, duplicate: true };
  const command: OfflineCommand = {
    id: input.clientId,
    kind: input.kind,
    idempotencyKey: input.idempotencyKey,
    clientId: input.clientId,
    state: 'queued',
    channelId: input.channelId,
    threadId: input.threadId ?? null,
    workspaceId: input.workspaceId ?? null,
    messageId: input.messageId ?? null,
    body: input.body ?? null,
    emoji: input.emoji ?? null,
    intent: input.intent ?? null,
    expectedVersion: input.expectedVersion ?? null,
    task: input.task ?? null,
    attempts: 0,
    error: null,
    serverBody: null,
  };
  return { state: { ...state, commands: [...state.commands, command] }, duplicate: false };
}

export function retryCommand(state: OfflinePartitionState, id: string): OfflinePartitionState {
  return {
    ...state,
    blocked: false,
    commands: state.commands.map((command) =>
      command.id === id && (command.state === 'failed' || command.state === 'conflict')
        ? { ...command, state: 'queued', error: null }
        : command,
    ),
  };
}

export function discardCommand(state: OfflinePartitionState, id: string): OfflinePartitionState {
  return {
    ...state,
    commands: state.commands.map((command) =>
      command.id === id ? { ...command, state: 'cancelled' } : command,
    ),
  };
}

export function visibleCommands(state: OfflinePartitionState): OfflineCommand[] {
  return state.commands.filter((command) => command.state !== 'acked' && command.state !== 'cancelled');
}

export function applyPage(state: OfflinePartitionState, page: OfflinePage): OfflinePartitionState {
  if (page.rebuild === 'ScopeRevoked') {
    return { commands: [], entries: [], blocked: false };
  }

  let next: OfflinePartitionState = page.rebuild
    ? {
        commands: state.commands.filter((command) => command.state !== 'acked' && command.state !== 'cancelled'),
        entries: [],
        blocked: page.rebuild === 'ProtocolUpgradeRequired',
      }
    : { ...state, entries: [...state.entries] };

  for (const change of page.changes) {
    next = upsert(next, change);
  }
  for (const tombstone of page.tombstones) {
    next = dropTarget(next, tombstone.id);
  }
  if (page.conversationCoverage === 'complete') {
    const allowed = new Set(page.authorizedConversationIds);
    next = {
      ...next,
      entries: next.entries.filter(
        (entry) => allowed.has(entry.conversationId) || allowed.has(entry.channelId),
      ),
      commands: next.commands.map((command) =>
        command.state === 'acked' || command.state === 'cancelled' || allowed.has(command.channelId)
          ? command
          : { ...command, state: 'cancelled', error: 'revoked' },
      ),
    };
  }
  return trim(next);
}

export function orderedEntries(entries: readonly CachedEntry[]): CachedEntry[] {
  return [...entries].sort((left, right) => left.seq - right.seq || left.id.localeCompare(right.id));
}

function upsert(state: OfflinePartitionState, change: OfflineChange): OfflinePartitionState {
  const without = state.entries.filter((entry) => entry.id !== change.id);
  return {
    ...state,
    entries: [
      ...without,
      {
        kind: change.kind,
        conversationId: change.conversationId,
        channelId: change.channelId,
        id: change.id,
        seq: change.seq,
        version: change.version,
        body: change.body,
      },
    ],
  };
}

function dropTarget(state: OfflinePartitionState, id: string): OfflinePartitionState {
  return {
    ...state,
    entries: state.entries.filter((entry) => entry.id !== id),
    commands: state.commands.map((command) =>
      command.messageId === id && command.state !== 'acked'
        ? { ...command, state: 'cancelled', error: 'deleted', body: null }
        : command,
    ),
  };
}

function trim(state: OfflinePartitionState): OfflinePartitionState {
  const pinned = new Set(
    state.commands
      .filter((command) => command.state !== 'acked' && command.state !== 'cancelled' && command.messageId)
      .map((command) => command.messageId as string),
  );
  const byConversation = new Map<string, CachedEntry[]>();
  for (const entry of orderedEntries(state.entries)) {
    const bucket = byConversation.get(entry.conversationId) ?? [];
    bucket.push(entry);
    byConversation.set(entry.conversationId, bucket);
  }
  const conversations = [...byConversation.keys()];
  while (conversations.length > MAX_CONVERSATIONS) {
    const oldest = conversations.shift();
    if (!oldest) break;
    const bucket = byConversation.get(oldest) ?? [];
    if (bucket.some((entry) => pinned.has(entry.id))) continue;
    byConversation.delete(oldest);
  }
  let entries = [...byConversation.values()].flat();
  if (entries.length > MAX_ENTRIES) {
    const removable = entries.filter((entry) => !pinned.has(entry.id));
    const removeCount = Math.min(removable.length, entries.length - MAX_ENTRIES);
    const drop = new Set(removable.slice(0, removeCount).map((entry) => entry.id));
    entries = entries.filter((entry) => !drop.has(entry.id));
  }
  return { ...state, entries };
}
