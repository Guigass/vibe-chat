import { Component, effect, inject, ElementRef, signal, viewChild } from '@angular/core';
import { AuthService } from '../../../core/auth/auth.service';
import { ApiService } from '../../../core/api/api.service';
import { MessageStore } from '../../../core/services/message.store';
import { ChatHubService } from '../../../core/services/chat-hub.service';
import { ChannelStore } from '../../../core/services/channel.store';
import { ThreadStore } from '../../../core/services/thread.store';
import { withoutSelfTyping } from '../../../core/services/typing-filter';
import { ChatMessage } from '../../../shared/models/chat.models';
import { EmptyState, MessageBubble, Skeleton, TypingIndicator } from '../../../shared/ui';
import { ForwardDialog } from '../forward-dialog/forward-dialog';

@Component({
  selector: 'vc-timeline',
  standalone: true,
  imports: [MessageBubble, TypingIndicator, EmptyState, Skeleton, ForwardDialog],
  template: `
    <section class="timeline" #scroller aria-live="polite">
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
        <div class="timeline__list">
          @for (message of messages.forActiveChannel(); track message.id) {
            <vc-message-bubble
              [message]="message"
              [showReplyAction]="true"
              [showForwardAction]="true"
              [showThreadAction]="true"
              [highlighted]="messages.highlightMessageId() === message.id"
              (edit)="onEdit(message.id, $event)"
              (delete)="onDelete(message.id)"
              (reply)="onReply(message)"
              (forward)="onForward(message)"
              (openThread)="onOpenThread(message.id)"
              (quoteClick)="onQuoteClick($event)"
              (react)="onReact(message.id, $event)"
            />
          }
        </div>
      }
      <vc-typing-indicator [users]="typingForChannel()" />
    </section>

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
    }
    .timeline {
      flex: 1 1 auto;
      min-height: 0;
      overflow: auto;
      overscroll-behavior: contain;
      padding: var(--vc-timeline-pad);
      display: flex;
      flex-direction: column;
      gap: var(--vc-timeline-gap);
    }
    .timeline__list {
      display: flex;
      flex-direction: column;
      gap: var(--vc-timeline-gap);
      margin-top: auto;
    }
    .timeline__loading {
      display: grid;
      gap: 0.75rem;
      margin-top: auto;
    }
  `,
})
export class Timeline {
  readonly messages = inject(MessageStore);
  readonly channels = inject(ChannelStore);
  private readonly threads = inject(ThreadStore);
  private readonly hub = inject(ChatHubService);
  private readonly auth = inject(AuthService);
  private readonly api = inject(ApiService);
  private readonly scroller = viewChild<ElementRef<HTMLElement>>('scroller');
  private readonly forwardDialog = viewChild(ForwardDialog);

  readonly forwardTarget = signal<ChatMessage | null>(null);
  readonly forwardSourcePrivate = signal(false);
  readonly forwardSubmitting = signal(false);

  constructor() {
    effect(() => {
      this.messages.forActiveChannel();
      queueMicrotask(() => {
        const el = this.scroller()?.nativeElement;
        if (el) el.scrollTop = el.scrollHeight;
      });
    });
  }

  typingForChannel() {
    const channelId = this.channels.activeChannel()?.id;
    if (!channelId) return [];
    const forChannel = this.hub.typingUsers().filter((t) => t.channelId === channelId);
    return withoutSelfTyping(forChannel, this.auth.profile()?.id);
  }

  async onEdit(messageId: string, body: string): Promise<void> {
    await this.messages.edit(messageId, body);
  }

  async onDelete(messageId: string): Promise<void> {
    await this.messages.remove(messageId);
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
    this.messages.jumpToMessage(messageId);
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
}
