import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiService } from '../api/api.service';
import { AuthService } from '../auth/auth.service';
import { ChatHubService } from './chat-hub.service';
import { ChannelStore } from './channel.store';
import { ChatMessage, ChatThread } from '../../shared/models/chat.models';

@Injectable({ providedIn: 'root' })
export class ThreadStore {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly hub = inject(ChatHubService);
  private readonly channels = inject(ChannelStore);

  private readonly activeSignal = signal<ChatThread | null>(null);
  private readonly messagesSignal = signal<ChatMessage[]>([]);
  private readonly loadingSignal = signal(false);
  private readonly sendingSignal = signal(false);
  private readonly openSignal = signal(false);

  readonly active = this.activeSignal.asReadonly();
  readonly messages = this.messagesSignal.asReadonly();
  readonly loading = this.loadingSignal.asReadonly();
  readonly sending = this.sendingSignal.asReadonly();
  readonly open = this.openSignal.asReadonly();
  readonly sortedMessages = computed(() =>
    [...this.messagesSignal()].sort(
      (a, b) => (a.seq ?? 0) - (b.seq ?? 0) || a.createdAt.localeCompare(b.createdAt),
    ),
  );

  constructor() {
    this.hub.onMessage((message) => this.ingestRemote(message));
  }

  async openFromMessage(channelId: string, messageId: string): Promise<void> {
    this.openSignal.set(true);
    this.loadingSignal.set(true);
    try {
      if (this.channels.isDemo() || this.auth.isOfflineDemo()) {
        const demo = this.demoThread(channelId, messageId);
        this.activeSignal.set(demo);
        this.messagesSignal.set([]);
        return;
      }

      const thread = await this.api.openThread(channelId, messageId);
      const detailed = await this.api.getThread(thread.id);
      const replies = await this.api.getThreadMessages(thread.id);
      this.activeSignal.set(detailed);
      this.messagesSignal.set(replies.map((m) => this.normalize(m)));
    } catch {
      this.activeSignal.set(null);
      this.messagesSignal.set([]);
    } finally {
      this.loadingSignal.set(false);
    }
  }

  close(): void {
    this.openSignal.set(false);
    this.activeSignal.set(null);
    this.messagesSignal.set([]);
  }

  bumpReplyCount(threadId: string): void {
    const active = this.activeSignal();
    if (active?.id === threadId) {
      this.activeSignal.set({ ...active, replyCount: (active.replyCount ?? 0) + 1 });
    }
  }

  async send(body: string): Promise<void> {
    const thread = this.activeSignal();
    const profile = this.auth.profile();
    const text = body.trim();
    if (!thread || !text) return;

    const clientMessageId = crypto.randomUUID();
    const idempotencyKey = crypto.randomUUID();
    const optimistic: ChatMessage = {
      id: clientMessageId,
      clientMessageId,
      conversationId: thread.id,
      channelId: thread.channelId,
      authorUserId: profile?.id ?? 'me',
      authorName: profile?.name ?? 'Você',
      body: text,
      createdAt: new Date().toISOString(),
      status: 'sending',
      mine: true,
      threadId: thread.id,
      replyToMessageId: thread.parentMessageId,
    };

    this.messagesSignal.update((list) => [...list, optimistic]);
    this.sendingSignal.set(true);

    try {
      if (this.channels.isDemo() || this.auth.isOfflineDemo()) {
        await new Promise((r) => setTimeout(r, 350));
        this.patchByClientId(clientMessageId, {
          status: 'sent',
          id: crypto.randomUUID(),
          seq: this.messagesSignal().length,
        });
        this.bumpReplyCount(thread.id);
        return;
      }

      const persisted = await this.api.sendThreadMessage({
        threadId: thread.id,
        body: text,
        clientMessageId,
        idempotencyKey,
        replyToMessageId: thread.parentMessageId,
      });

      this.patchByClientId(clientMessageId, {
        ...this.normalize(persisted),
        status: 'persisted',
        mine: true,
        clientMessageId,
      });
      // replyCount is bumped when the outbox/hub event arrives (or by MessageStore)
    } catch {
      this.patchByClientId(clientMessageId, { status: 'failed' });
    } finally {
      this.sendingSignal.set(false);
    }
  }

  private ingestRemote(message: ChatMessage): void {
    const active = this.activeSignal();
    if (!active || !message.threadId || message.threadId !== active.id) return;
    if (message.conversationId === message.channelId) return;

    const normalized = this.normalize(message);
    const mine = normalized.authorUserId === this.auth.profile()?.id;
    if (mine && normalized.clientMessageId) {
      const existing = this.messagesSignal().find(
        (m) => m.clientMessageId === normalized.clientMessageId,
      );
      if (existing) {
        this.patchByClientId(normalized.clientMessageId, {
          ...normalized,
          status: 'persisted',
          mine: true,
        });
        return;
      }
    }

    this.messagesSignal.update((list) => {
      if (list.some((m) => m.id === normalized.id)) return list;
      return [...list, { ...normalized, mine }];
    });
  }

  private patchByClientId(clientMessageId: string, patch: Partial<ChatMessage>): void {
    this.messagesSignal.update((list) =>
      list.map((m) => (m.clientMessageId === clientMessageId ? { ...m, ...patch } : m)),
    );
  }

  private normalize(message: ChatMessage): ChatMessage {
    return {
      ...message,
      channelId: message.channelId || message.conversationId,
      status: message.status ?? 'persisted',
      mine: message.mine ?? message.authorUserId === this.auth.profile()?.id,
      authorName: message.authorName || 'Membro',
      body: message.deletedAt ? '' : message.body,
    };
  }

  private demoThread(channelId: string, messageId: string): ChatThread {
    return {
      id: `thread-${messageId}`,
      channelId,
      parentMessageId: messageId,
      createdBy: this.auth.profile()?.id ?? 'me',
      createdAt: new Date().toISOString(),
      replyCount: 0,
      parentMessage: {
        id: messageId,
        conversationId: channelId,
        channelId,
        authorUserId: 'u-alice',
        authorName: 'Alice Mendes',
        body: 'Mensagem de origem da thread (demo).',
        createdAt: new Date().toISOString(),
        status: 'persisted',
        mine: false,
        threadId: `thread-${messageId}`,
        replyCount: 0,
      },
    };
  }
}
