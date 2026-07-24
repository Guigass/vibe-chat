import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiService } from '../api/api.service';
import { ChatHubService } from './chat-hub.service';
import { AuthService } from '../auth/auth.service';
import { ChannelStore } from './channel.store';
import { ChatMessage } from '../../shared/models/chat.models';

@Injectable({ providedIn: 'root' })
export class MessageStore {
  private readonly api = inject(ApiService);
  private readonly hub = inject(ChatHubService);
  private readonly auth = inject(AuthService);
  private readonly channels = inject(ChannelStore);

  private readonly messagesSignal = signal<ChatMessage[]>([]);
  private readonly loadingSignal = signal(false);
  private readonly sendingSignal = signal(false);
  private unsubCreated: (() => void) | null = null;
  private unsubEdited: (() => void) | null = null;
  private unsubDeleted: (() => void) | null = null;

  readonly messages = this.messagesSignal.asReadonly();
  readonly loading = this.loadingSignal.asReadonly();
  readonly sending = this.sendingSignal.asReadonly();
  readonly forActiveChannel = computed(() => {
    const channelId = this.channels.activeChannel()?.id;
    if (!channelId) return [];
    return this.messagesSignal()
      .filter((m) => m.channelId === channelId)
      .sort((a, b) => (a.seq ?? 0) - (b.seq ?? 0) || a.createdAt.localeCompare(b.createdAt));
  });

  constructor() {
    this.unsubCreated = this.hub.onMessage((message) => this.ingestRemote(message));
    this.unsubEdited = this.hub.onMessageEdited((patch) => this.applyEdit(patch));
    this.unsubDeleted = this.hub.onMessageDeleted((patch) => this.applyDelete(patch));
  }

  async loadChannel(channelId: string): Promise<void> {
    this.loadingSignal.set(true);
    try {
      await this.hub.joinChannel(channelId);
      if (this.channels.isDemo()) {
        this.messagesSignal.set(this.demoMessages(channelId));
      } else {
        const messages = await this.api.getMessages(channelId);
        this.messagesSignal.update((current) => {
          const others = current.filter((m) => m.channelId !== channelId);
          return [...others, ...messages.map((m) => this.normalize(m))];
        });
      }
    } catch {
      this.messagesSignal.update((current) => {
        const others = current.filter((m) => m.channelId !== channelId);
        return [...others, ...this.demoMessages(channelId)];
      });
    } finally {
      this.loadingSignal.set(false);
    }
  }

  async send(body: string, file?: File): Promise<void> {
    const channel = this.channels.activeChannel();
    const profile = this.auth.profile();
    const text = body.trim();
    if (!channel || (!text && !file)) return;

    const clientMessageId = crypto.randomUUID();
    const idempotencyKey = crypto.randomUUID();
    const optimistic: ChatMessage = {
      id: clientMessageId,
      clientMessageId,
      conversationId: channel.id,
      channelId: channel.id,
      authorUserId: profile?.id ?? 'me',
      authorName: profile?.name ?? 'Você',
      body: text,
      createdAt: new Date().toISOString(),
      status: 'sending',
      mine: true,
      attachments: file
        ? [
            {
              id: clientMessageId,
              fileName: file.name,
              contentType: file.type || 'application/octet-stream',
              sizeBytes: file.size,
              status: 'PendingUpload',
            },
          ]
        : [],
    };

    this.messagesSignal.update((list) => [...list, optimistic]);
    this.sendingSignal.set(true);

    try {
      if (this.channels.isDemo() || this.auth.isOfflineDemo()) {
        await new Promise((r) => setTimeout(r, 450));
        this.patchByClientId(clientMessageId, {
          status: 'sent',
          id: crypto.randomUUID(),
          attachments: file
            ? [
                {
                  id: crypto.randomUUID(),
                  fileName: file.name,
                  contentType: file.type || 'application/octet-stream',
                  sizeBytes: file.size,
                  status: 'Ready',
                },
              ]
            : [],
        });
        return;
      }

      const attachmentIds: string[] = [];
      if (file) {
        const initiated = await this.api.initiateAttachmentUpload({
          channelId: channel.id,
          fileName: file.name,
          contentType: file.type || 'application/octet-stream',
          sizeBytes: file.size,
        });
        await this.api.uploadFileToPresignedUrl(
          initiated.uploadUrl,
          file,
          initiated.requiredHeaders ?? {},
        );
        const ready = await this.api.completeAttachmentUpload(channel.id, initiated.attachmentId);
        attachmentIds.push(ready.id);
      }

      const persisted = await this.api.sendMessage({
        channelId: channel.id,
        body: optimistic.body,
        clientMessageId,
        idempotencyKey,
        attachmentIds,
      });

      this.patchByClientId(clientMessageId, {
        ...this.normalize(persisted),
        status: 'persisted',
        mine: true,
        clientMessageId,
      });
    } catch {
      this.patchByClientId(clientMessageId, { status: 'failed' });
    } finally {
      this.sendingSignal.set(false);
    }
  }

