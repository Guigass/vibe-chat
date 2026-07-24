import { Component, inject, signal } from '@angular/core';
import { Button, Textarea } from '../../../shared/ui';
import { MessageStore } from '../../../core/services/message.store';
import { ChatHubService } from '../../../core/services/chat-hub.service';
import { ChannelStore } from '../../../core/services/channel.store';

@Component({
  selector: 'vc-composer',
  standalone: true,
  imports: [Button, Textarea],
  template: `
    <form class="composer" (submit)="onSubmit($event)">
      <vc-textarea
        [(value)]="draft"
        [placeholder]="'Mensagem em #' + (channels.activeChannel()?.name || 'channel')"
        [label]="''"
        (keydown)="onKeydown($event)"
      />
      <vc-button type="submit" [disabled]="!draft().trim()" [loading]="messages.sending()">
        Enviar
      </vc-button>
    </form>
  `,
  styles: `
    .composer {
      display: grid;
      grid-template-columns: 1fr auto;
      gap: 0.75rem;
      align-items: end;
      padding: var(--vc-space-4);
      border-top: 1px solid var(--vc-border);
      background: color-mix(in srgb, var(--vc-surface-elevated) 88%, transparent);
    }
    @media (max-width: 720px) {
      .composer {
        grid-template-columns: 1fr;
      }
    }
  `,
})
export class Composer {
  readonly messages = inject(MessageStore);
  readonly channels = inject(ChannelStore);
  private readonly hub = inject(ChatHubService);

  readonly draft = signal('');
  private lastTyping = 0;

  async onSubmit(event: Event): Promise<void> {
    event.preventDefault();
    const body = this.draft().trim();
    if (!body) return;
    this.draft.set('');
    await this.messages.send(body);
  }

  onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      void this.onSubmit(event);
      return;
    }

    const channelId = this.channels.activeChannel()?.id;
    if (!channelId) return;
    const now = Date.now();
    if (now - this.lastTyping > 1500) {
      this.lastTyping = now;
      void this.hub.sendTyping(channelId);
    }
  }
}
