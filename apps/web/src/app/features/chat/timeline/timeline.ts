import {
  Component,
  DestroyRef,
  effect,
  inject,
  ElementRef,
  signal,
  computed,
  viewChild,
} from '@angular/core';
import { AuthService } from '../../../core/auth/auth.service';
import { ApiService } from '../../../core/api/api.service';
import { MessageStore, type MessageScrollRequest } from '../../../core/services/message.store';
import { ChatHubService } from '../../../core/services/chat-hub.service';
import { ChannelStore } from '../../../core/services/channel.store';
import { ThemeService } from '../../../core/services/theme.service';
import { ThreadStore } from '../../../core/services/thread.store';
import { withoutSelfTyping } from '../../../core/services/typing-filter';
import { ChatMessage } from '../../../shared/models/chat.models';
import { Avatar, EmptyState, MessageBubble, Skeleton, TypingIndicator } from '../../../shared/ui';
import { ForwardDialog } from '../forward-dialog/forward-dialog';
import { PinStore } from '../../../core/services/pin.store';
import { SavedStore } from '../../../core/services/saved.store';
import {
  buildTimelineItems,
  unreadDividerAfterSeq,
  type TimelineItem,
} from './timeline-items';
import {
  TimelineScrollAnchorController,
  TimelineStickyBottomPin,
  shouldStickTimelineToBottom,
  type TimelineScrollAnchor,
} from './timeline-scroll';

const NEAR_BOTTOM_PX = 80;
const NEAR_TOP_PX = 120;

