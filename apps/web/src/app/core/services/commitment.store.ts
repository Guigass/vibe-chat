import { Injectable, inject, signal } from '@angular/core';
import { translateErrorCode, ui } from '../i18n/strings';
import {
  CommitmentApiService,
  CommitmentChange,
  CommitmentItem,
  CommitmentKind,
  CommitmentState,
  CommitmentVisibility,
} from '../api/commitment-api.service';
import { pokeInbox } from './inbox-refresh';
import { AuthService } from '../auth/auth.service';
import { ChannelStore } from './channel.store';
import { MessageStore } from './message.store';
import { ThreadStore } from './thread.store';
import { ChatMessage } from '../../shared/models/chat.models';

export interface CommitmentDraft {
  messageId: string;
  channelId: string;
  kind: CommitmentKind;
  idempotencyKey: string;
}

export interface CommitmentForm {
  title: string;
  description: string;
  ownerUserId: string;
  dueAt: string;
  visibility: CommitmentVisibility;
}

@Injectable({ providedIn: 'root' })
export class CommitmentStore {
  private readonly api = inject(CommitmentApiService);
  private readonly auth = inject(AuthService);
  private readonly channels = inject(ChannelStore);
  private readonly messages = inject(MessageStore);
  private readonly threads = inject(ThreadStore);

  private readonly itemsSignal = signal<CommitmentItem[]>([]);
  private readonly historySignal = signal<CommitmentChange[]>([]);
  private readonly selectedIdSignal = signal<string | null>(null);
  private readonly loadingSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);
  private readonly panelOpenSignal = signal(false);
  private readonly draftSignal = signal<CommitmentDraft | null>(null);
  private readonly savingSignal = signal(false);
  private readonly saveErrorSignal = signal<string | null>(null);
  private readonly kindSignal = signal<CommitmentKind | 'all'>('all');
  private readonly stateSignal = signal<CommitmentState | 'all'>('open');
  private readonly scopeSignal = signal<'workspace' | 'channel' | 'mine'>('workspace');

  readonly items = this.itemsSignal.asReadonly();
  readonly history = this.historySignal.asReadonly();
  readonly selectedId = this.selectedIdSignal.asReadonly();
  readonly loading = this.loadingSignal.asReadonly();
  readonly error = this.errorSignal.asReadonly();
  readonly panelOpen = this.panelOpenSignal.asReadonly();
  readonly draft = this.draftSignal.asReadonly();
  readonly saving = this.savingSignal.asReadonly();
  readonly saveError = this.saveErrorSignal.asReadonly();
  readonly kindFilter = this.kindSignal.asReadonly();
  readonly stateFilter = this.stateSignal.asReadonly();
  readonly scopeFilter = this.scopeSignal.asReadonly();

  begin(message: ChatMessage, kind: CommitmentKind): void {
    this.saveErrorSignal.set(null);
    this.draftSignal.set({
      messageId: message.id,
      channelId: message.channelId,
      kind,
      idempotencyKey: crypto.randomUUID(),
    });
  }

  cancelDraft(): void {
    this.draftSignal.set(null);
    this.saveErrorSignal.set(null);
  }

  openPanel(): void {
    this.panelOpenSignal.set(true);
    void this.reload();
  }

  closePanel(): void {
    this.panelOpenSignal.set(false);
    this.selectedIdSignal.set(null);
    this.historySignal.set([]);
  }

  setKind(kind: CommitmentKind | 'all'): void {
    this.kindSignal.set(kind);
    void this.reload();
  }

  setState(state: CommitmentState | 'all'): void {
    this.stateSignal.set(state);
    void this.reload();
  }

  setScope(scope: 'workspace' | 'channel' | 'mine'): void {
    this.scopeSignal.set(scope);
    void this.reload();
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
      const kind = this.kindSignal();
      const items = await this.api.list(workspaceId, {
        channelId: this.scopeSignal() === 'channel' ? this.channels.activeChannelId() : null,
        ownerId: this.scopeSignal() === 'mine' ? this.auth.profile()?.id ?? null : null,
        kind: kind === 'all' ? null : kind,
        state: this.stateSignal(),
      });
      this.itemsSignal.set(items);
    } catch {
      this.errorSignal.set(ui.commitmentLoadError);
    } finally {
      this.loadingSignal.set(false);
    }
  }

  async select(id: string): Promise<void> {
    const workspaceId = this.channels.activeWorkspace()?.id;
    this.selectedIdSignal.set(id);
    if (!workspaceId) return;
    try {
      this.historySignal.set(await this.api.history(workspaceId, id));
    } catch {
      this.historySignal.set([]);
    }
  }

  async submit(form: CommitmentForm): Promise<boolean> {
    const draft = this.draftSignal();
    const workspaceId = this.channels.activeWorkspace()?.id;
    if (!draft || !workspaceId || this.channels.isDemo()) return false;
    this.savingSignal.set(true);
    this.saveErrorSignal.set(null);
    try {
      await this.api.create(workspaceId, {
        idempotencyKey: draft.idempotencyKey,
        messageId: draft.messageId,
        kind: draft.kind,
        title: form.title.trim(),
        description: form.description.trim() || null,
        ownerUserId: form.ownerUserId || null,
        dueAt: form.dueAt ? new Date(form.dueAt).toISOString() : null,
        visibility: form.visibility,
      });
      this.draftSignal.set(null);
      pokeInbox();
      if (this.panelOpenSignal()) await this.reload();
      return true;
    } catch (error) {
      const code = readErrorCode(error);
      this.saveErrorSignal.set(code ? translateErrorCode(code) : ui.commitmentSaveError);
      return false;
    } finally {
      this.savingSignal.set(false);
    }
  }

  async closeItem(id: string): Promise<void> {
    await this.transition(id, 'close');
  }

  async reopenItem(id: string): Promise<void> {
    await this.transition(id, 'reopen');
  }

  async openOrigin(item: CommitmentItem): Promise<void> {
    if (item.sourceRedacted) return;
    this.closePanel();
    if (item.sourceThreadId) {
      await this.channels.selectChannel(item.channelId);
      await this.threads.openById(item.sourceThreadId);
      return;
    }
    await this.messages.jumpToSequence(item.channelId, item.sourceSequence, item.sourceMessageId);
  }

  private async transition(id: string, action: 'close' | 'reopen'): Promise<void> {
    const workspaceId = this.channels.activeWorkspace()?.id;
    if (!workspaceId) return;
    try {
      const updated = action === 'close'
        ? await this.api.close(workspaceId, id)
        : await this.api.reopen(workspaceId, id);
      this.itemsSignal.update((items) => items.map((item) => (item.id === id ? updated : item)));
      pokeInbox();
      await this.select(id);
    } catch {
      this.errorSignal.set(ui.commitmentSaveError);
    }
  }
}

function readErrorCode(error: unknown): string | null {
  if (!(error instanceof Error) || !error.message) return null;
  try {
    const parsed = JSON.parse(error.message) as { error?: string };
    return parsed.error ?? null;
  } catch {
    return null;
  }
}
