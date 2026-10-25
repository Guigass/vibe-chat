import { Injectable, inject, signal } from '@angular/core';
import { OfflineApiService } from '../api/offline-api.service';
import { MessagingApiService } from '../api/messaging-api.service';
import { TaskApiService } from '../api/task-api.service';
import { AuthService } from '../auth/auth.service';
import { ChatMessage } from '../../shared/models/chat.models';
import { OfflineDatabase } from './offline-database';
import { createOfflineKey, offlineCryptoAvailable, openText, sealText } from './offline-crypto';
import {
  applyPage,
  discardCommand,
  emptyPartition,
  enqueueCommand,
  partitionKey,
  retryCommand,
  visibleCommands,
  type OfflineCommand,
  type OfflineEnqueue,
  type OfflinePage,
  type OfflinePartitionState,
  type OfflineQueuePort,
} from './offline-engine';

interface StoredPartition {
  enabled: boolean;
  cursor: string | null;
  state: OfflinePartitionState;
}

const DEVICE_KEY = 'vc.offline.device';

@Injectable({ providedIn: 'root' })
export class OfflineSyncService implements OfflineQueuePort {
  private readonly api = inject(OfflineApiService);
  private readonly messaging = inject(MessagingApiService);
  private readonly tasks = inject(TaskApiService);
  private readonly auth = inject(AuthService);
  private readonly database = inject(OfflineDatabase);

  readonly ready = signal(false);
  readonly enabled = signal(false);
  readonly confirmingClear = signal(false);
  readonly items = signal<OfflineCommand[]>([]);
  readonly delivered = signal<ChatMessage | null>(null);
  readonly revoked = signal(false);
  readonly cryptoMissing = signal(false);

  private cursor: string | null = null;
  private state: OfflinePartitionState = emptyPartition();
  private started = false;
  private pulling = false;
  private readonly onOnline = () => {
    void this.flush();
    void this.pull();
  };
  private readonly onVisible = () => {
    if (document.visibilityState === 'visible') void this.pull();
  };

  start(): void {
    if (this.started || typeof window === 'undefined') return;
    this.started = true;
    window.addEventListener('online', this.onOnline);
    document.addEventListener('visibilitychange', this.onVisible);
    void this.restore();
  }

  stop(): void {
    if (!this.started || typeof window === 'undefined') return;
    window.removeEventListener('online', this.onOnline);
    document.removeEventListener('visibilitychange', this.onVisible);
    this.started = false;
  }

  shouldQueue(): boolean {
    return this.enabled()
      && !this.state.blocked
      && typeof navigator !== 'undefined'
      && navigator.onLine === false;
  }

  async enqueue(input: OfflineEnqueue): Promise<boolean> {
    if (!this.shouldQueue()) return false;
    const partition = this.partition();
    if (!partition) return false;
    const next = enqueueCommand(this.state, input);
    this.state = next.state;
    this.publish();
    await this.persist(partition);
    return true;
  }

  async enable(): Promise<void> {
    if (!offlineCryptoAvailable() || typeof indexedDB === 'undefined') {
      this.cryptoMissing.set(true);
      return;
    }
    this.cryptoMissing.set(false);
    this.confirmingClear.set(false);
    this.enabled.set(true);
    await this.persist(this.partition());
    await this.pull();
  }

  askClear(): void {
    this.confirmingClear.set(true);
  }

  async clear(): Promise<void> {
    this.confirmingClear.set(false);
    this.enabled.set(false);
    this.state = emptyPartition();
    this.cursor = null;
    this.publish();
    const partition = this.partition();
    if (partition) await this.persist(partition);
  }

  async retry(id: string): Promise<void> {
    this.state = retryCommand(this.state, id);
    this.publish();
    await this.persist(this.partition());
    await this.flush();
  }

  async discard(id: string): Promise<void> {
    this.state = discardCommand(this.state, id);
    this.publish();
    await this.persist(this.partition());
  }

