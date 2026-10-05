import { Injectable, WritableSignal, inject } from '@angular/core';
import { DraftStoreService } from '../../../core/services/draft-store.service';
import { MessageStore } from '../../../core/services/message.store';
import { updateTextareaSelection } from '../../../shared/markdown/markdown-format';
import { AttachmentQueueService } from './attachment-queue.service';
import { AudioRecorderService } from './audio-recorder.service';
import { ComposerMentions } from './composer-mentions';
import { ComposerSlashSession } from './composer-slash-session';
import { ComposerState } from './composer-state';
import { SlashCommandsService } from './slash-commands.service';

@Injectable()
export class ComposerDrafts {
  private readonly drafts = inject(DraftStoreService);
  private readonly state = inject(ComposerState);
  private readonly mentions = inject(ComposerMentions);
  private readonly slashSession = inject(ComposerSlashSession);
  private readonly attachments = inject(AttachmentQueueService);
  private readonly audioRecorder = inject(AudioRecorderService);
  private readonly messages = inject(MessageStore);
  private readonly slash = inject(SlashCommandsService);

  async onActiveChannelChanged(channelId: string | null): Promise<void> {
    const previousId = this.state.boundChannelId;
    if (previousId === channelId) return;

    if (previousId) {
      await this.persistNow(previousId);
    }

    this.state.boundChannelId = channelId;
    this.audioRecorder.reset();
    this.state.validationError.set(null);
    this.slash.clearNotice();
    this.messages.clearReplyTarget();
    this.messages.clearEdit();
    this.state.draftBeforeEdit = null;
    this.mentions.close();
    this.slashSession.close();
    void this.slashSession.ensureCatalog();

    if (!channelId) {
      this.state.restoringDraft = true;
      this.state.draft.set('');
      this.attachments.clear();
      this.state.restoringDraft = false;
      return;
    }

    await this.restore(channelId);
  }

  async restore(channelId: string): Promise<void> {
    this.state.restoringDraft = true;
    try {
      const saved = await this.drafts.get(channelId);
      const body = this.mentions.toComposerDisplay(saved?.body ?? '');
      this.state.draft.set(body);
      this.attachments.restoreReady(saved?.attachments ?? []);
      if (saved && body === saved.body && (saved.selectionStart != null || saved.selectionEnd != null)) {
        queueMicrotask(() => {
          const textarea = this.state.textarea();
          if (!textarea) return;
          const start = saved.selectionStart ?? body.length;
          const end = saved.selectionEnd ?? start;
          updateTextareaSelection(textarea, body, start, end);
        });
      }
    } finally {
      this.state.restoringDraft = false;
    }
  }

  persistSoon(): void {
    const channelId = this.state.boundChannelId;
    if (!channelId || this.state.restoringDraft) return;
    const textarea = this.state.textarea();
    this.drafts.scheduleSave(channelId, {
      body: this.state.draft(),
      attachments: this.attachments.readyAttachmentMetas(),
      selectionStart: textarea?.selectionStart,
      selectionEnd: textarea?.selectionEnd,
    });
  }

  async persistNow(channelId: string): Promise<void> {
    const textarea = this.state.textarea();
    await this.drafts.saveNow(channelId, {
      body: this.state.draft(),
      attachments: this.attachments.readyAttachmentMetas(),
      selectionStart: textarea?.selectionStart,
      selectionEnd: textarea?.selectionEnd,
    });
  }

  restoreAfterEdit(draft: WritableSignal<string>): void {
    const snapshot = this.state.draftBeforeEdit;
    this.state.draftBeforeEdit = null;
    this.state.restoringDraft = true;
    try {
      draft.set(snapshot?.body ?? '');
    } finally {
      this.state.restoringDraft = false;
    }
    if (this.state.boundChannelId) {
      this.persistSoon();
    }
  }
}
