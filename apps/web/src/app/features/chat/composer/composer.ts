import { Component, computed, effect, inject, untracked, viewChild } from '@angular/core';
import { Button, IconButton, Textarea } from '../../../shared/ui';
import { ui } from '../../../core/i18n/strings';
import { MessageStore } from '../../../core/services/message.store';
import { ChatHubService } from '../../../core/services/chat-hub.service';
import { ChannelStore } from '../../../core/services/channel.store';
import { MemberProfileStore } from '../../../core/services/member-profile.store';
import {
  MESSAGE_BODY_COUNTER_THRESHOLD,
  MESSAGE_BODY_MAX_LENGTH,
  isMessageBodyTooLong,
  measureMessageBodyLength,
} from '../../../shared/models/chat.models';
import { MentionAutocompleteItem } from '../../../shared/markdown/mention-tokens';
import { SlashCommandDef } from '../../../shared/markdown/slash-tokens';
import { AttachmentQueueService } from './attachment-queue.service';
import { collectFilesFromClipboard } from './attachment-upload';
import { AudioRecorderService } from './audio-recorder.service';
import { MentionAutocomplete } from './mention-autocomplete';
import { SlashAutocomplete } from './slash-autocomplete';
import { SlashCommandsService } from './slash-commands.service';
import { CommandPaletteService } from '../../../core/services/command-palette.service';
import { SchedulePanel } from '../schedule-panel/schedule-panel';
import { ScheduleStore } from '../../../core/services/schedule.store';
import { isAnnouncementReadOnly, isCanPublishAnnouncement } from './composer-announcement';
import { ComposerAnnouncementAck } from './composer-announcement-ack';
import { ComposerAttachmentList } from './composer-attachment-list';
import { ComposerAudioControls } from './composer-audio-controls';
import { ComposerContextBar } from './composer-context-bar';
import { ComposerDrafts } from './composer-drafts';
import { ComposerFormatKind, applyComposerFormat, insertComposerEmoji } from './composer-format';
import { ComposerFormatToolbar } from './composer-format-toolbar';
import { ComposerKeyAction, decideComposerMenuKey, decideComposerSubmitKey } from './composer-keyboard';
import { ComposerMentions } from './composer-mentions';
import { ComposerPoll } from './composer-poll';
import { ComposerPollForm } from './composer-poll-form';
import { ComposerSchedule } from './composer-schedule';
import { ComposerScheduleFields } from './composer-schedule-fields';
import { ComposerSlashNotice } from './composer-slash-notice';
import { ComposerSlashSession } from './composer-slash-session';
import { ComposerState } from './composer-state';
import { ComposerSubmit } from './composer-submit';

@Component({
  selector: 'vc-composer',
  standalone: true,
  imports: [
    Button,
    IconButton,
    Textarea,
    MentionAutocomplete,
    SlashAutocomplete,
    SchedulePanel,
    ComposerAnnouncementAck,
    ComposerContextBar,
    ComposerAttachmentList,
    ComposerPollForm,
    ComposerSlashNotice,
    ComposerFormatToolbar,
    ComposerScheduleFields,
    ComposerAudioControls,
  ],
  templateUrl: './composer.html',
  styleUrl: './composer.scss',
  providers: [
    ComposerState,
    ComposerMentions,
    ComposerPoll,
    ComposerSlashSession,
    ComposerSchedule,
    ComposerDrafts,
    ComposerSubmit,
  ],
})
export class Composer {
  private readonly composerTextarea = viewChild<Textarea>('composerTextarea');

  readonly state = inject(ComposerState);
  readonly messages = inject(MessageStore);
  readonly channels = inject(ChannelStore);
  private readonly profiles = inject(MemberProfileStore);
  readonly attachments = inject(AttachmentQueueService);
  readonly audioRecorder = inject(AudioRecorderService);
  readonly slash = inject(SlashCommandsService);
  private readonly palette = inject(CommandPaletteService);
  private readonly hub = inject(ChatHubService);
  readonly schedule = inject(ScheduleStore);
  readonly mentions = inject(ComposerMentions);
  readonly slashSession = inject(ComposerSlashSession);
  readonly polls = inject(ComposerPoll);
  readonly scheduler = inject(ComposerSchedule);
  private readonly drafts = inject(ComposerDrafts);
  private readonly submitter = inject(ComposerSubmit);

