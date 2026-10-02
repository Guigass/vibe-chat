import { Injectable, inject, signal } from '@angular/core';
import { ApiService } from '../api/api.service';
import { translateErrorCode, ui } from '../i18n/strings';
import { ScheduleItem } from '../../shared/models/chat.models';
import { ChannelStore } from './channel.store';
import { ChatHubService } from './chat-hub.service';

export interface RemindTarget {
  messageId: string;
  channelId: string;
  threadId: string | null;
  preview: string;
}

@Injectable({ providedIn: 'root' })
export class ScheduleStore {
  private readonly api = inject(ApiService);
  private readonly channels = inject(ChannelStore);
  private readonly hub = inject(ChatHubService);

  readonly panelOpen = signal(false);
  readonly items = signal<ScheduleItem[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly notice = signal<string | null>(null);
  readonly remindTarget = signal<RemindTarget | null>(null);

  constructor() {
    if (typeof this.hub.onScheduleNotice === 'function') {
      this.hub.onScheduleNotice((event) => {
        if (event.kind === 'reminder') {
          this.notice.set(ui.scheduleDueReminder);
        } else if (event.status === 'membership_revoked') {
          this.notice.set(ui.scheduleDueRevoked);
        } else if (event.status === 'failed') {
          this.notice.set(ui.scheduleDueFailed);
        }
        if (this.panelOpen()) {
          void this.refresh();
        }
      });
    }
  }

  togglePanel(): void {
    this.panelOpen.update((open) => !open);
    if (this.panelOpen()) {
      void this.refresh();
    }
  }

  openPanel(): void {
    this.panelOpen.set(true);
    void this.refresh();
  }

  closePanel(): void {
    this.panelOpen.set(false);
    this.remindTarget.set(null);
  }

  clearNotice(): void {
    this.notice.set(null);
  }

  beginRemind(target: RemindTarget): void {
    this.remindTarget.set(target);
    this.openPanel();
  }

  async refresh(): Promise<void> {
    const workspaceId = this.channels.activeWorkspace()?.id;
    if (!workspaceId) return;
    this.loading.set(true);
    this.error.set(null);
    try {
      const page = await this.api.getSchedule(workspaceId, { limit: 50 });
      this.items.set(page.items);
    } catch {
      this.error.set(ui.scheduleLoadError);
    } finally {
      this.loading.set(false);
    }
  }

  async createScheduled(input: {
    channelId: string;
    body: string;
    sendAtLocal: string;
    timeZone: string;
    idempotencyKey: string;
    replyToMessageId?: string | null;
    threadId?: string | null;
  }): Promise<boolean> {
    this.error.set(null);
    try {
      await this.api.createScheduledMessage(input);
      if (this.panelOpen()) {
        await this.refresh();
      }
      return true;
    } catch (error) {
      this.error.set(messageFrom(error, ui.composerScheduleFailed));
      return false;
    }
  }

  async createReminder(input: {
    remindAtLocal: string;
    timeZone: string;
    note?: string | null;
    targetKind: 'Time' | 'Message' | 'Thread';
    messageId?: string | null;
    threadId?: string | null;
    idempotencyKey: string;
  }): Promise<boolean> {
    const workspaceId = this.channels.activeWorkspace()?.id;
    if (!workspaceId) return false;
    this.error.set(null);
    try {
      await this.api.createReminder({ workspaceId, ...input });
      this.remindTarget.set(null);
      await this.refresh();
      return true;
    } catch (error) {
      this.error.set(messageFrom(error, ui.scheduleRemindFailed));
      return false;
    }
  }

  async updateItem(
    item: ScheduleItem,
    patch: { body?: string; note?: string | null; sendAtLocal?: string; timeZone?: string },
  ): Promise<boolean> {
    const workspaceId = this.channels.activeWorkspace()?.id;
    if (!workspaceId) return false;
    this.error.set(null);
    try {
      if (item.kind === 'scheduled_message') {
        await this.api.updateScheduledMessage(workspaceId, item.id, {
          body: patch.body,
          sendAtLocal: patch.sendAtLocal,
          timeZone: patch.timeZone,
        });
      } else {
        await this.api.updateReminder(workspaceId, item.id, {
          note: patch.note,
          remindAtLocal: patch.sendAtLocal,
          timeZone: patch.timeZone,
        });
      }
      await this.refresh();
      return true;
    } catch (error) {
      this.error.set(messageFrom(error, ui.scheduleEditError));
      return false;
    }
  }

  async cancelItem(item: ScheduleItem): Promise<boolean> {
    const workspaceId = this.channels.activeWorkspace()?.id;
    if (!workspaceId) return false;
    this.error.set(null);
    try {
      if (item.kind === 'scheduled_message') {
        await this.api.cancelScheduledMessage(workspaceId, item.id);
      } else {
        await this.api.cancelReminder(workspaceId, item.id);
      }
      await this.refresh();
      return true;
    } catch (error) {
      this.error.set(messageFrom(error, ui.scheduleCancelError));
      return false;
    }
  }
}

function messageFrom(error: unknown, fallback: string): string {
  if (error instanceof Error && error.message) {
    try {
      const parsed = JSON.parse(error.message) as { error?: string };
      const translated = translateErrorCode(parsed.error);
      if (translated) return translated;
    } catch {
      /* response was not JSON */
    }
  }
  return fallback;
}