@Component({
  selector: 'vc-timeline',
  standalone: true,
  imports: [Avatar, MessageBubble, TypingIndicator, EmptyState, Skeleton, ForwardDialog],
  template: `
    <section
      class="timeline"
      #scroller
      aria-live="polite"
      (scroll)="onScroll()"
      (wheel)="cancelPendingAnchor()"
      (pointerdown)="cancelPendingAnchor()"
      (touchstart)="cancelPendingAnchor()"
      (keydown)="cancelPendingAnchor()"
    >
      @if (messages.loading()) {
        <div class="timeline__loading">
          <vc-skeleton height="3.5rem" />
          <vc-skeleton height="3.5rem" width="80%" />
          <vc-skeleton height="3.5rem" width="70%" />
        </div>
      } @else if (!messages.forActiveChannel().length) {
        <vc-empty-state
          title="Canal em silêncio"
          description="Envie a primeira mensagem. O status mostrará enviando → enviada/salva sem fingir persistência."
        />
      } @else {
        @if (messages.paginationForActive().loadingOlder) {
          <div class="timeline__top-load" data-testid="timeline-loading-older">
            <vc-skeleton height="3.5rem" />
            <vc-skeleton height="3.5rem" width="80%" />
          </div>
        } @else if (messages.paginationForActive().loadError) {
          <button
            type="button"
            class="timeline__retry"
            data-testid="timeline-retry-older"
            (click)="messages.retryLoadOlder()"
          >
            Não foi possível carregar — tentar novamente
          </button>
        } @else if (atConversationStart()) {
          <div class="timeline__start" data-testid="timeline-start" role="status">
            Início da conversa
          </div>
        }
        <div class="timeline__list">
          @for (item of timelineItems(); track item.id) {
            @switch (item.kind) {
              @case ('day') {
                <div
                  class="timeline__day"
                  role="separator"
                  [attr.aria-label]="item.ariaLabel"
                  data-testid="timeline-day"
                >
                  <span>{{ item.label }}</span>
                </div>
              }
              @case ('unread') {
                <div
                  class="timeline__unread"
                  role="separator"
                  aria-label="Novas mensagens"
                  data-testid="timeline-unread"
                >
                  <span>Novas mensagens</span>
                </div>
              }
              @case ('stack') {
                <div
                  class="timeline__stack"
                  [class.timeline__stack--mine]="item.mine"
                  data-testid="timeline-stack"
                >
                  @if (!item.mine) {
                    <div class="timeline__stack-avatar">
                      <vc-avatar
                        [name]="item.messages[0].message.authorName"
                        [size]="avatarSize()"
                      />
                    </div>
                  }
                  <div class="timeline__stack-body">
                    @for (entry of item.messages; track entry.id) {
                      <vc-message-bubble
                        [message]="entry.message"
                        [showMeta]="entry.showMeta"
                        [showAvatar]="false"
                        [groupRole]="entry.group"
                        [surface]="'plain'"
                        [showReplyAction]="true"
                        [showForwardAction]="true"
                        [showThreadAction]="true"
                        [showPinAction]="true"
                        [showSaveAction]="true"
                        [showMarkUnreadAction]="true"
                        [highlighted]="messages.highlightMessageId() === entry.message.id"
                        (startEdit)="onStartEdit(entry.message)"
                        (delete)="onDelete(entry.message.id)"
                        (removeLinkPreview)="onRemoveLinkPreview(entry.message.id)"
                        (reply)="onReply(entry.message)"
                        (forward)="onForward(entry.message)"
                        (openThread)="onOpenThread(entry.message.id)"
                        (quoteClick)="onQuoteClick($event)"
                        (react)="onReact(entry.message.id, $event)"
                        (pin)="onPin(entry.message.id)"
                        (unpin)="onUnpin(entry.message.id)"
                        (save)="onSave(entry.message.id)"
                        (unsave)="onUnsave(entry.message.id)"
                        (markUnread)="onMarkUnread(entry.message)"
                      />
                    }
                  </div>
                </div>
              }
              @case ('system') {
                <div class="timeline__system" role="status" data-testid="timeline-system">
                  <span>{{ item.label }}</span>
                </div>
              }
            }
          }
        </div>
      }
      @if (typingForChannel().length) {
        <vc-typing-indicator [users]="typingForChannel()" />
      }
    </section>

    <div class="vc-sr-only" aria-live="polite">{{ unreadLive() }}</div>

    @if (showJump()) {
      <button
        type="button"
        class="timeline__jump"
        data-testid="timeline-jump"
        (click)="jumpToLatest()"
      >
        {{ jumpLabel() }}
      </button>
    }

    <vc-forward-dialog
      [open]="!!forwardTarget()"
      [sourceIsPrivate]="!!forwardSourcePrivate()"
      [submitting]="forwardSubmitting()"
      (cancel)="closeForward()"
      (confirm)="confirmForward($event)"
    />
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      flex: 1 1 auto;
      min-height: 0;
      overflow: hidden;
      position: relative;
    }
    .timeline {
      flex: 1 1 auto;
      min-width: 0;
      min-height: 0;
      overflow-x: clip;
      overflow-y: auto;
      overscroll-behavior: contain;
      padding: 0 var(--vc-timeline-pad) 0.5rem;
      display: flex;
      flex-direction: column;
    }
    .timeline__list {
      display: flex;
      flex-direction: column;
      gap: var(--vc-timeline-gap);
      margin-top: auto;
      min-width: 0;
      padding-top: var(--vc-timeline-pad);
    }
    .timeline__top-load,
    .timeline__start,
    .timeline__retry {
      flex-shrink: 0;
      margin: 0.35rem 0 0.5rem;
    }
    .timeline__start {
      text-align: center;
      color: var(--vc-text-muted);
      font-size: 0.78rem;
      letter-spacing: 0.02em;
    }
    .timeline__retry {
      display: block;
      width: 100%;
      border: 1px dashed var(--vc-border-subtle);
      border-radius: var(--vc-radius-md, 8px);
      background: transparent;
      color: var(--vc-brand);
      font: inherit;
      font-size: 0.82rem;
      padding: 0.45rem 0.75rem;
      cursor: pointer;
    }
    .timeline__stack {
      --vc-msg-max: min(44rem, 100%);
      display: grid;
      grid-template-columns: var(--vc-msg-avatar) minmax(0, var(--vc-msg-max));
      gap: var(--vc-msg-gap);
      align-items: flex-start;
      width: fit-content;
      max-width: 100%;
      min-width: 0;
    }
    .timeline__stack--mine {
      margin-left: auto;
      max-width: calc(100% - 2.75rem);
      grid-template-columns: minmax(0, var(--vc-msg-max));
      justify-content: end;
    }
    .timeline__stack-avatar {
      width: var(--vc-msg-avatar);
      flex-shrink: 0;
      padding-top: 0.15rem;
    }
    .timeline__stack-body {
      display: flex;
      flex-direction: column;
      min-width: 0;
      width: max-content;
      max-width: 100%;
      gap: 0.25rem;
      /* visible: hover toolbar / menus must not be clipped */
      overflow: visible;
    }
    .timeline__stack--mine .timeline__stack-body {
      align-items: flex-end;
    }
    .timeline__loading {
      display: grid;
      gap: 0.75rem;
      margin-top: auto;
      padding-top: var(--vc-timeline-pad);
    }
    .timeline__day {
      position: sticky;
      top: 0;
      z-index: 2;
      display: grid;
      grid-template-columns: 1fr auto 1fr;
      align-items: center;
      gap: 0.75rem;
      width: auto;
      margin-inline: calc(-1 * var(--vc-timeline-pad));
      padding: 0.45rem var(--vc-timeline-pad);
      background: var(--vc-surface);
      color: var(--vc-ink-muted);
      font-size: 0.78rem;
      font-weight: 600;
    }
    .timeline__day::before,
    .timeline__day::after {
      content: '';
      height: 1px;
      background: var(--vc-border);
    }
    .timeline__day span {
      text-align: center;
      white-space: nowrap;
      padding: 0.12rem 0.65rem;
      border-radius: var(--vc-radius-md);
      background: var(--vc-surface);
    }
    .timeline__unread {
      display: grid;
      grid-template-columns: 1fr auto;
      align-items: center;
      gap: 0.75rem;
      color: var(--vc-brand);
      font-size: 0.78rem;
      font-weight: 600;
    }
    .timeline__unread::before {
      content: '';
      height: 1px;
      background: var(--vc-brand);
    }
    .timeline__unread span {
      white-space: nowrap;
    }
    .timeline__jump {
      position: absolute;
      bottom: 0.75rem;
      left: 50%;
      transform: translateX(-50%);
      z-index: 2;
      border: 1px solid color-mix(in srgb, var(--vc-brand) 40%, var(--vc-border));
      border-radius: var(--vc-radius-md);
      background: var(--vc-surface-elevated);
      color: var(--vc-brand-ink);
      font: inherit;
      font-size: 0.82rem;
      font-weight: 600;
      padding: 0.4rem 0.85rem;
      cursor: pointer;
      box-shadow: var(--vc-shadow-soft, 0 8px 24px rgba(15, 23, 42, 0.12));
    }
    .timeline__jump:hover {
      background: color-mix(in srgb, var(--vc-brand) 12%, var(--vc-surface-elevated));
    }

    .timeline__system {
      display: flex;
      justify-content: center;
      padding: var(--vc-space-1) var(--vc-space-3);
      color: var(--vc-text-muted);
      font-size: var(--vc-text-xs);
    }

    .timeline__system span {
      padding: 0.15rem 0.65rem;
      border-radius: var(--vc-radius-pill);
      background: color-mix(in srgb, var(--vc-text-muted) 12%, transparent);
    }
  `,
})
export class Timeline {
  readonly messages = inject(MessageStore);
  readonly channels = inject(ChannelStore);
  private readonly pins = inject(PinStore);
  private readonly saved = inject(SavedStore);
  private readonly threads = inject(ThreadStore);
  private readonly hub = inject(ChatHubService);
  private readonly auth = inject(AuthService);
  private readonly api = inject(ApiService);
  private readonly theme = inject(ThemeService);
  private readonly destroyRef = inject(DestroyRef);
  readonly avatarSize = computed(() => (this.theme.density() === 'compact' ? 28 : 34));
  private readonly scroller = viewChild<ElementRef<HTMLElement>>('scroller');
  private readonly forwardDialog = viewChild(ForwardDialog);

