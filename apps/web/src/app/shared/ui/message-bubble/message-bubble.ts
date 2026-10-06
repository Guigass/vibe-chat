import { DatePipe } from '@angular/common';
import { CdkContextMenuTrigger, CdkMenu, CdkMenuItem, CdkMenuTrigger } from '@angular/cdk/menu';
import { Component, computed, effect, inject, input, output, signal, viewChild } from '@angular/core';
import { ChatMessage, MessageAttachment, REACTION_EMOJI_OPTIONS } from '../../models/chat.models';
import type { MessageMenuActionId } from '../../attachments/attachment-preview';
import { Avatar } from '../avatar/avatar';
import { ImageLightbox } from '../image-lightbox/image-lightbox';
import { MarkdownBody } from '../../markdown/markdown-body';
import { ApiService } from '../../../core/api/api.service';
import { AuthService } from '../../../core/auth/auth.service';
import { ChannelStore } from '../../../core/services/channel.store';
import { MessageStore } from '../../../core/services/message.store';
import { idsEqual } from '../../../core/services/message-sync';
import { ThemeService } from '../../../core/services/theme.service';
import { EmojiPicker } from '../emoji-picker/emoji-picker';
import { PollCard } from '../poll-card/poll-card';
import { AnnouncementCard } from '../announcement-card/announcement-card';
import { rememberRecentEmoji } from '../../emoji/emoji-data';
import { environment } from '../../../../environments/environment';
import { LocaleService } from '../../../core/i18n/locale.service';
import { fillTemplate, ui } from '../../../core/i18n/strings';
import {
  MINE_ACTION_MENU_POSITIONS,
  THEIRS_ACTION_MENU_POSITIONS,
  buildMessageMenuItems,
  clampOpenMessageMenu,
  emitMessageMenuAction,
} from './message-menu';
import {
  downloadMessageAttachment,
  formatReactionAriaLabel,
  lightboxImagesOf,
  loadAttachmentDownloadUrl,
  loadReactionTooltipText,
  syncAttachmentUrls,
  syncLinkPreviewImage,
  transcribeMessageAttachment,
  visibleLinkPreviewOf,
} from './message-media';
import { MessageAttachments } from './message-attachments';
import { MessageForward } from './message-forward';
import { MessageLinkPreviewCard } from './message-link-preview';
import { MessageQuote } from './message-quote';
import { MessageReactions } from './message-reactions';
import { MessageStatusIcons } from './message-status-icons';
import { MessageHistoryDialog } from '../message-history/message-history-dialog';
import { MessageMoveDialog } from '../message-history/message-move-dialog';
import { MOVED_BODY } from '../../messaging/message-history';

@Component({
  selector: 'vc-message-bubble',
  standalone: true,
  imports: [
    Avatar,
    DatePipe,
    ImageLightbox,
    MarkdownBody,
    EmojiPicker,
    PollCard,
    AnnouncementCard,
    CdkContextMenuTrigger,
    CdkMenuTrigger,
    CdkMenu,
    CdkMenuItem,
    MessageStatusIcons,
    MessageForward,
    MessageQuote,
    MessageLinkPreviewCard,
    MessageAttachments,
    MessageReactions,
    MessageHistoryDialog,
    MessageMoveDialog,
  ],
  templateUrl: './message-bubble.html',
  styleUrl: './message-bubble.scss',
})
export class MessageBubble {
  readonly ui = ui;
  readonly fillTemplate = fillTemplate;
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly channels = inject(ChannelStore);
  private readonly messages = inject(MessageStore);
  private readonly theme = inject(ThemeService);
  private readonly locales = inject(LocaleService);
  private readonly contextMenu = viewChild(CdkContextMenuTrigger);

  readonly message = input.required<ChatMessage>();
  readonly statusEmoji = input<string | null>(null);
  readonly statusLabel = input<string | null>(null);
  readonly showMeta = input(true);
  readonly showAvatar = input(true);
  readonly groupRole = input<'start' | 'middle' | 'end' | 'single'>('single');
  readonly surface = input<'bubble' | 'plain'>('bubble');
  readonly showThreadAction = input(false);
  readonly showReplyAction = input(false);
  readonly showForwardAction = input(false);
  readonly showShareToChannelAction = input(false);
  readonly showPinAction = input(false);
  readonly showSaveAction = input(false);
  readonly showRemindAction = input(false);
  readonly showMarkUnreadAction = input(false);
  readonly highlighted = input(false);
  readonly delete = output<void>();
  readonly removeLinkPreview = output<void>();
  readonly openThread = output<void>();
  readonly reply = output<void>();
  readonly forward = output<void>();
  readonly shareToChannel = output<void>();
  readonly quoteClick = output<string>();
  readonly react = output<string>();
  readonly pin = output<void>();
  readonly unpin = output<void>();
  readonly save = output<void>();
  readonly unsave = output<void>();
  readonly remind = output<void>();
  readonly markUnread = output<void>();
  readonly startEdit = output<void>();