  private async restore(): Promise<void> {
    const partition = this.partition();
    if (partition && offlineCryptoAvailable()) {
      const stored = await this.read(partition);
      if (stored?.enabled) {
        this.enabled.set(true);
        this.cursor = stored.cursor;
        this.state = stored.state;
        this.publish();
      }
    }
    if (this.enabled()) {
      this.ready.set(true);
      await this.pull();
      return;
    }
    try {
      await this.api.sync(deviceId());
      this.ready.set(true);
    } catch (error) {
      if (statusOf(error) === 401) await this.wipe();
    }
  }

  private async pull(): Promise<void> {
    if (!this.enabled() || this.pulling || this.shouldQueue()) return;
    const partition = this.partition();
    if (!partition) return;
    this.pulling = true;
    try {
      let guard = 0;
      let hasMore = true;
      while (hasMore && guard < 4) {
        guard += 1;
        const page = await this.api.sync(deviceId(), this.cursor);
        if (page.rebuild?.reason === 'ProtocolUpgradeRequired') {
          this.state = { ...this.state, blocked: true };
          break;
        }
        this.state = applyPage(this.state, toPage(page));
        if (page.rebuild?.reason === 'ScopeRevoked') {
          this.enabled.set(false);
          this.revoked.set(true);
          this.cursor = null;
          break;
        }
        this.cursor = page.cursor || this.cursor;
        hasMore = page.hasMore;
        this.publish();
      }
      await this.persist(partition);
      await this.flush();
    } catch (error) {
      if (codeOf(error) === 'ProtocolUpgradeRequired') {
        this.state = { ...this.state, blocked: true };
        this.publish();
      }
      if (statusOf(error) === 401) await this.wipe();
    } finally {
      this.pulling = false;
    }
  }

  private async flush(): Promise<void> {
    if (this.shouldQueue() || this.state.blocked) return;
    const partition = this.partition();
    if (!partition) return;
    for (const command of [...this.state.commands]) {
      if (command.state !== 'queued') continue;
      this.replace(command.id, { ...command, state: 'sending', attempts: command.attempts + 1 });
      try {
        await this.dispatch(command);
        this.replace(command.id, { ...command, state: 'acked', error: null });
      } catch (error) {
        const status = statusOf(error);
        const code = codeOf(error);
        if (status === 404 || code === 'ScopeRevoked') {
          this.replace(command.id, { ...command, state: 'cancelled', error: 'deleted', body: null });
        } else if (status === 409) {
          this.replace(command.id, {
            ...command,
            state: 'conflict',
            error: code || 'conflict',
            serverBody: bodyOf(error),
          });
        } else if (status === 400 || status === 403 || status === 422) {
          this.replace(command.id, { ...command, state: 'failed', error: code || 'failed' });
        } else {
          this.replace(command.id, { ...command, state: 'queued', error: code || 'retry' });
        }
      }
      this.publish();
      await this.persist(partition);
    }
  }

  private async dispatch(command: OfflineCommand): Promise<void> {
    if (command.kind === 'send') {
      const message = command.threadId
        ? await this.messaging.sendThreadMessage({
            threadId: command.threadId,
            body: command.body ?? '',
            clientMessageId: command.clientId,
            idempotencyKey: command.idempotencyKey,
          })
        : await this.messaging.sendMessage({
            channelId: command.channelId,
            body: command.body ?? '',
            clientMessageId: command.clientId,
            idempotencyKey: command.idempotencyKey,
          });
      this.delivered.set({ ...message, status: 'persisted' });
      return;
    }
    if (command.kind === 'edit' && command.messageId) {
      await this.messaging.editMessage(
        command.channelId,
        command.messageId,
        command.body ?? '',
        command.expectedVersion,
      );
      return;
    }
    if (command.kind === 'reaction' && command.messageId && command.emoji && command.intent) {
      await this.messaging.toggleReaction(command.channelId, command.messageId, command.emoji, command.intent);
      return;
    }
    if (command.kind === 'task' && command.workspaceId && command.task && command.messageId) {
      await this.tasks.create(command.workspaceId, {
        idempotencyKey: command.idempotencyKey,
        messageId: command.messageId,
        title: command.task.title,
        description: command.task.description,
        priority: command.task.priority as 'low' | 'normal' | 'high' | 'urgent',
        assigneeUserIds: command.task.assigneeUserIds,
        dueAt: command.task.dueAt,
        visibility: command.task.visibility as 'channel' | 'restricted',
        checklist: command.task.checklist,
      });
    }
  }

