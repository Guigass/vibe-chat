import { DatePipe } from '@angular/common';
import { Component, computed, effect, inject, input, output, signal } from '@angular/core';
import {
  ChatMessage,
  MessageAttachment,
  REACTION_EMOJI_OPTIONS,
  isMessageBodyTooLong,
  measureMessageBodyLength,
  MESSAGE_BODY_COUNTER_THRESHOLD,
  MESSAGE_BODY_MAX_LENGTH,
} from '../../models/chat.models';
import { Avatar } from '../avatar/avatar';
import { AudioMessage } from '../audio-message/audio-message';
import { ApiService } from '../../../core/api/api.service';
import { ChannelStore } from '../../../core/services/channel.store';
import { environment } from '../../../../environments/environment';

@Component({
  selector: 'vc-message-bubble',
  standalone: true,
  imports: [Avatar, DatePipe, AudioMessage],
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
            @if (showEditCounter()) {
              <p
                class="vc-msg__edit-counter"
                [class.vc-msg__edit-counter--over]="editTooLong()"
                aria-live="polite"
              >
                {{ editLength() }} / {{ maxLength }}
              </p>
            }
            <div class="vc-msg__edit-actions">
              <button type="button" [disabled]="editSaveDisabled()" (click)="saveEdit()">Salvar</button>
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
                  @if (attachment.kind === 'Audio') {
                    <vc-audio-message
                      [attachment]="attachment"
                      [downloadUrl]="downloadUrls()[attachment.id] ?? null"
                    />
                    @if (transcribeEnabled()) {
                      <button type="button" class="vc-msg__transcribe" (click)="transcribe(attachment)">
                        Transcrever
                      </button>
                    }
                  } @else {
                    <button type="button" (click)="download(attachment)">
                      {{ attachment.fileName }}
                      <span>{{ formatSize(attachment.sizeBytes) }}</span>
                    </button>
                  }
                </li>
              }
            </ul>
            @if (transcript()) {
              <p class="vc-msg__transcript">{{ transcript() }}</p>
            }
          }
          @if (message().reactions?.length) {
            <ul class="vc-msg__reactions" aria-label="Reações">
              @for (reaction of message().reactions; track reaction.emoji) {
                <li>
                  <button
                    type="button"
                    [class.active]="reaction.me"
                    [attr.aria-pressed]="reaction.me"
                    [attr.aria-label]="'Reação ' + reaction.emoji"
                    (click)="react.emit(reaction.emoji)"
                  >
                    <span aria-hidden="true">{{ reaction.emoji }}</span>
                    <span>{{ reaction.count }}</span>
                  </button>
                </li>
              }
            </ul>
          }
        }

        @if (!message().deletedAt && !editing() && message().status === 'persisted') {
          <div class="vc-msg__actions">
            <div class="vc-msg__react-picker" role="group" aria-label="Adicionar reação">
              @for (emoji of emojiOptions; track emoji) {
                <button type="button" [attr.aria-label]="'Reagir com ' + emoji" (click)="react.emit(emoji)">
                  {{ emoji }}
                </button>
              }
            </div>
            @if (showThreadAction()) {
              <button type="button" (click)="openThread.emit()">
                @if (message().replyCount) {
                  {{ message().replyCount }} {{ message().replyCount === 1 ? 'resposta' : 'respostas' }}
                } @else {
                  Responder
                }
              </button>
            }
            @if (message().mine) {
              <button type="button" (click)="startEdit()">Editar</button>
              <button type="button" class="danger" (click)="delete.emit()">Apagar</button>
            }
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
    .vc-msg__transcribe {
      margin-top: 0.25rem;
      font-size: 0.72rem;
    }
    .vc-msg__transcript {
      margin-top: 0.35rem;
      font-size: 0.82rem;
      color: var(--vc-ink-muted);
      border-left: 2px solid var(--vc-border);
      padding-left: 0.55rem;
    }
    .vc-msg__attachments span {
      color: var(--vc-ink-subtle);
      font-size: 0.72rem;
    }
    .vc-msg__reactions {
      list-style: none;
      margin: 0.5rem 0 0;
      padding: 0;
      display: flex;
      flex-wrap: wrap;
      gap: 0.35rem;
    }
    .vc-msg__reactions button {
      border: 1px solid var(--vc-border);
      background: color-mix(in srgb, var(--vc-surface) 88%, var(--vc-brand));
      color: var(--vc-ink);
      border-radius: var(--vc-radius-sm);
      font: inherit;
      font-size: 0.78rem;
      padding: 0.12rem 0.4rem;
      cursor: pointer;
      display: inline-flex;
      gap: 0.28rem;
      align-items: center;
    }
    .vc-msg__reactions button.active {
      border-color: color-mix(in srgb, var(--vc-brand) 45%, var(--vc-border));
      background: color-mix(in srgb, var(--vc-brand) 16%, var(--vc-surface));
    }
    .vc-msg__actions,
    .vc-msg__edit-actions {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
      align-items: center;
      margin-top: 0.45rem;
    }
    .vc-msg__react-picker {
      display: inline-flex;
      gap: 0.15rem;
      margin-right: 0.25rem;
    }
    .vc-msg__react-picker button {
      border: 0;
      background: transparent;
      font-size: 0.9rem;
      line-height: 1;
      cursor: pointer;
      padding: 0.1rem;
      opacity: 0.55;
    }
    .vc-msg__react-picker button:hover,
    .vc-msg__react-picker button:focus-visible {
      opacity: 1;
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
    .vc-msg__actions > button:hover,
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
    .vc-msg__edit-counter {
      margin: 0.35rem 0 0;
      font-size: 0.72rem;
      color: var(--vc-ink-subtle);
      text-align: right;
    }
    .vc-msg__edit-counter--over {
      color: var(--vc-danger);
    }
  `,
})
export class MessageBubble {
  private readonly api = inject(ApiService);
  private readonly channels = inject(ChannelStore);

  readonly message = input.required<ChatMessage>();
  readonly showThreadAction = input(false);
  readonly edit = output<string>();
  readonly delete = output<void>();
  readonly openThread = output<void>();
  readonly react = output<string>();

  readonly editing = signal(false);
  readonly draft = signal('');
  readonly transcript = signal<string | null>(null);
  readonly downloadUrls = signal<Record<string, string>>({});
  readonly emojiOptions = REACTION_EMOJI_OPTIONS;
  readonly maxLength = MESSAGE_BODY_MAX_LENGTH;
  readonly editLength = computed(() => measureMessageBodyLength(this.draft()));
  readonly editTooLong = computed(() => isMessageBodyTooLong(this.draft()));
  readonly showEditCounter = computed(() => this.editLength() >= MESSAGE_BODY_COUNTER_THRESHOLD);
  readonly editSaveDisabled = computed(
    () => !this.draft().trim() || this.editTooLong(),
  );
  readonly transcribeEnabled = computed(
    () => environment.aiTranscribeEnabled && environment.aiSummarizeEnabled,
  );

  constructor() {
    effect(() => {
      const attachments = this.message().attachments ?? [];
      const channelId = this.message().channelId;
      for (const attachment of attachments) {
        if (attachment.kind !== 'Audio' || this.downloadUrls()[attachment.id]) continue;
        void this.loadAudioUrl(channelId, attachment.id);
      }
    });
  }

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
    if (!value || isMessageBodyTooLong(value)) return;
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

  async transcribe(attachment: MessageAttachment): Promise<void> {
    const workspace = this.channels.activeWorkspace();
    const channelId = this.message().channelId;
    if (!workspace || !channelId) return;

    try {
      const result = await this.api.transcribeAttachment({
        workspaceId: workspace.id,
        channelId,
        messageId: this.message().id,
        attachmentId: attachment.id,
      });
      this.transcript.set(result.text);
    } catch {
      this.transcript.set('Transcrição indisponível.');
    }
  }

  formatSize(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }

  private async loadAudioUrl(channelId: string, attachmentId: string): Promise<void> {
    try {
      const result = await this.api.getAttachmentDownload(channelId, attachmentId);
      this.downloadUrls.update((current) => ({ ...current, [attachmentId]: result.downloadUrl }));
    } catch {
      // playback stays disabled until URL resolves
    }
  }
}