  readonly forwardTarget = signal<ChatMessage | null>(null);
  readonly forwardSourcePrivate = signal(false);
  readonly forwardSubmitting = signal(false);
  readonly nearBottom = signal(true);
  readonly newWhileAway = signal(0);
  readonly unreadLive = signal('');
  readonly showJump = computed(
    () =>
      !this.messages.loading() && this.messages.forActiveChannel().length > 0 && !this.nearBottom(),
  );
  readonly jumpLabel = computed(() => {
    const count = this.newWhileAway();
    return count > 0 ? `Ir para a mais recente · ${count}` : 'Ir para a mais recente';
  });
  readonly atConversationStart = computed(
    () =>
      !this.messages.loading() &&
      this.messages.forActiveChannel().length > 0 &&
      !this.messages.paginationForActive().hasMoreBefore &&
      !this.messages.paginationForActive().loadingOlder,
  );

  private loadingOlder = false;
  private scrollAnchorController: TimelineScrollAnchorController | null = null;
  private readonly stickyBottomPin = new TimelineStickyBottomPin(
    () => this.scroller()?.nativeElement ?? null,
  );

  private readonly unreadSnapshot = signal(0);
  private readonly frozenUnreadAfterSeq = signal<number | null>(null);
  private readonly unreadDismissed = signal(false);
  private lastChannelId: string | null = null;
  private lastMessageCount = 0;
  private lastTailId: string | null = null;
  private unreadAnnouncedFor: string | null = null;