  private replace(id: string, command: OfflineCommand): void {
    this.state = {
      ...this.state,
      commands: this.state.commands.map((item) => (item.id === id ? command : item)),
    };
  }

  private publish(): void {
    this.items.set(visibleCommands(this.state));
  }

  private partition(): string | null {
    const profile = this.auth.profile();
    if (!profile?.id || !profile.tenantId) return null;
    return partitionKey(profile.id, profile.tenantId);
  }

  private async persist(partition: string | null): Promise<void> {
    if (!partition || !offlineCryptoAvailable()) return;
    const key = await this.keyFor(partition);
    const stored: StoredPartition = {
      enabled: this.enabled(),
      cursor: this.cursor,
      state: this.state,
    };
    await this.database.write(partition, await sealText(key, JSON.stringify(stored)));
  }

  private async read(partition: string): Promise<StoredPartition | null> {
    const packed = await this.database.read(partition);
    if (!packed) return null;
    const key = await this.database.readKey(partition);
    if (!key) return null;
    const json = await openText(key, packed);
    if (!json) return null;
    const parsed = JSON.parse(json) as StoredPartition;
    return {
      enabled: parsed.enabled === true,
      cursor: parsed.cursor ?? null,
      state: parsed.state ?? emptyPartition(),
    };
  }

  private async keyFor(partition: string): Promise<CryptoKey> {
    const existing = await this.database.readKey(partition);
    if (existing) return existing;
    const created = await createOfflineKey();
    await this.database.writeKey(partition, created);
    return created;
  }

  private async wipe(): Promise<void> {
    this.enabled.set(false);
    this.state = emptyPartition();
    this.cursor = null;
    this.revoked.set(true);
    this.publish();
    await this.database.dropAll();
  }
}

function deviceId(): string {
  if (typeof localStorage === 'undefined' || typeof crypto === 'undefined') return '00000000-0000-4000-8000-000000000001';
  const existing = localStorage.getItem(DEVICE_KEY);
  if (existing) return existing;
  const created = crypto.randomUUID();
  localStorage.setItem(DEVICE_KEY, created);
  return created;
}

function toPage(page: {
  rebuild: { reason: string } | null;
  changes: OfflinePage['changes'];
  tombstones: Array<{ kind: 'message' | 'task'; conversationId: string; id: string; reason: string | null }>;
  authorizedConversationIds: string[];
  conversationCoverage: 'complete' | 'partial';
}): OfflinePage {
  return {
    rebuild: page.rebuild?.reason ?? null,
    changes: page.changes,
    tombstones: page.tombstones.map((item) => ({
      kind: item.kind,
      conversationId: item.conversationId,
      id: item.id,
      reason: item.reason ?? 'deleted',
    })),
    authorizedConversationIds: page.authorizedConversationIds,
    conversationCoverage: page.conversationCoverage,
  };
}

function statusOf(error: unknown): number {
  if (typeof error === 'object' && error && 'status' in error) {
    return Number((error as { status: number }).status);
  }
  return 0;
}

function codeOf(error: unknown): string {
  const message = error instanceof Error ? error.message : '';
  return /"error"\s*:\s*"([^"]+)"/.exec(message)?.[1] ?? '';
}

function bodyOf(error: unknown): string | null {
  const message = error instanceof Error ? error.message : '';
  return /"currentBody"\s*:\s*"([^"]*)"/.exec(message)?.[1] ?? null;
}
