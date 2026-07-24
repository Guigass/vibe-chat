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
      <div class="composer__main">
        @if (pendingFile()) {
          <div class="composer__file" aria-live="polite">
            <span>{{ pendingFile()!.name }}</span>
            <button type="button" class="ghost" (click)="clearFile()" aria-label="Remover anexo">
              Remover
            </button>
          </div>
        }
        <vc-textarea
          [(value)]="draft"
          [placeholder]="'Mensagem em #' + (channels.activeChannel()?.name || 'channel')"
          [label]="''"
          (keydown)="onKeydown($event)"
        />
      </div>
      <div class="composer__actions">
        <label class="composer__attach">
          <input
            type="file"
            [disabled]="messages.sending()"
            (change)="onFileSelected($event)"
            accept="image/png,image/jpeg,image/webp,image/gif,application/pdf,text/plain"
            aria-label="Anexar arquivo"
          />
          Anexar
        </label>
        <vc-button
          type="submit"
          [disabled]="(!draft().trim() && !pendingFile()) || messages.sending()"
          [loading]="messages.sending()"
        >
          Enviar
        </vc-button>
      </div>
    </form>
  `,
  styles: `
    :host {
      display: block;
      flex: 0 0 auto;
    }
    .composer {
      display: grid;
      grid-template-columns: 1fr auto;
      gap: 0.75rem;
      align-items: end;
      padding: var(--vc-space-4);
      border-top: 1px solid var(--vc-border);
      background: color-mix(in srgb, var(--vc-surface-elevated) 88%, transparent);
    }
    .composer__main {
      display: grid;
      gap: 0.45rem;
    }
    .composer__file {
      display: flex;
      gap: 0.75rem;
      align-items: center;
      font-size: 0.82rem;
      color: var(--vc-ink-muted);
    }
    .composer__file .ghost,
    .composer__attach {
      border: 0;
      background: transparent;
      color: var(--vc-brand);
      cursor: pointer;
      font: inherit;
      padding: 0;
    }
    .composer__actions {
      display: flex;
      gap: 0.65rem;
      align-items: center;
    }
    .composer__attach {
      position: relative;
      overflow: hidden;
      font-size: 0.9rem;
      white-space: nowrap;
    }
    .composer__attach input {
      position: absolute;
      inset: 0;
      opacity: 0;
      cursor: pointer;
    }
    @media (max-width: 720px) {
      .composer {
        grid-template-columns: 1fr;
      }
      .composer__actions {
        justify-content: space-between;
      }
    }
  `,
})
export class Composer {
  readonly messages = inject(MessageStore);
  readonly channels = inject(ChannelStore);
  private readonly hub = inject(ChatHubService);

  readonly draft = signal('');
  readonly pendingFile = signal<File | null>(null);
  private lastTyping = 0;

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    this.pendingFile.set(file);
    input.value = '';
  }

  clearFile(): void {
    this.pendingFile.set(null);
  }

  async onSubmit(event: Event): Promise<void> {
    event.preventDefault();
    const body = this.draft().trim();
    const file = this.pendingFile();
    if (!body && !file) return;
    this.draft.set('');
    this.pendingFile.set(null);
    await this.messages.send(body, file ?? undefined);
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