  readonly timelineItems = computed((): TimelineItem[] =>
    buildTimelineItems(this.messages.forActiveChannel(), {
      unreadCount: this.unreadSnapshot(),
      dividerAfterSeq: this.frozenUnreadAfterSeq(),
      showUnreadDivider: !this.unreadDismissed() && this.unreadSnapshot() > 0,
    }),
  );

  constructor() {
    this.destroyRef.onDestroy(() => {
      this.scrollAnchorController?.cancel();
      this.stickyBottomPin.destroy();
    });

    effect(() => {
      const channelId = this.channels.activeChannelId();
      const list = this.messages.forActiveChannel();
      const opened = this.channels.openedUnreadCount();
      this.messages.scrollRequest();

      if (channelId !== this.lastChannelId) {
        this.lastChannelId = channelId;
        this.lastMessageCount = list.length;
        this.lastTailId = list.at(-1)?.id ?? null;
        this.newWhileAway.set(0);
        this.unreadDismissed.set(false);
        this.unreadSnapshot.set(opened);
        this.frozenUnreadAfterSeq.set(
          opened > 0 && list.length > 0 ? unreadDividerAfterSeq(list, opened) : null,
        );
        this.unreadLive.set('');
        this.unreadAnnouncedFor = null;
        this.setNearBottom(true);
        this.messages.markViewedLatest();
        queueMicrotask(() => {
          if (!this.isScrollRequestForChannel(this.messages.scrollRequest(), channelId)) {
            this.afterChannelOpen();
          }
        });
        return;
      }

      if (list.length === this.lastMessageCount) return;

      const added = Math.max(0, list.length - this.lastMessageCount);
      const wasEmpty = this.lastMessageCount === 0;
      const nextTailId = list.at(-1)?.id ?? null;
      const prependOnly =
        this.loadingOlder || (added > 0 && !!this.lastTailId && nextTailId === this.lastTailId);
      const incoming = !prependOnly && added > 0 ? list.slice(-added) : [];
      const ownArrival = incoming.some((m) => m.mine);
      this.lastMessageCount = list.length;
      this.lastTailId = nextTailId;
      this.ensureFrozenUnreadDivider(list);

      if (prependOnly) return;

      // Latch before microtask: after DOM growth, remasuring distance falsely
      // reports "away" even when the user was stuck at the bottom (BUG-018).
      const stick = shouldStickTimelineToBottom(ownArrival, this.nearBottom());

      queueMicrotask(() => {
        if (this.isScrollRequestForChannel(this.messages.scrollRequest(), channelId)) return;
        const el = this.scroller()?.nativeElement;
        if (wasEmpty) {
          this.afterChannelOpen();
          return;
        }
        if (stick) {
          this.setNearBottom(true);
          this.scrollToBottom(el);
          this.newWhileAway.set(0);
          this.messages.markViewedLatest();
          this.clearActiveUnread();
        } else {
          this.newWhileAway.update((n) => n + added);
          this.setNearBottom(false);
          this.messages.setViewingLatest(false);
        }
      });
    });

    effect(() => {
      const request = this.messages.scrollRequest();
      const loading = this.messages.loading();
      const channelId = this.channels.activeChannelId();
      if (!request || loading || !this.isScrollRequestForChannel(request, channelId)) return;

      queueMicrotask(() => {
        const current = this.messages.scrollRequest();
        if (current?.requestId !== request.requestId || this.messages.loading()) return;
        this.anchorMessageRequest(request);
      });
    });
  }

  typingForChannel() {
    const channelId = this.channels.activeChannelId();
    if (!channelId) return [];
    const forChannel = this.hub.typingUsers().filter((t) => t.channelId === channelId);
    return withoutSelfTyping(forChannel, this.auth.profile()?.id);
  }

  onScroll(): void {
    const el = this.scroller()?.nativeElement;
    if (!el) return;
    const near = this.isNearBottom(el);
    const wasNear = this.nearBottom();
    this.setNearBottom(near);
    if (near && !wasNear) {
      this.newWhileAway.set(0);
      this.clearActiveUnread();
      this.messages.markViewedLatest();
    } else if (!near) {
      this.messages.setViewingLatest(false);
    }
    this.dismissUnreadIfPast(el);
    void this.maybeLoadOlder(el);
  }

