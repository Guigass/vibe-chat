import { Component, DestroyRef, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../../core/auth/auth.service';
import { TopicApiService, TopicItem, TopicMessage } from '../../../core/api/topic-api.service';
import { ChannelStore } from '../../../core/services/channel.store';
import { ChatHubService } from '../../../core/services/chat-hub.service';
import { TopicStore } from '../../../core/services/topic.store';
import { fillTemplate, ui } from '../../../core/i18n/strings';
import { idsEqual } from '../../../core/services/message-sync';

@Component({
  selector: 'vc-topic-board',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './topic-board.html',
  styleUrl: './topic-board.css',
})
export class TopicBoard {
  readonly ui = ui;
  readonly channels = inject(ChannelStore);
  readonly auth = inject(AuthService);
  private readonly api = inject(TopicApiService);
  private readonly topics = inject(TopicStore);
  private readonly hub = inject(ChatHubService);
  private readonly destroyRef = inject(DestroyRef);

  readonly items = signal<TopicItem[]>([]);
  readonly canManage = signal(false);
  readonly filter = signal('recent');
  readonly active = signal<TopicItem | null>(null);
  readonly messages = signal<TopicMessage[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly draftTitle = signal('');
  readonly draftTags = signal('');
  readonly draftBody = signal('');
  readonly activeIndex = signal(0);

  constructor() {
    effect(() => {
      const channelId = this.channels.activeChannelId();
      const pending = this.topics.pendingId();
      if (!channelId || this.channels.activeChannel()?.layout !== 'Topics') {
        this.items.set([]);
        this.active.set(null);
        return;
      }
      void this.reload(channelId, pending);
    });

    const stop = this.hub.onMessage((message) => {
      const current = this.active();
      if (!current || !idsEqual(message.conversationId, current.id)) return;
      this.messages.update((rows) => {
        if (rows.some((row) => row.id === message.id)) return rows;
        return [...rows, {
          id: message.id,
          topicId: current.id,
          channelId: message.channelId,
          sequence: message.seq ?? rows.length + 1,
          authorId: message.authorUserId,
          authorName: message.authorName,
          body: message.body,
          createdAt: message.createdAt,
        }];
      });
    });
    this.destroyRef.onDestroy(stop);
  }

  destination(topic: TopicItem | null): string {
    return fillTemplate(ui.topicsDestination, { name: topic?.title ?? '' });
  }

  async reload(channelId = this.channels.activeChannelId(), pending: string | null = null): Promise<void> {
    if (!channelId) return;
    this.loading.set(true);
    this.error.set(null);
    try {
      const page = await this.api.list(channelId, this.filter());
      this.items.set(page.items ?? []);
      this.canManage.set(!!page.canManage);
      const openId = pending ?? this.active()?.id;
      if (openId) {
        const match = (page.items ?? []).find((item) => item.id === openId);
        if (match) await this.open(match);
        if (pending) this.topics.pendingId.set(null);
      }
    } catch {
      this.error.set(ui.topicsLoadError);
    } finally {
      this.loading.set(false);
    }
  }

  async create(): Promise<void> {
    const channelId = this.channels.activeChannelId();
    const title = this.draftTitle().trim();
    if (!channelId || !title) return;
    const tags = this.draftTags().split(',').map((tag) => tag.trim()).filter(Boolean);
    try {
      const created = await this.api.create(channelId, title, tags);
      this.draftTitle.set('');
      this.draftTags.set('');
      await this.reload(channelId);
      const match = this.items().find((item) => item.id === created.id) ?? created;
      await this.open(match);
    } catch {
      this.error.set(ui.topicsActionError);
    }
  }

  async open(topic: TopicItem): Promise<void> {
    this.active.set(topic);
    this.error.set(null);
    try {
      const rows = await this.api.messages(topic.id);
      this.messages.set(rows ?? []);
      const last = rows?.at(-1)?.sequence ?? 0;
      if (last > 0) await this.api.read(topic.id, last);
    } catch {
      this.error.set(ui.topicsLoadError);
    }
  }

  back(): void {
    this.active.set(null);
    this.messages.set([]);
    this.draftBody.set('');
  }

  async toggleFollow(topic: TopicItem): Promise<void> {
    try {
      await this.api.follow(topic.id, !topic.following);
      await this.reload();
    } catch {
      this.error.set(ui.topicsActionError);
    }
  }

  async toggleClosed(topic: TopicItem): Promise<void> {
    try {
      await this.api.close(topic.id, topic.state !== 'Closed');
      await this.reload();
      const next = this.items().find((item) => item.id === topic.id);
      if (next) this.active.set(next);
    } catch {
      this.error.set(ui.topicsActionError);
    }
  }

  async send(): Promise<void> {
    const topic = this.active();
    const body = this.draftBody().trim();
    if (!topic || !body || !topic.canPost) return;
    try {
      const message = await this.api.send(topic.id, body);
      this.draftBody.set('');
      this.messages.update((rows) => rows.some((row) => row.id === message.id) ? rows : [...rows, message]);
    } catch {
      this.error.set(ui.topicsActionError);
    }
  }

  onListKey(event: KeyboardEvent): void {
    const rows = this.items();
    if (!rows.length) return;
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault();
      const delta = event.key === 'ArrowDown' ? 1 : -1;
      const next = (this.activeIndex() + delta + rows.length) % rows.length;
      this.activeIndex.set(next);
      return;
    }
    if (event.key === 'Enter') {
      event.preventDefault();
      const topic = rows[this.activeIndex()];
      if (topic) void this.open(topic);
    }
  }

  setFilter(value: string): void {
    this.filter.set(value);
    void this.reload();
  }

  mine(message: TopicMessage): boolean {
    return idsEqual(message.authorId, this.auth.profile()?.id);
  }
}