  readonly transcript = signal<string | null>(null);
  readonly downloadUrls = signal<Record<string, string>>({});
  readonly previewUrls = signal<Record<string, string>>({});
  readonly linkPreviewImageUrl = signal<string | null>(null);
  readonly emojiOptions = REACTION_EMOJI_OPTIONS;
  readonly own = computed(() => idsEqual(this.message().authorUserId, this.auth.profile()?.id));
  readonly authorLabel = computed(() => {
    const id = this.message().authorUserId;
    const members = this.channels.members?.() ?? [];
    return members.find((member) => idsEqual(member.userId, id))?.displayName || this.message().authorName;
  });
  readonly authorAvatarPath = computed(() => {
    const id = this.message().authorUserId;
    const members = this.channels.members?.() ?? [];
    return members.find((member) => idsEqual(member.userId, id))?.avatarUrl ?? null;
  });
  readonly actionMenuPositions = computed(() =>
    this.own() ? MINE_ACTION_MENU_POSITIONS : THEIRS_ACTION_MENU_POSITIONS,
  );
  readonly reactionPickerOpen = signal(false);
  readonly menuOpen = signal(false);
  readonly historyOpen = signal(false);
  readonly moveOpen = signal(false);
  readonly movedBody = MOVED_BODY;
  readonly openHistory = { emit: () => this.historyOpen.set(true) };
  readonly openMove = { emit: () => this.moveOpen.set(true) };
  readonly reactionTooltips = signal<Record<string, string>>({});
  readonly lightboxOpen = signal(false);
  readonly lightboxStartId = signal<string | null>(null);
  readonly avatarSize = computed(() => (this.theme.density() === 'compact' ? 28 : 34));
  readonly emojiLocale = computed(() => (this.locales.locale() === 'en' ? 'en' : 'pt'));
  readonly transcribeEnabled = computed(() => environment.aiTranscribeEnabled && environment.aiSummarizeEnabled);
  readonly mentionLabels = computed(() => this.channels.mentionLabels());
  readonly canClosePoll = computed(() => {
    const poll = this.message().poll;
    const role = this.channels.activeWorkspace()?.role?.toLowerCase();
    return !!poll && !poll.closedAt && (this.own() || role === 'admin' || role === 'workspaceowner');
  });
  readonly announcementReport = signal<Array<{ userId: string; displayName: string }>>([]);
  readonly announcementError = signal<string | null>(null);
  readonly showActions = computed(() => !this.message().deletedAt && this.message().status === 'persisted');
  readonly visibleLinkPreview = computed(() => visibleLinkPreviewOf(this.message()));
  readonly menuItems = computed(() =>
    buildMessageMenuItems(this.channels, this.message(), this.own(), {
      showForward: this.showForwardAction(),
      showShareToChannel: this.showShareToChannelAction(),
      showThread: this.showThreadAction(),
      showPin: this.showPinAction(),
      showSave: this.showSaveAction(),
      showRemind: this.showRemindAction(),
      showMarkUnread: this.showMarkUnreadAction(),
      hasLinkPreview: !!this.visibleLinkPreview(),
    }),
  );
  readonly lightboxImages = computed(() =>
    lightboxImagesOf(this.message(), this.downloadUrls(), this.previewUrls()),
  );

  private longPressTimer: ReturnType<typeof setTimeout> | null = null;
  private longPressPoint: { x: number; y: number } | null = null;

  constructor() {
    effect(() => {
      syncAttachmentUrls(this.message(), this.api, this.previewUrls, this.downloadUrls);
    });
    effect(() => {
      syncLinkPreviewImage(this.visibleLinkPreview(), this.message(), this.api, this.linkPreviewImageUrl);
    });
  }