  private async maybeLoadOlder(el: HTMLElement): Promise<void> {
    if (el.scrollTop > NEAR_TOP_PX) return;
    const pagination = this.messages.paginationForActive();
    if (!pagination.hasMoreBefore || pagination.loadingOlder || this.loadingOlder) return;

    const prevHeight = el.scrollHeight;
    const prevTop = el.scrollTop;
    this.loadingOlder = true;
    const loaded = await this.messages.loadOlderMessages();
    this.loadingOlder = false;
    if (!loaded) return;

    const prependAnchor: TimelineScrollAnchor = {
      kind: 'prepend',
      previousScrollHeight: prevHeight,
      previousScrollTop: prevTop,
    };
    this.anchorTimeline(el, prependAnchor);
  }

  jumpToLatest(): void {
    this.messages.cancelScrollRequest();
    this.newWhileAway.set(0);
    this.setNearBottom(true);
    this.scrollToBottom();
    this.unreadDismissed.set(true);
    this.messages.markViewedLatest();
    this.clearActiveUnread();
  }

  onStartEdit(message: ChatMessage): void {
    this.messages.startEdit(message);
  }

  async onDelete(messageId: string): Promise<void> {
    await this.messages.remove(messageId);
  }

  async onRemoveLinkPreview(messageId: string): Promise<void> {
    await this.messages.removeLinkPreview(messageId);
  }

  onReply(message: ChatMessage): void {
    this.messages.setReplyTarget(message);
  }

  onForward(message: ChatMessage): void {
    const active = this.channels.activeChannel();
    this.forwardSourcePrivate.set(!!active?.isPrivate);
    this.forwardTarget.set(message);
    queueMicrotask(() => this.forwardDialog()?.reset());
  }

  closeForward(): void {
    this.forwardTarget.set(null);
    this.forwardSubmitting.set(false);
    this.forwardDialog()?.reset();
  }

  async confirmForward(payload: { targetChannelIds: string[]; comment: string }): Promise<void> {
    const source = this.forwardTarget();
    const workspace = this.channels.activeWorkspace();
    if (!source || !workspace) return;

    this.forwardSubmitting.set(true);
    try {
      const created = await this.api.forwardMessage({
        workspaceId: workspace.id,
        messageId: source.id,
        targetChannelIds: payload.targetChannelIds,
        comment: payload.comment || undefined,
        idempotencyKey: `fwd-${source.id}-${payload.targetChannelIds.slice().sort().join('-')}`,
      });
      for (const msg of created) {
        this.messages.ingestRemote(msg);
      }
      this.closeForward();
    } catch {
      this.forwardSubmitting.set(false);
    }
  }

  onQuoteClick(messageId: string): void {
    void this.messages.ensureMessageVisible(messageId);
  }

  async onOpenThread(messageId: string): Promise<void> {
    const channelId = this.channels.activeChannel()?.id;
    if (!channelId) return;
    await this.threads.openFromMessage(channelId, messageId);
    const thread = this.threads.active();
    if (thread) {
      this.messages.markThreadOpened(messageId, thread.id);
    }
  }

  async onReact(messageId: string, emoji: string): Promise<void> {
    await this.messages.toggleReaction(messageId, emoji);
  }

  async onPin(messageId: string): Promise<void> {
    const channelId = this.channels.activeChannelId();
    if (!channelId) return;
    const result = await this.pins.pinMessage(channelId, messageId);
    if (result === 'limit') {
      this.pins.openPanel();
    }
  }

  async onUnpin(messageId: string): Promise<void> {
    const channelId = this.channels.activeChannelId();
    if (!channelId) return;
    await this.pins.unpinMessage(channelId, messageId);
  }

  async onSave(messageId: string): Promise<void> {
    await this.saved.saveMessage(messageId);
  }

  async onUnsave(messageId: string): Promise<void> {
    await this.saved.unsaveMessage(messageId);
  }

  async onMarkUnread(message: ChatMessage): Promise<void> {
    await this.messages.markMessageUnread(message);
    this.unreadDismissed.set(false);
  }

  private afterChannelOpen(): void {
    const el = this.scroller()?.nativeElement;
    if (!el) return;
    const channelId = this.channels.activeChannelId();
    if (this.isScrollRequestForChannel(this.messages.scrollRequest(), channelId)) return;
    this.ensureFrozenUnreadDivider(this.messages.forActiveChannel());
    this.announceUnreadOnce();
    const divider = el.querySelector<HTMLElement>('.timeline__unread');
    if (divider) {
      this.anchorTimeline(
        el,
        {
          kind: 'element',
          target: () => el.querySelector<HTMLElement>('.timeline__unread'),
        },
        () => {
          const near = this.isNearBottom(el);
          this.setNearBottom(near);
          if (near) this.messages.markViewedLatest();
          else this.messages.setViewingLatest(false);
          if (!near && this.newWhileAway() === 0) {
            this.newWhileAway.set(this.unreadSnapshot());
          }
        },
      );
      return;
    }
    this.setNearBottom(true);
    this.scrollToBottom(el);
    this.messages.markViewedLatest();
  }

