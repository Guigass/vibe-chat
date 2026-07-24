import { DatePipe } from '@angular/common';
import { Component, inject, input, output, signal } from '@angular/core';
import { ChatMessage, MessageAttachment } from '../../models/chat.models';
import { Avatar } from '../avatar/avatar';
import { ApiService } from '../../../core/api/api.service';

@Component({
  selector: 'vc-message-bubble',
  standalone: true,
  imports: [Avatar, DatePipe],
  template: `
    <article
      class="vc-msg vc-anim-fade-in"
      [class.vc-msg--mine]="message().mine"
      [class.vc-msg--deleted]="!!message().deletedAt"
      [attr.data-status]="message().status"
    >
      @if (!message().mine) {
        <vc-avatar [name]="message().authorName" [size]="34" />
      }
      <div class="vc-msg__body">
        <header>
          <strong>{{ message().authorName }}</strong>
          <time [attr.datetime]="message().createdAt">{{ message().createdAt | date: 'shortTime' }}</time>
          @if (message().editedAt && !message().deletedAt) {
            <span class="vc-msg__status">editada</span>
          }
          @if (message().status === 'sending') {
            <span class="vc-msg__status">enviando…</span>
          } @else if (message().status === 'sent') {
            <span class="vc-msg__status">enviada</span>
          } @else if (message().status === 'failed') {
            <span class="vc-msg__status vc-msg__status--fail">falhou</span>
          } @else if (message().status === 'persisted' && !message().editedAt && !message().deletedAt) {
            <span class="vc-msg__status">salva</span>
          }
        </header>

        @if (message().deletedAt) {
          <p class="vc-msg__deleted">Mensagem removida</p>
        } @else if (editing()) {
          <div class="vc-msg__edit">
            <textarea
              [value]="draft()"
              (input)="draft.set(($any($event.target).value))"
              rows="3"
              aria-label="Editar mensagem"
            ></textarea>
            <div class="vc-msg__edit-actions">
              <button type="button" (click)="saveEdit()">Salvar</button>
              <button type="button" class="ghost" (click)="cancelEdit()">Cancelar</button>
            </div>
          </div>
        } @else {
          @if (message().body) {
            <p>{{ message().body }}</p>
          }
          @if (message().attachments?.length) {
            <ul class="vc-msg__attachments">
              @for (attachment of message().attachments; track attachment.id) {
                <li>
                  <button type="button" (click)="download(attachment)">
                    {{ attachment.fileName }}
                    <span>{{ formatSize(attachment.sizeBytes) }}</span>
                  </button>
                </li>
              }
            </ul>
          }
        }

        @if (message().mine && !message().deletedAt && message().status === 'persisted' && !editing()) {
          <div class="vc-msg__actions">
            <button type="button" (click)="startEdit()">Editar</button>
            <button type="button" class="danger" (click)="delete.emit()">Apagar</button>
          </div>
        }
      </div>
    </article>
  `,
  styles: `
    .vc-msg {
      display: flex;
      gap: 0.7rem;
      align-items: flex-start;
      max-width: min(720px, 100%);
    }
    .vc-msg--mine {
      margin-left: auto;
      flex-direction: row-reverse;
    }
    .vc-msg__body {
      padding: 0.65rem 0.85rem;
      border-radius: var(--vc-radius-md);
      background: var(--vc-msg-theirs);
      border: 1px solid var(--vc-border);
      min-width: 12rem;
    }
    .vc-msg--mine .vc-msg__body {
      background: var(--vc-msg-mine);
      border-color: color-mix(in srgb, var(--vc-brand) 28%, var(--vc-border));
    }
    .vc-msg--deleted .vc-msg__body {
      opacity: 0.72;
    }
    header {
      display: flex;
      flex-wrap: wrap;
      gap: 0.45rem;
      align-items: baseline;
      margin-bottom: 0.25rem;
    }
    strong {
      font-size: 0.88rem;
      font-family: var(--vc-font-display);
    }
    time,
    .vc-msg__status {
      font-size: 0.72rem;
      color: var(--vc-ink-subtle);
    }
    .vc-msg__status--fail {
      color: var(--vc-danger);
    }
    p {
      margin: 0;
      white-space: pre-wrap;
      line-height: 1.45;
      word-break: break-word;
    }
    .vc-msg__deleted {
      font-style: italic;
      color: var(--vc-ink-subtle);
    }
    .vc-msg__attachments {
      list-style: none;
      margin: 0.45rem 0 0;
      padding: 0;
      display: grid;
      gap: 0.3rem;
    }
    .vc-msg__attachments button {
      border: 0;
      background: transparent;
      color: var(--vc-brand);
      cursor: pointer;
      font: inherit;
      font-size: 0.84rem;
      padding: 0;
      display: inline-flex;
      gap: 0.45rem;
      align-items: baseline;
    }
    .vc-msg__attachments span {
      color: var(--vc-ink-subtle);
      font-size: 0.72rem;
    }
    .vc-msg__actions,
    .vc-msg__edit-actions {
      display: flex;
      gap: 0.5rem;
      margin-top: 0.45rem;
    }
    .vc-msg__actions button,
    .vc-msg__edit-actions button {
      border: 0;
      background: transparent;
      color: var(--vc-ink-muted);
      font-size: 0.75rem;
      cursor: pointer;
      padding: 0;
    }
    .vc-msg__actions button:hover,
    .vc-msg__edit-actions button:hover {
      color: var(--vc-ink);
    }
    .vc-msg__actions .danger {
      color: var(--vc-danger);
    }
    .vc-msg__edit textarea {
      width: 100%;
      resize: vertical;
      min-height: 4rem;
      border-radius: var(--vc-radius-sm);
      border: 1px solid var(--vc-border);
      background: var(--vc-surface);
      color: var(--vc-ink);
      font: inherit;
      padding: 0.5rem 0.65rem;
    }
  `,
})
export class MessageBubble {
  private readonly api = inject(ApiService);

  readonly message = input.required<ChatMessage>();
  readonly edit = output<string>();
  readonly delete = output<void>();

  readonly editing = signal(false);
  readonly draft = signal('');

  startEdit(): void {
    this.draft.set(this.message().body);
    this.editing.set(true);
  }

  cancelEdit(): void {
    this.editing.set(false);
    this.draft.set('');
  }

  saveEdit(): void {
    const value = this.draft().trim();
    if (!value) return;
    this.edit.emit(value);
    this.editing.set(false);
  }

  async download(attachment: MessageAttachment): Promise<void> {
    const channelId = this.message().channelId;
    if (!channelId) return;
    try {
      const result = await this.api.getAttachmentDownload(channelId, attachment.id);
      window.open(result.downloadUrl, '_blank', 'noopener,noreferrer');
    } catch {
      // keep UI quiet; connection banner / toast stack not present in MVP shell
    }
  }

  formatSize(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }
}