  readonly ui = ui;
  readonly draft = this.state.draft;
  readonly validationError = this.state.validationError;
  readonly sendingAudio = this.state.sendingAudio;
  readonly mentionOpen = this.mentions.open;
  readonly mentionItems = this.mentions.items;
  readonly mentionActiveIndex = this.mentions.activeIndex;
  readonly slashOpen = this.slashSession.open;
  readonly slashItems = this.slashSession.items;
  readonly slashActiveIndex = this.slashSession.activeIndex;
  readonly pollComposerOpen = this.polls.open;
  readonly announcementReadOnly = computed(() => isAnnouncementReadOnly(this.channels));
  readonly canPublishAnnouncement = computed(() => isCanPublishAnnouncement(this.channels, this.messages));
  readonly maxLength = MESSAGE_BODY_MAX_LENGTH;
  readonly bodyLength = computed(() => measureMessageBodyLength(this.draft()));
  readonly bodyTooLong = computed(() => isMessageBodyTooLong(this.draft()));
  readonly showCounter = computed(() => this.bodyLength() >= MESSAGE_BODY_COUNTER_THRESHOLD);
  readonly readyCount = computed(() => this.attachments.readyAttachmentIds().length);
  /** Primary CTA — Salvar while editing (B-173); Enviar áudio while mic active. */
  readonly primarySubmitLabel = computed(() => {
    if (this.messages.editingMessage()) return ui.composerSave;
    const phase = this.audioRecorder.phase();
    if (phase === 'recording' || phase === 'preview') return ui.composerSendAudio;
    if (this.scheduler.mode()) return ui.composerScheduleConfirm;
    return ui.composerSend;
  });
  readonly submitDisabled = computed(() => {
    if (this.announcementReadOnly()) return true;
    if (this.state.submitting() || this.messages.sending() || this.sendingAudio()) return true;
    if (this.messages.editingMessage()) return !this.draft().trim() || this.bodyTooLong();
    const phase = this.audioRecorder.phase();
    if (phase === 'recording' || phase === 'preview') return false;
    const hasText = !!this.draft().trim();
    const ready = this.readyCount();
    const uploading = this.attachments.hasActiveUploads();
    return (!hasText && ready === 0 && !uploading) || this.bodyTooLong() || this.attachments.submitBlocked();
  });

  constructor() {
    this.state.textarea = () => this.composerTextarea()?.nativeElement() ?? null;

    effect(() => {
      const prefill = this.channels.composerPrefill();
      if (prefill === null) return;
      const text = this.channels.consumeComposerPrefill();
      if (text) {
        this.draft.set(text);
        this.drafts.persistSoon();
      }
    });

    effect(() => {
      if (!this.messages.replyTarget()) return;
      queueMicrotask(() => this.state.textarea()?.focus());
    });

    effect(() => {
      if (this.palette.composerFocusTick() <= 0) return;
      queueMicrotask(() => this.state.textarea()?.focus());
    });

    effect(() => {
      const editing = this.messages.editingMessage();
      if (!editing) return;
      untracked(() => {
        if (!this.state.draftBeforeEdit) this.state.draftBeforeEdit = { body: this.draft() };
        this.draft.set(this.mentions.toComposerDisplay(editing.body));
        this.attachments.clear();
        this.audioRecorder.reset();
        this.mentions.close();
        this.slashSession.close();
        this.validationError.set(null);
        this.slash.clearNotice();
      });
      queueMicrotask(() => this.state.textarea()?.focus());
    });

    effect(() => {
      const channelId = this.channels.activeChannelId();
      untracked(() => {
        void this.drafts.onActiveChannelChanged(channelId);
      });
    });

    effect(() => {
      this.draft();
      this.attachments.items();
      if (this.state.restoringDraft || this.messages.editingMessage()) return;
      const channelId = untracked(() => this.state.boundChannelId);
      if (!channelId) return;
      this.drafts.persistSoon();
    });
  }

  composerPlaceholder(): string {
    if (this.announcementReadOnly()) return ui.composerAnnouncementReadonly;
    if (this.messages.editingMessage()) return ui.composerEdit;
    return `${ui.composerPlaceholder} #${this.channels.activeChannel()?.name || 'channel'}`;
  }

  cancelEdit(): void {
    this.messages.clearEdit();
    this.drafts.restoreAfterEdit(this.draft);
    queueMicrotask(() => this.state.textarea()?.focus());
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    input.value = '';
    this.queueFiles(files);
  }

