import { Component, effect, inject, ElementRef, viewChild } from '@angular/core';
import { AuthService } from '../../../core/auth/auth.service';
import { MessageStore } from '../../../core/services/message.store';
import { ChatHubService } from '../../../core/services/chat-hub.service';
import { ChannelStore } from '../../../core/services/channel.store';
import { ThreadStore } from '../../../core/services/thread.store';
import { withoutSelfTyping } from '../../../core/services/typing-filter';
import { EmptyState, MessageBubble, Skeleton, TypingIndicator } from '../../../shared/ui';

@Component({
  selector: 'vc-timeline',
  standalone: true,
  imports: [MessageBubble, TypingIndicator, EmptyState, Skeleton],
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
              [showThreadAction]="true"
              (edit)="onEdit(message.id, $event)"
              (delete)="onDelete(message.id)"
              (openThread)="onOpenThread(message.id)"
              (react)="onReact(message.id, $event)"
            />
          }
        </div>
      }
      <vc-typing-indicator [users]="typingForChannel()" />
    </section>
  `,
  styles: `
    .timeline {
      flex: 1;
      min-height: 0;
      overflow: auto;
      padding: var(--vc-space-4);
      display: flex;
      flex-direction: column;
      gap: var(--vc-space-3);
    }
    .timeline__list {
      display: flex;
      flex-direction: column;
      gap: var(--vc-space-3);
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
  private readonly scroller = viewChild<ElementRef<HTMLElement>>('scroller');

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