  private ensureFrozenUnreadDivider(list: readonly ChatMessage[]): void {
    if (this.frozenUnreadAfterSeq() !== null) return;
    const unread = this.unreadSnapshot();
    if (unread <= 0 || list.length === 0) return;
    this.frozenUnreadAfterSeq.set(unreadDividerAfterSeq(list, unread));
  }

  private clearActiveUnread(): void {
    const active = this.channels.activeChannel();
    if (!active) return;
    if (active.unreadCount <= 0 && (active.mentionCount ?? 0) <= 0) return;
    this.channels.patchChannel(active.id, { unreadCount: 0, mentionCount: 0 });
  }

  private announceUnreadOnce(): void {
    const channelId = this.channels.activeChannelId();
    if (!channelId || this.unreadDismissed() || this.unreadSnapshot() <= 0) return;
    if (this.unreadAnnouncedFor === channelId) return;
    if (!this.timelineItems().some((item) => item.kind === 'unread')) return;
    this.unreadAnnouncedFor = channelId;
    this.unreadLive.set('Novas mensagens');
    setTimeout(() => this.unreadLive.set(''), 800);
  }

  private dismissUnreadIfPast(el: HTMLElement): void {
    if (this.unreadDismissed()) return;
    const divider = el.querySelector<HTMLElement>('.timeline__unread');
    if (!divider) return;
    const scrollerTop = el.getBoundingClientRect().top;
    if (divider.getBoundingClientRect().bottom < scrollerTop) {
      this.unreadDismissed.set(true);
    }
  }

  private isNearBottom(el = this.scroller()?.nativeElement): boolean {
    if (!el) return true;
    return el.scrollHeight - el.scrollTop - el.clientHeight <= NEAR_BOTTOM_PX;
  }

  private scrollToBottom(el = this.scroller()?.nativeElement): void {
    if (!el) return;
    // Scroller may appear after the latch was set; re-bind the pin observer.
    if (this.nearBottom()) this.stickyBottomPin.sync();
    this.anchorTimeline(el, { kind: 'bottom' });
  }

  cancelPendingAnchor(): void {
    this.scrollAnchorController?.cancel();
    this.scrollAnchorController = null;
    this.messages.cancelScrollRequest();
  }

  private anchorMessageRequest(request: MessageScrollRequest): void {
    const el = this.scroller()?.nativeElement;
    if (!el) return;
    this.anchorTimeline(
      el,
      {
        kind: 'element',
        target: () =>
          Array.from(el.querySelectorAll<HTMLElement>('[data-message-id]')).find((candidate) =>
            this.idsEqual(candidate.dataset['messageId'], request.messageId),
          ) ?? null,
      },
      () => {
        this.setNearBottom(this.isNearBottom(el));
        this.messages.setViewingLatest(false);
      },
      () => this.messages.cancelScrollRequest(request.requestId),
      () => this.messages.acknowledgeScrollRequest(request.requestId),
    );
  }

  private setNearBottom(near: boolean): void {
    // Always sync the pin — nearBottom starts true, so skipping when unchanged
    // left the pin disarmed until the user scrolled away and back.
    this.nearBottom.set(near);
    this.stickyBottomPin.setPinned(near);
  }

  private anchorTimeline(
    el: HTMLElement,
    anchor: TimelineScrollAnchor,
    onAnchored?: () => void,
    onExpired?: () => void,
    onSettled?: () => void,
  ): void {
    this.scrollAnchorController?.cancel();
    const controller = new TimelineScrollAnchorController(el);
    this.scrollAnchorController = controller;
    controller.anchor(anchor, onAnchored, onExpired, onSettled);
  }

  private isScrollRequestForChannel(
    request: MessageScrollRequest | null,
    channelId: string | null,
  ): boolean {
    return !!request && !!channelId && this.idsEqual(request.channelId, channelId);
  }

  private idsEqual(left: string | null | undefined, right: string | null | undefined): boolean {
    return !!left && !!right && left.toLowerCase() === right.toLowerCase();
  }
}