  onPaste(event: ClipboardEvent): void {
    if (this.messages.editingMessage()) return;
    const files = collectFilesFromClipboard(event.clipboardData);
    if (!files.length) return;
    event.preventDefault();
    this.queueFiles(files);
  }

  queueFiles(files: File[]): void {
    if (this.messages.editingMessage()) return;
    this.validationError.set(this.attachments.addFiles(files));
  }

  onSubmit(event: Event): Promise<void> {
    return this.submitter.onSubmit(event);
  }

  applyFormat(kind: ComposerFormatKind): void {
    applyComposerFormat(this.draft, this.state.textarea(), kind);
  }

  insertEmoji(emoji: string): void {
    insertComposerEmoji(this.draft, this.state.textarea(), emoji);
  }

  onInput(): void {
    const textarea = this.state.textarea();
    if (!textarea) return;
    this.syncMenus(textarea.value, textarea.selectionStart ?? textarea.value.length);
  }

  onKeydown(event: KeyboardEvent): void {
    const menu = decideComposerMenuKey(event, {
      slashOpen: this.slashOpen(),
      slashCount: this.slashItems().length,
      slashIndex: this.slashActiveIndex(),
      mentionOpen: this.mentionOpen(),
      mentionQueryActive: this.mentions.hasActiveQuery(),
      mentionCount: this.mentionItems().length,
      mentionIndex: this.mentionActiveIndex(),
      editing: !!this.messages.editingMessage(),
    });
    if (menu) {
      this.applyMenuKey(menu);
      return;
    }

    if (
      event.key === 'ArrowUp' &&
      !this.messages.editingMessage() &&
      !this.draft().trim() &&
      !this.slashOpen() &&
      !this.mentionOpen()
    ) {
      const cursor = this.state.textarea()?.selectionStart ?? 0;
      if (cursor === 0) {
        const last = this.messages.lastOwnPersistedMessage();
        if (last) {
          event.preventDefault();
          this.messages.startEdit(last);
          return;
        }
      }
    }

    const action = decideComposerSubmitKey(event);
    if (action.type === 'format') {
      this.applyFormat(action.kind);
      return;
    }
    if (action.type === 'submit') {
      void this.onSubmit(event);
      return;
    }
    this.afterKeySync();
  }

  applyMention(item: MentionAutocompleteItem): void {
    this.mentions.apply(item, this.draft);
  }

  openMentionProfile(item: MentionAutocompleteItem): void {
    if (item.userId) this.profiles.open(item.userId);
  }

  applySlash(item: SlashCommandDef): void {
    this.slashSession.apply(item);
  }

  openPollComposer(question = '', options?: string[]): void {
    this.polls.openWith(question, options);
  }

  submitPoll(event: Event): Promise<void> {
    return this.polls.submit(event);
  }

  private syncMenus(text: string, cursor: number): void {
    this.slashSession.syncWithMentions(text, cursor, !!this.messages.editingMessage(), this.mentions);
  }

  private applyMenuKey(action: ComposerKeyAction): void {
    switch (action.type) {
      case 'slash-index':
        this.slashSession.activeIndex.set(action.index);
        return;
      case 'slash-close':
        this.slashSession.close();
        return;
      case 'slash-apply':
        this.applySlash(this.slashItems()[this.slashActiveIndex()]);
        return;
      case 'mention-index':
        this.mentions.activeIndex.set(action.index);
        return;
      case 'mention-close':
        this.mentions.close();
        return;
      case 'mention-apply': {
        const items = this.mentionItems();
        if (items.length) {
          const index = Math.min(Math.max(this.mentionActiveIndex(), 0), items.length - 1);
          this.applyMention(items[index]);
        }
        return;
      }
      case 'cancel-edit':
        this.cancelEdit();
        return;
      default:
        return;
    }
  }

  private afterKeySync(): void {
    const textarea = this.state.textarea();
    if (textarea) {
      queueMicrotask(() => {
        this.syncMenus(textarea.value, textarea.selectionStart ?? textarea.value.length);
      });
    }
    const channelId = this.channels.activeChannel()?.id;
    if (!channelId) return;
    const now = Date.now();
    if (now - this.state.lastTyping > 1500) {
      this.state.lastTyping = now;
      void this.hub.sendTyping(channelId);
    }
  }
}
