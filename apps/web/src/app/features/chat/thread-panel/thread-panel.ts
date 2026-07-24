import { Component, effect, ElementRef, inject, signal, viewChild } from '@angular/core';
import { ThreadStore } from '../../../core/services/thread.store';
import { ChatHubService } from '../../../core/services/chat-hub.service';
import { Button, EmptyState, IconButton, MessageBubble, Skeleton, Textarea } from '../../../shared/ui';

@Component({
  selector: 'vc-thread-panel',
  standalone: true,
  imports: [Button, EmptyState, IconButton, MessageBubble, Skeleton, Textarea],
  template: `
    <aside class="thread" aria-label="Thread">
      <header class="thread__header">
        <div>
          <h2>Thread</h2>
          @if (threads.active(); as active) {
            <p>{{ active.replyCount }} {{ active.replyCount === 1 ? 'resposta' : 'respostas' }}</p>
          }
        </div>
        <vc-icon-button label="Fechar thread" (click)="threads.close()">
          <span aria-hidden="true">×</span>
        </vc-icon-button>
      </header>

      <div class="thread__scroll" #scroller>
        @if (threads.loading()) {
          <div class="thread__loading">
            <vc-skeleton height="3rem" />
            <vc-skeleton height="3rem" width="85%" />
          </div>
        } @else if (threads.active(); as active) {
          @if (active.parentMessage; as parent) {
            <div class="thread__parent">
              <vc-message-bubble [message]="parent" />
            </div>
          }

          @if (!threads.sortedMessages().length) {
            <vc-empty-state
              title="Sem respostas ainda"
              description="Escreva abaixo para continuar a conversa nesta thread."
            />
          } @else {
            <div class="thread__list">
              @for (message of threads.sortedMessages(); track message.id) {
                <vc-message-bubble [message]="message" />
              }
            </div>
          }
        }
      </div>

      <form class="thread__composer" (submit)="onSubmit($event)">
        <vc-textarea
          [(value)]="draft"
          placeholder="Responder na thread…"
          [label]="''"
          (keydown)="onKeydown($event)"
        />
        <vc-button type="submit" [disabled]="!draft().trim() || threads.sending()" [loading]="threads.sending()">
          Responder
        </vc-button>
      </form>
    </aside>
  `,
  styles: `
    .thread {
      display: grid;
      grid-template-rows: auto 1fr auto;
      min-height: 100dvh;
      border-left: 1px solid var(--vc-border);
      background: color-mix(in srgb, var(--vc-surface-elevated) 94%, transparent);
      animation: vc-thread-in 220ms ease-out;
    }
    .thread__header {
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
      gap: 0.75rem;
      padding: var(--vc-space-4);
      border-bottom: 1px solid var(--vc-border);
    }
    .thread__header h2 {
      margin: 0;
      font-family: var(--vc-font-display);
      font-size: 1.05rem;
    }
    .thread__header p {
      margin: 0.2rem 0 0;
      color: var(--vc-ink-muted);
      font-size: 0.8rem;
    }
    .thread__scroll {
      overflow: auto;
      padding: var(--vc-space-4);
      display: flex;
      flex-direction: column;
      gap: var(--vc-space-3);
    }
    .thread__parent {
      padding-bottom: var(--vc-space-3);
      border-bottom: 1px dashed var(--vc-border);
    }
    .thread__list,
    .thread__loading {
      display: flex;
      flex-direction: column;
      gap: var(--vc-space-3);
    }
    .thread__composer {
      display: grid;
      gap: 0.65rem;
      padding: var(--vc-space-4);
      border-top: 1px solid var(--vc-border);
    }
    @keyframes vc-thread-in {
      from {
        opacity: 0;
        transform: translateX(0.6rem);
      }
      to {
        opacity: 1;
        transform: translateX(0);
      }
    }
  `,
})
export class ThreadPanel {
  readonly threads = inject(ThreadStore);
  private readonly hub = inject(ChatHubService);
  private readonly scroller = viewChild<ElementRef<HTMLElement>>('scroller');
  readonly draft = signal('');
  private lastTyping = 0;

  constructor() {
    effect(() => {
      this.threads.sortedMessages();
      queueMicrotask(() => {
        const el = this.scroller()?.nativeElement;
        if (el) el.scrollTop = el.scrollHeight;
      });
    });
  }

  async onSubmit(event: Event): Promise<void> {
    event.preventDefault();
    const body = this.draft().trim();
    if (!body) return;
    this.draft.set('');
    await this.threads.send(body);
  }

  onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      void this.onSubmit(event);
      return;
    }

    const channelId = this.threads.active()?.channelId;
    if (!channelId) return;
    const now = Date.now();
    if (now - this.lastTyping > 1500) {
      this.lastTyping = now;
      void this.hub.sendTyping(channelId);
    }
  }
}