  async edit(messageId: string, body: string): Promise<void> {
    const channel = this.channels.activeChannel();
    if (!channel || !body.trim()) return;

    if (this.channels.isDemo() || this.auth.isOfflineDemo()) {
      this.messagesSignal.update((list) =>
        list.map((m) =>
          m.id === messageId
            ? { ...m, body: body.trim(), editedAt: new Date().toISOString() }
            : m,
        ),
      );
      return;
    }

    const updated = await this.api.editMessage(channel.id, messageId, body.trim());
    this.applyEdit({
      id: updated.id,
      channelId: updated.channelId,
      body: updated.body,
      editedAt: updated.editedAt ?? new Date().toISOString(),
      seq: updated.seq,
    });
  }

  async remove(messageId: string): Promise<void> {
    const channel = this.channels.activeChannel();
    if (!channel) return;

    if (this.channels.isDemo() || this.auth.isOfflineDemo()) {
      this.applyDelete({
        id: messageId,
        channelId: channel.id,
        deletedAt: new Date().toISOString(),
      });
      return;
    }

    await this.api.deleteMessage(channel.id, messageId);
    this.applyDelete({
      id: messageId,
      channelId: channel.id,
      deletedAt: new Date().toISOString(),
    });
  }

  private ingestRemote(message: ChatMessage): void {
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

    if (!mine) {
      this.channels.bumpUnread(normalized.channelId);
    }
  }

  private applyEdit(patch: {
    id: string;
    channelId: string;
    body: string;
    editedAt: string;
    seq?: number;
  }): void {
    this.messagesSignal.update((list) =>
      list.map((m) =>
        m.id === patch.id
          ? {
              ...m,
              body: patch.body,
              editedAt: patch.editedAt,
              seq: patch.seq ?? m.seq,
              deletedAt: null,
            }
          : m,
      ),
    );
  }

  private applyDelete(patch: { id: string; channelId: string; deletedAt: string; seq?: number }): void {
    this.messagesSignal.update((list) =>
      list.map((m) =>
        m.id === patch.id
          ? {
              ...m,
              body: '',
              deletedAt: patch.deletedAt,
              seq: patch.seq ?? m.seq,
            }
          : m,
      ),
    );
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

  private demoMessages(channelId: string): ChatMessage[] {
    const now = Date.now();
    return [
      {
        id: `${channelId}-1`,
        conversationId: channelId,
        channelId,
        authorUserId: 'u-alice',
        authorName: 'Alice Mendes',
        body: 'Maré baixa no outbox — boa janela para deploy.',
        createdAt: new Date(now - 1000 * 60 * 42).toISOString(),
        status: 'persisted',
        mine: false,
        seq: 1,
      },
      {
        id: `${channelId}-2`,
        conversationId: channelId,
        channelId,
        authorUserId: 'u-bob',
        authorName: 'Bob Costa',
        body: 'SignalR reconectou limpo. Mantemos o banner só em reconnecting.',
        createdAt: new Date(now - 1000 * 60 * 18).toISOString(),
        status: 'persisted',
        mine: false,
        seq: 2,
      },
    ];
  }
}
