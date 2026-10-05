import { Injectable, inject } from '@angular/core';
import { ui } from '../../../core/i18n/strings';
import { ChannelStore } from '../../../core/services/channel.store';
import { DraftStoreService } from '../../../core/services/draft-store.service';
import { MessageStore } from '../../../core/services/message.store';
import { isMessageBodyTooLong } from '../../../shared/models/chat.models';
import { looksLikeSlashCommand } from '../../../shared/markdown/slash-tokens';
import { AttachmentQueueService } from './attachment-queue.service';
import { AudioRecorderService } from './audio-recorder.service';
import { isAnnouncementReadOnly, isCanPublishAnnouncement } from './composer-announcement';
import { ComposerDrafts } from './composer-drafts';
import { ComposerMentions } from './composer-mentions';
import { ComposerSchedule } from './composer-schedule';
import { ComposerSlashSession } from './composer-slash-session';
import { ComposerState } from './composer-state';
import { SlashCommandsService } from './slash-commands.service';

@Injectable()
export class ComposerSubmit {
  private readonly state = inject(ComposerState);
  private readonly mentions = inject(ComposerMentions);
  private readonly slashSession = inject(ComposerSlashSession);
  private readonly scheduler = inject(ComposerSchedule);
  private readonly draftSession = inject(ComposerDrafts);
  private readonly draftStore = inject(DraftStoreService);
  private readonly attachments = inject(AttachmentQueueService);
  private readonly audioRecorder = inject(AudioRecorderService);
  private readonly messages = inject(MessageStore);
  private readonly channels = inject(ChannelStore);
  private readonly slash = inject(SlashCommandsService);

  async onSubmit(event: Event): Promise<void> {
    event.preventDefault();
    if (isAnnouncementReadOnly(this.channels)) return;
    if (this.state.submitting() || this.state.sendingAudio()) return;
    if (this.mentions.open() || this.slashSession.open()) return;

    const editing = this.messages.editingMessage();
    if (editing) {
      const body = this.mentions.toSendBody(this.state.draft());
      if (!body || isMessageBodyTooLong(body)) return;
      this.state.submitting.set(true);
      try {
        await this.messages.edit(editing.id, body);
        this.draftSession.restoreAfterEdit(this.state.draft);
        this.state.validationError.set(null);
        this.slash.clearNotice();
      } finally {
        this.state.submitting.set(false);
      }
      return;
    }

    const phase = this.audioRecorder.phase();
    if (phase === 'recording' || phase === 'preview') {
      this.state.sendingAudio.set(true);
      try {
        if (phase === 'recording') {
          const recorded = await this.audioRecorder.stop();
          if (!recorded) {
            this.state.validationError.set(this.audioRecorder.errorMessage() ?? ui.composerAudioFinishError);
            return;
          }
        }
        await this.sendRecording();
      } finally {
        this.state.sendingAudio.set(false);
      }
      return;
    }

    const display = this.state.draft().trim();
    if (this.scheduler.mode()) {
      await this.scheduler.commit(display);
      return;
    }
    if (looksLikeSlashCommand(display)) {
      await this.slashSession.run(display);
      return;
    }

    const body = this.mentions.toSendBody(display);
    if (isMessageBodyTooLong(body)) return;

    this.state.submitting.set(true);
    try {
      const attachmentIds = await this.attachments.waitForReady();
      if (!body && attachmentIds.length === 0) return;

      const channelId = this.state.boundChannelId;
      this.state.draft.set('');
      this.attachments.clear();
      this.state.validationError.set(null);
      this.slash.clearNotice();
      if (channelId) {
        await this.draftStore.remove(channelId);
      }

      const announcement = this.announcementDraft();
      const ok = announcement
        ? await this.messages.send(body, attachmentIds, announcement)
        : await this.messages.send(body, attachmentIds);
      if (!ok) {
        this.state.draft.set(display);
        this.draftSession.persistSoon();
      }
    } finally {
      this.state.submitting.set(false);
    }
  }

  private async sendRecording(): Promise<void> {
    const channelId = this.channels.activeChannel()?.id;
    if (!channelId) return;

    const recorded = await this.audioRecorder.buildRecordedAudio();
    if (!recorded) {
      this.state.validationError.set(this.audioRecorder.errorMessage() ?? ui.composerAudioPrepareError);
      return;
    }

    const result = await this.attachments.uploadRecordedAudio(channelId, recorded);
    if (result.error) {
      this.state.validationError.set(result.error);
      return;
    }

    const ok = await this.messages.send('', result.attachmentId ? [result.attachmentId] : []);
    if (ok) {
      this.audioRecorder.reset();
      this.attachments.clear();
      this.state.validationError.set(null);
      await this.draftStore.remove(channelId);
      return;
    }

    this.state.validationError.set(ui.composerAudioSendError);
  }

  private announcementDraft():
    | { requiresAcknowledgement: boolean; acknowledgeBy?: string | null }
    | undefined {
    if (!isCanPublishAnnouncement(this.channels, this.messages) || !this.state.requireAck()) {
      return undefined;
    }
    const local = this.state.ackByLocal().trim();
    const acknowledgeBy = local ? new Date(local).toISOString() : null;
    return { requiresAcknowledgement: true, acknowledgeBy };
  }
}
