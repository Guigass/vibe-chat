import { Injectable, computed, inject, signal } from '@angular/core';
import { fillTemplate, ui } from '../../../core/i18n/strings';
import { DraftStoreService } from '../../../core/services/draft-store.service';
import { MessageStore } from '../../../core/services/message.store';
import { ScheduleStore } from '../../../core/services/schedule.store';
import { isMessageBodyTooLong } from '../../../shared/models/chat.models';
import { AttachmentQueueService } from './attachment-queue.service';
import { ComposerMentions } from './composer-mentions';
import { ComposerState } from './composer-state';

export function browserTimeZone(): string {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC';
  } catch {
    return 'UTC';
  }
}

@Injectable()
export class ComposerSchedule {
  private readonly schedule = inject(ScheduleStore);
  private readonly state = inject(ComposerState);
  private readonly mentions = inject(ComposerMentions);
  private readonly messages = inject(MessageStore);
  private readonly attachments = inject(AttachmentQueueService);
  private readonly drafts = inject(DraftStoreService);

  readonly mode = signal(false);
  readonly at = signal('');
  readonly zone = signal(browserTimeZone());
  readonly hint = computed(() =>
    this.at()
      ? fillTemplate(ui.composerScheduleDelay, {
          when: this.at().replace('T', ' '),
          zone: this.zone(),
        })
      : ui.composerTimeZone,
  );
  private scheduleKey: string | null = null;

  toggle(): void {
    this.mode.update((open) => !open);
  }

  inputValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  async commit(display: string): Promise<void> {
    const channelId = this.state.boundChannelId;
    const body = this.mentions.toSendBody(display);
    if (!channelId || !body || isMessageBodyTooLong(body)) return;
    if (this.attachments.items().length > 0) {
      this.state.validationError.set(ui.composerScheduleNoAttachments);
      return;
    }
    if (!this.at()) {
      this.state.validationError.set(ui.errorInvalidScheduleTime);
      return;
    }

    this.state.submitting.set(true);
    try {
      this.scheduleKey ??= crypto.randomUUID();
      const local = this.at().length === 16 ? `${this.at()}:00` : this.at();
      const ok = await this.schedule.createScheduled({
        channelId,
        body,
        sendAtLocal: local,
        timeZone: this.zone(),
        idempotencyKey: this.scheduleKey,
        replyToMessageId: this.messages.replyTarget()?.id ?? null,
      });
      if (!ok) {
        this.state.validationError.set(this.schedule.error() ?? ui.composerScheduleFailed);
        return;
      }
      this.scheduleKey = null;
      this.state.draft.set('');
      this.mode.set(false);
      this.state.validationError.set(null);
      await this.drafts.remove(channelId);
    } finally {
      this.state.submitting.set(false);
    }
  }
}
