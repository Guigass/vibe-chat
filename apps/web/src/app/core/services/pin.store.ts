import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiService } from '../api/api.service';
import { ChatHubService } from './chat-hub.service';
import { ChannelStore } from './channel.store';
import { MessageStore } from './message.store';
import { PinnedMessageItem } from '../../shared/models/chat.models';

@Injectable({ providedIn: 'root' })
export class PinStore {
  private readonly api = inject(ApiService);
  private readonly hub = inject(ChatHubService);
  private readonly channels = inject(ChannelStore);
  private readonly messages = inject(MessageStore);

  private readonly pinsByChannelSignal = signal<Record<string, PinnedMessageItem[]>>({});
  private readonly loadingSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);
  private readonly panelOpenSignal = signal(false);

  readonly panelOpen = this.panelOpenSignal.asReadonly();
  readonly loading = this.loadingSignal.asReadonly();
  readonly error = this.errorSignal.asReadonly();

  readonly activePins = computed(() => {
    const channelId = this.channels.activeChannelId();
    if (!channelId) return [];
    return this.pinsByChannelSignal()[channelId] ?? [];
  });

  readonly activeCount = computed(() => this.activePins().length);

  readonly pinLimit = computed(() => {
    const channelId = this.channels.activeChannelId();
    if (!channelId) return 20;
    const pins = this.pinsByChannelSignal()[channelId];
    return pins?.[0]?.limit ?? 20;
  });

  readonly pinnedMessageIds = computed(() => {
    const set = new Set<string>();
    for (const pin of this.activePins()) {
      set.add(pin.messageId);
    }
    return set;
  });

  private unsubPinChanged: (() => void) | null = null;

  constructor() {
    this.unsubPinChanged = this.hub.onPinChanged((event) => {
      void this.handlePinChanged(event.channelId, event.messageId, event.pinned);
    });
  }

  openPanel(): void {
    this.panelOpenSignal.set(true);
  }

  closePanel(): void {
    this.panelOpenSignal.set(false);
  }

  togglePanel(): void {
    this.panelOpenSignal.update((open) => !open);
  }

  isPinned(messageId: string): boolean {
    return this.pinnedMessageIds().has(messageId);
  }

  async loadForChannel(channelId: string): Promise<void> {
    if (!channelId || this.channels.isDemo()) {
      this.pinsByChannelSignal.update((current) => ({ ...current, [channelId]: [] }));
      return;
    }

    this.loadingSignal.set(true);
    this.errorSignal.set(null);
    try {
      const page = await this.api.getPins(channelId);
      this.pinsByChannelSignal.update((current) => ({ ...current, [channelId]: page.pins }));
      this.messages.applyPinnedFlags(
        channelId,
        page.pins.map((p) => p.messageId),
      );
    } catch {
      this.errorSignal.set('Não foi possível carregar mensagens fixadas.');
    } finally {
      this.loadingSignal.set(false);
    }
  }

  async pinMessage(channelId: string, messageId: string): Promise<'ok' | 'limit' | 'error'> {
    try {
      const result = await this.api.pinMessage(channelId, messageId);
      await this.handlePinChanged(channelId, messageId, true, result.pinCount);
      return 'ok';
    } catch (err: unknown) {
      const body = (err as { body?: { error?: string } })?.body;
      if (body?.error === 'PinLimitReached') return 'limit';
      return 'error';
    }
  }

  async unpinMessage(channelId: string, messageId: string): Promise<boolean> {
    try {
      await this.api.unpinMessage(channelId, messageId);
      await this.handlePinChanged(channelId, messageId, false);
      return true;
    } catch {
      return false;
    }
  }

  async jumpToPin(pin: PinnedMessageItem): Promise<void> {
    this.channels.selectChannel(pin.channelId);
    this.closePanel();
    await this.messages.jumpToSequence(pin.channelId, pin.sequence, pin.messageId);
  }

  private async handlePinChanged(
    channelId: string,
    messageId: string,
    pinned: boolean,
    knownCount?: number,
  ): Promise<void> {
    this.messages.setPinned(channelId, messageId, pinned);
    if (this.channels.isDemo()) return;

    if (knownCount !== undefined && channelId === this.channels.activeChannelId()) {
      const existing = this.pinsByChannelSignal()[channelId] ?? [];
      if (pinned) {
        if (!existing.some((p) => p.messageId === messageId)) {
          void this.loadForChannel(channelId);
        }
      } else {
        this.pinsByChannelSignal.update((current) => ({
          ...current,
          [channelId]: (current[channelId] ?? []).filter((p) => p.messageId !== messageId),
        }));
      }
      return;
    }

    if (channelId === this.channels.activeChannelId()) {
      void this.loadForChannel(channelId);
    }
  }
}
