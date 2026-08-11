import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiService } from '../api/api.service';
import { ChannelStore } from './channel.store';
import { MessageStore } from './message.store';
import { SavedMessageItem } from '../../shared/models/chat.models';

@Injectable({ providedIn: 'root' })
export class SavedStore {
  private readonly api = inject(ApiService);
  private readonly channels = inject(ChannelStore);
  private readonly messages = inject(MessageStore);

  private readonly itemsSignal = signal<SavedMessageItem[]>([]);
  private readonly pendingCountSignal = signal(0);
  private readonly loadingSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);
  private readonly panelOpenSignal = signal(false);
  private readonly showCompletedSignal = signal(false);
  private readonly savedIdsSignal = signal<Set<string>>(new Set());

  readonly panelOpen = this.panelOpenSignal.asReadonly();
  readonly loading = this.loadingSignal.asReadonly();
  readonly error = this.errorSignal.asReadonly();
  readonly items = this.itemsSignal.asReadonly();
  readonly pendingCount = this.pendingCountSignal.asReadonly();
  readonly showCompleted = this.showCompletedSignal.asReadonly();
  readonly savedMessageIds = computed(() => this.savedIdsSignal());

  openPanel(): void {
    this.panelOpenSignal.set(true);
    void this.reload();
  }

  closePanel(): void {
    this.panelOpenSignal.set(false);
  }

  togglePanel(): void {
    if (this.panelOpenSignal()) {
      this.closePanel();
    } else {
      this.openPanel();
    }
  }

  setShowCompleted(value: boolean): void {
    this.showCompletedSignal.set(value);
    void this.reload();
  }

  isSaved(messageId: string): boolean {
    return this.savedIdsSignal().has(messageId);
  }

  async loadForWorkspace(workspaceId: string | null | undefined): Promise<void> {
    if (!workspaceId || this.channels.isDemo()) {
      this.itemsSignal.set([]);
      this.pendingCountSignal.set(0);
      this.savedIdsSignal.set(new Set());
      this.messages.applySavedFlags([]);
      return;
    }

    this.loadingSignal.set(true);
    this.errorSignal.set(null);
    try {
      const [openPage, completedPage] = await Promise.all([
        this.api.getSavedMessages(workspaceId, { completed: false, limit: 100 }),
        this.api.getSavedMessages(workspaceId, { completed: true, limit: 100 }),
      ]);
      const ids = new Set<string>([
        ...openPage.items.map((i) => i.messageId),
        ...completedPage.items.map((i) => i.messageId),
      ]);
      this.savedIdsSignal.set(ids);
      this.pendingCountSignal.set(openPage.pendingCount);
      this.messages.applySavedFlags([...ids]);
      if (this.panelOpenSignal()) {
        this.itemsSignal.set(this.showCompletedSignal() ? completedPage.items : openPage.items);
      }
    } catch {
      this.errorSignal.set('Não foi possível carregar salvos.');
    } finally {
      this.loadingSignal.set(false);
    }
  }

  async reload(): Promise<void> {
    const workspaceId = this.channels.activeWorkspace()?.id;
    if (!workspaceId || this.channels.isDemo()) {
      this.itemsSignal.set([]);
      return;
    }

    this.loadingSignal.set(true);
    this.errorSignal.set(null);
    try {
      const page = await this.api.getSavedMessages(workspaceId, {
        completed: this.showCompletedSignal(),
        limit: 50,
      });
      this.itemsSignal.set(page.items);
      this.pendingCountSignal.set(page.pendingCount);
      const ids = new Set(this.savedIdsSignal());
      for (const item of page.items) {
        ids.add(item.messageId);
      }
      this.savedIdsSignal.set(ids);
      this.messages.applySavedFlags([...ids]);
    } catch {
      this.errorSignal.set('Não foi possível carregar salvos.');
    } finally {
      this.loadingSignal.set(false);
    }
  }

  async saveMessage(messageId: string, note?: string | null): Promise<boolean> {
    const workspaceId = this.channels.activeWorkspace()?.id;
    if (!workspaceId || this.channels.isDemo()) return false;
    try {
      await this.api.saveMessage(workspaceId, messageId, note);
      this.savedIdsSignal.update((current) => new Set(current).add(messageId));
      this.messages.setSaved(messageId, true);
      await this.loadForWorkspace(workspaceId);
      return true;
    } catch {
      return false;
    }
  }

  async unsaveMessage(messageId: string): Promise<boolean> {
    const workspaceId = this.channels.activeWorkspace()?.id;
    if (!workspaceId || this.channels.isDemo()) return false;
    try {
      await this.api.unsaveMessage(workspaceId, messageId);
      this.savedIdsSignal.update((current) => {
        const next = new Set(current);
        next.delete(messageId);
        return next;
      });
      this.messages.setSaved(messageId, false);
      this.itemsSignal.update((items) => items.filter((i) => i.messageId !== messageId));
      await this.loadForWorkspace(workspaceId);
      return true;
    } catch {
      return false;
    }
  }

  async toggleComplete(item: SavedMessageItem): Promise<void> {
    const workspaceId = this.channels.activeWorkspace()?.id;
    if (!workspaceId) return;
    const completed = !item.completedAt;
    try {
      await this.api.patchSavedMessage(workspaceId, item.messageId, { completed });
      await this.loadForWorkspace(workspaceId);
      if (this.panelOpenSignal()) {
        await this.reload();
      }
    } catch {
      this.errorSignal.set('Não foi possível atualizar o salvo.');
    }
  }

  async updateNote(item: SavedMessageItem, note: string): Promise<void> {
    const workspaceId = this.channels.activeWorkspace()?.id;
    if (!workspaceId) return;
    try {
      await this.api.patchSavedMessage(workspaceId, item.messageId, { note });
      if (this.panelOpenSignal()) {
        await this.reload();
      }
    } catch {
      this.errorSignal.set('Não foi possível salvar a nota.');
    }
  }

  async jumpToSaved(item: SavedMessageItem): Promise<void> {
    if (item.messageRemoved) return;
    await this.channels.selectChannel(item.channelId);
    await this.messages.jumpToSequence(item.channelId, item.sequence, item.messageId);
    this.closePanel();
  }
}