  async onMentionClick(userId: string): Promise<void> {
    const me = this.auth.profile()?.id;
    if (!userId || userId === me) return;
    const channel = await this.channels.openDirectMessage(userId);
    if (channel) await this.messages.loadChannel(channel.id);
  }

  onPollToggle(optionId: string): void {
    const poll = this.message().poll;
    if (poll) void this.messages.votePoll(poll, optionId);
  }

  onPollClose(): void {
    const poll = this.message().poll;
    if (poll) void this.messages.closePoll(poll.id, this.message().id);
  }

  async onAcknowledge(): Promise<void> {
    this.announcementError.set(null);
    const ok = await this.messages.acknowledgeAnnouncement(this.message());
    if (!ok) this.announcementError.set(ui.announcementActionFailed);
  }

  async onCloseAnnouncement(): Promise<void> {
    this.announcementError.set(null);
    const ok = await this.messages.closeAnnouncement(this.message());
    if (!ok) this.announcementError.set(ui.announcementActionFailed);
  }

  async onLoadAnnouncementReport(): Promise<void> {
    try {
      this.announcementReport.set(await this.messages.loadAnnouncementReport(this.message()));
    } catch {
      this.announcementError.set(ui.announcementActionFailed);
    }
  }

  onLinkPreviewImageError(): void {
    this.linkPreviewImageUrl.set(null);
  }

  async download(attachment: MessageAttachment): Promise<void> {
    await downloadMessageAttachment(this.api, this.message().channelId, attachment, this.downloadUrls);
  }

  async transcribe(attachment: MessageAttachment): Promise<void> {
    await transcribeMessageAttachment(
      this.api,
      this.channels.activeWorkspace()?.id,
      this.message(),
      attachment,
      this.transcript,
    );
  }

  toggleReactionPicker(event: Event): void {
    event.stopPropagation();
    this.reactionPickerOpen.update((open) => !open);
  }

  onQuickReact(emoji: string): void {
    rememberRecentEmoji(emoji);
    this.reactionPickerOpen.set(false);
    this.react.emit(emoji);
  }

  reactionTooltip(emoji: string): string {
    return this.reactionTooltips()[emoji] ?? '';
  }

  reactionAriaLabel(emoji: string): string {
    return formatReactionAriaLabel(emoji, this.reactionTooltip(emoji));
  }

  async loadReactionTooltip(emoji: string): Promise<void> {
    await loadReactionTooltipText(
      this.api,
      this.message().channelId,
      this.message().id,
      emoji,
      this.reactionTooltips,
      this.channels.isDemo(),
    );
  }

  openMovedTarget(): void {
    const message = this.message();
    const channelId = message.movedToChannelId;
    const messageId = message.movedToMessageId;
    if (!message.movedToAccessible || !channelId || !messageId) return;
    this.channels.selectChannel(channelId);
    void this.messages.jumpToSequence(channelId, message.movedToSequence ?? 0, messageId);
  }

  onActionsMenuOpened(): void {
    this.menuOpen.set(true);
    clampOpenMessageMenu();
    requestAnimationFrame(() => clampOpenMessageMenu());
  }

  onMenuAction(id: MessageMenuActionId): void {
    if (this.menuItems().find((item) => item.id === id)?.disabled) return;
    emitMessageMenuAction(id, this);
  }

  openLightbox(attachmentId: string): void {
    const channelId = this.message().channelId;
    if (channelId && !this.downloadUrls()[attachmentId]) {
      void loadAttachmentDownloadUrl(this.api, channelId, attachmentId, this.downloadUrls);
    }
    this.lightboxStartId.set(attachmentId);
    this.lightboxOpen.set(true);
  }

  closeLightbox(): void {
    this.lightboxOpen.set(false);
    this.lightboxStartId.set(null);
  }

  onTouchStart(event: TouchEvent): void {
    if (!this.showActions() || event.touches.length !== 1) return;
    const touch = event.touches[0];
    this.longPressPoint = { x: touch.clientX, y: touch.clientY };
    this.clearLongPress();
    this.longPressTimer = setTimeout(() => {
      const point = this.longPressPoint;
      const trigger = this.contextMenu();
      if (!point || !trigger) return;
      event.preventDefault();
      trigger.open(point);
    }, 480);
  }

  onTouchEnd(): void {
    this.clearLongPress();
  }

  private clearLongPress(): void {
    if (this.longPressTimer) {
      clearTimeout(this.longPressTimer);
      this.longPressTimer = null;
    }
  }
}
