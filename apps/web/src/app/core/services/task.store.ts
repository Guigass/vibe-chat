import { Injectable, inject, signal } from '@angular/core';
import { translateErrorCode, ui } from '../i18n/strings';
import {
  TaskApiService,
  TaskItem,
  TaskListState,
  TaskPriority,
  TaskView,
  TaskVisibility,
} from '../api/task-api.service';
import { pokeInbox } from './inbox-refresh';
import { ChannelStore } from './channel.store';
import { MessageStore } from './message.store';
import { ThreadStore } from './thread.store';
import { ChatMessage } from '../../shared/models/chat.models';

export interface TaskDraft {
  messageId: string | null;
  channelId: string;
  idempotencyKey: string;
}

export interface TaskForm {
  messageId: string;
  title: string;
  description: string;
  priority: TaskPriority;
  assigneeUserIds: string[];
  dueAt: string;
  visibility: TaskVisibility;
  checklistText: string;
}

@Injectable({ providedIn: 'root' })
export class TaskStore {
  private readonly api = inject(TaskApiService);
  private readonly channels = inject(ChannelStore);
  private readonly messages = inject(MessageStore);
  private readonly threads = inject(ThreadStore);

  private readonly itemsSignal = signal<TaskItem[]>([]);
  private readonly loadingSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);
  private readonly panelOpenSignal = signal(false);
  private readonly draftSignal = signal<TaskDraft | null>(null);
  private readonly savingSignal = signal(false);
  private readonly saveErrorSignal = signal<string | null>(null);
  private readonly viewSignal = signal<TaskView>('workspace');
  private readonly stateSignal = signal<TaskListState>('active');

  readonly items = this.itemsSignal.asReadonly();
  readonly loading = this.loadingSignal.asReadonly();
  readonly error = this.errorSignal.asReadonly();
  readonly panelOpen = this.panelOpenSignal.asReadonly();
  readonly draft = this.draftSignal.asReadonly();
  readonly saving = this.savingSignal.asReadonly();
  readonly saveError = this.saveErrorSignal.asReadonly();
  readonly viewFilter = this.viewSignal.asReadonly();
  readonly stateFilter = this.stateSignal.asReadonly();

  begin(message: ChatMessage): void {
    this.saveErrorSignal.set(null);
    this.draftSignal.set({
      messageId: message.id,
      channelId: message.channelId,
      idempotencyKey: crypto.randomUUID(),
    });
  }

  beginGlobal(): void {
    const channelId = this.channels.activeChannelId();
    if (!channelId) return;
    this.saveErrorSignal.set(null);
    this.draftSignal.set({
      messageId: null,
      channelId,
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
  }

  setView(view: TaskView): void {
    this.viewSignal.set(view);
    void this.reload();
  }

  setState(state: TaskListState): void {
    this.stateSignal.set(state);
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
      const items = await this.api.list(workspaceId, {
        view: this.viewSignal(),
        state: this.stateSignal(),
        channelId: this.viewSignal() === 'channel' ? this.channels.activeChannelId() : null,
      });
      this.itemsSignal.set(items);
    } catch {
      this.errorSignal.set(ui.taskLoadError);
    } finally {
      this.loadingSignal.set(false);
    }
  }

  async submit(form: TaskForm): Promise<boolean> {
    const draft = this.draftSignal();
    const workspaceId = this.channels.activeWorkspace()?.id;
    if (!draft || !workspaceId || this.channels.isDemo() || !form.messageId) return false;
    this.savingSignal.set(true);
    this.saveErrorSignal.set(null);
    try {
      await this.api.create(workspaceId, {
        idempotencyKey: draft.idempotencyKey,
        messageId: form.messageId,
        title: form.title.trim(),
        description: form.description.trim() || null,
        priority: form.priority,
        assigneeUserIds: form.assigneeUserIds,
        dueAt: form.dueAt ? new Date(form.dueAt).toISOString() : null,
        visibility: form.visibility,
        checklist: checklistFromText(form.checklistText),
      });
      this.draftSignal.set(null);
      pokeInbox();
      if (this.panelOpenSignal()) await this.reload();
      return true;
    } catch (error) {
      const code = readErrorCode(error);
      this.saveErrorSignal.set(code ? translateErrorCode(code) : ui.taskSaveError);
      return false;
    } finally {
      this.savingSignal.set(false);
    }
  }

  async complete(item: TaskItem): Promise<void> {
    await this.transition(item, 'complete');
  }

  async reopen(item: TaskItem): Promise<void> {
    await this.transition(item, 'reopen');
  }

  async toggleChecklist(item: TaskItem, index: number): Promise<void> {
    const workspaceId = this.channels.activeWorkspace()?.id;
    if (!workspaceId) return;
    const checklist = item.checklist.map((row, position) => ({
      text: row.text,
      done: position === index ? !row.done : row.done,
    }));
    try {
      const updated = await this.api.patch(workspaceId, item.id, {
        version: item.version,
        replaceChecklist: true,
        checklist,
      });
      this.itemsSignal.update((items) => items.map((row) => (row.id === item.id ? updated : row)));
    } catch {
      this.errorSignal.set(ui.taskSaveError);
    }
  }

  async openOrigin(item: TaskItem): Promise<void> {
    if (item.sourceRedacted) return;
    this.closePanel();
    if (item.sourceThreadId) {
      await this.channels.selectChannel(item.channelId);
      await this.threads.openById(item.sourceThreadId);
      return;
    }
    await this.messages.jumpToSequence(item.channelId, item.sourceSequence, item.sourceMessageId);
  }

  private async transition(item: TaskItem, action: 'complete' | 'reopen'): Promise<void> {
    const workspaceId = this.channels.activeWorkspace()?.id;
    if (!workspaceId) return;
    try {
      const updated = action === 'complete'
        ? await this.api.complete(workspaceId, item.id, item.version)
        : await this.api.reopen(workspaceId, item.id, item.version);
      this.itemsSignal.update((items) => items.map((row) => (row.id === item.id ? updated : row)));
      pokeInbox();
      if (this.stateSignal() === 'active' && updated.state === 'completed') {
        this.itemsSignal.update((items) => items.filter((row) => row.id !== item.id));
      }
    } catch {
      this.errorSignal.set(ui.taskSaveError);
    }
  }
}

function checklistFromText(value: string): { text: string; done: boolean }[] {
  return value
    .split('\n')
    .map((line) => line.trim())
    .filter((line) => line.length > 0)
    .map((text) => ({ text, done: false }));
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
