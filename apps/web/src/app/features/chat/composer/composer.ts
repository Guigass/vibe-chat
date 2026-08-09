import { Component, computed, effect, inject, signal } from '@angular/core';
import { Button, Textarea } from '../../../shared/ui';
import { MessageStore } from '../../../core/services/message.store';
import { ChatHubService } from '../../../core/services/chat-hub.service';
import { ChannelStore } from '../../../core/services/channel.store';
import {
  isMessageBodyTooLong,
  measureMessageBodyLength,
  MESSAGE_BODY_COUNTER_THRESHOLD,
  MESSAGE_BODY_MAX_LENGTH,
} from '../../../shared/models/chat.models';
import { AttachmentQueueService } from './attachment-queue.service';
import {
  attachmentIcon,
  collectFilesFromClipboard,
  formatFileSize,
  resolveContentType,
} from './attachment-upload';
import { AudioRecorderService } from './audio-recorder.service';
import { formatDuration } from './audio-recorder';
import { drawAudioWaveform } from '../../../shared/utils/audio';

@Component({
  selector: 'vc-composer',
  standalone: true,
  imports: [Button, Textarea],
  template: `
    <form class="composer" (submit)="onSubmit($event)">
      <div class="composer__main">
        @if (attachments.items().length) {
          <ul class="composer__attachments" aria-label="Anexos pendentes">
            @for (item of attachments.items(); track item.localId) {
              <li class="composer__attachment" [class.is-failed]="item.status === 'failed'">
                <span class="composer__attachment-icon" aria-hidden="true">
                  {{ iconFor(item.file) }}
                </span>
                <div class="composer__attachment-meta">
                  <span class="composer__attachment-name">{{ item.file.name }}</span>
                  <span class="composer__attachment-size">{{ sizeFor(item.file.size) }}</span>
                  @if (item.status === 'uploading' || item.status === 'queued') {
                    <progress
                      class="composer__attachment-progress"
                      [value]="item.progress"
                      max="100"
                      [attr.aria-label]="'Progresso de ' + item.file.name"
                    ></progress>
                  } @else if (item.status === 'failed') {
                    <span class="composer__attachment-error" role="alert">{{ item.error }}</span>
                  } @else if (item.status === 'ready') {
                    <span class="composer__attachment-ready">Pronto</span>
                  }
                </div>
                <div class="composer__attachment-actions">
                  @if (item.status === 'uploading') {
                    <button
                      type="button"
                      class="ghost"
                      (click)="attachments.cancelUpload(item.localId)"
                      [attr.aria-label]="'Cancelar upload de ' + item.file.name"
                    >
                      Cancelar
                    </button>
                  } @else if (item.status === 'failed') {
                    <button
                      type="button"
                      class="ghost"
                      (click)="attachments.retry(item.localId)"
                      [attr.aria-label]="'Tentar novamente ' + item.file.name"
                    >
                      Tentar novamente
                    </button>
                  }
                  <button
                    type="button"
                    class="ghost composer__attachment-remove"
                    (click)="attachments.remove(item.localId)"
                    [attr.aria-label]="'Remover ' + item.file.name"
                  >
                    ×
                  </button>
                </div>
              </li>
            }
          </ul>
        }

        @if (attachments.liveAnnouncement(); as announcement) {
          <p class="vc-sr-only" aria-live="polite">{{ announcement }}</p>
        }

        @if (validationError()) {
          <p class="composer__validation" role="alert">{{ validationError() }}</p>
        }

        <vc-textarea
          [(value)]="draft"
          [placeholder]="'Mensagem em #' + (channels.activeChannel()?.name || 'channel')"
          [label]="''"
          (keydown)="onKeydown($event)"
          (paste)="onPaste($event)"
        />
        @if (showCounter()) {
          <p
            class="composer__counter"
            [class.composer__counter--over]="bodyTooLong()"
            aria-live="polite"
          >
            {{ bodyLength() }} / {{ maxLength }}
          </p>
        }
      </div>
      <div class="composer__actions">
        <label class="composer__attach">
          <input
            type="file"
            multiple
            [disabled]="messages.sending() || !attachments.canAcceptMore()"
            (change)="onFileSelected($event)"
            accept="image/png,image/jpeg,image/webp,image/gif,application/pdf,text/plain"
            aria-label="Anexar arquivo"
          />
          Anexar
        </label>
        @if (audioRecorder.supported) {
          @if (audioRecorder.phase() === 'idle') {
            <button
              type="button"
              class="composer__mic"
              [disabled]="messages.sending() || !attachments.canAcceptMore()"
              (click)="startRecording()"
              aria-label="Gravar áudio"
            >
              Mic
            </button>
          } @else if (audioRecorder.phase() === 'recording') {
            <div class="composer__audio-panel" aria-live="polite">
              <span>{{ formatDuration(audioRecorder.elapsedMs()) }}</span>
              <canvas #liveWave width="120" height="28" aria-hidden="true"></canvas>
              <button type="button" class="ghost" (click)="stopRecording()">Parar</button>
              <button type="button" class="ghost" (click)="discardRecording()">Descartar</button>
            </div>
          } @else if (audioRecorder.phase() === 'preview') {
            <div class="composer__audio-panel">
              @if (audioRecorder.previewUrl(); as previewUrl) {
                <audio [src]="previewUrl" controls aria-label="Prévia do áudio"></audio>
              }
              <button type="button" class="ghost" (click)="discardRecording()">Regravar</button>
              <button type="button" (click)="sendRecording()">Enviar áudio</button>
            </div>
          }
        } @else {
          <span class="composer__mic-hint" title="Use Anexar para enviar áudio">Mic indisponível</span>
        }
        <vc-button
          type="submit"
          [disabled]="submitDisabled()"
          [loading]="messages.sending() || attachments.hasActiveUploads()"
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
    .composer__attachments {
      list-style: none;
      margin: 0;
      padding: 0;
      display: grid;
      gap: 0.45rem;
    }
    .composer__attachment {
      display: grid;
      grid-template-columns: auto 1fr auto;
      gap: 0.55rem;
      align-items: center;
      padding: 0.45rem 0.55rem;
      border: 1px solid var(--vc-border);
      border-radius: var(--vc-radius-sm);
      background: color-mix(in srgb, var(--vc-surface) 92%, transparent);
      font-size: 0.82rem;
    }
    .composer__attachment.is-failed {
      border-color: color-mix(in srgb, var(--vc-danger) 45%, var(--vc-border));
    }
    .composer__attachment-meta {
      display: grid;
      gap: 0.15rem;
      min-width: 0;
    }
    .composer__attachment-name {
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
      color: var(--vc-ink);
    }
    .composer__attachment-size,
    .composer__attachment-ready {
      color: var(--vc-ink-muted);
      font-size: 0.75rem;
    }
    .composer__attachment-error {
      color: var(--vc-danger);
      font-size: 0.75rem;
    }
    .composer__attachment-progress {
      width: 100%;
      height: 0.35rem;
      accent-color: var(--vc-brand);
    }
    .composer__attachment-actions {
      display: flex;
      gap: 0.35rem;
      align-items: center;
    }
    .composer__attachment-remove {
      font-size: 1.1rem;
      line-height: 1;
    }
    .composer__validation {
      margin: 0;
      font-size: 0.78rem;
      color: var(--vc-danger);
    }
    .composer__file .ghost,
    .composer__attach,
    .ghost {
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
    .composer__mic,
    .composer__mic-hint {
      border: 0;
      background: transparent;
      color: var(--vc-brand);
      font: inherit;
      font-size: 0.82rem;
      cursor: pointer;
      padding: 0.35rem 0.5rem;
    }
    .composer__mic-hint {
      color: var(--vc-ink-subtle);
      cursor: help;
      font-size: 0.72rem;
    }
    .composer__audio-panel {
      display: flex;
      flex-wrap: wrap;
      gap: 0.45rem;
      align-items: center;
      font-size: 0.78rem;
      color: var(--vc-ink-muted);
    }
    .composer__audio-panel audio {
      max-width: 12rem;
      height: 1.8rem;
    }
    .composer__counter {
      margin: 0;
      font-size: 0.75rem;
      color: var(--vc-ink-subtle);
      text-align: right;
    }
    .composer__counter--over {
      color: var(--vc-danger);
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
  readonly attachments = inject(AttachmentQueueService);
  readonly audioRecorder = inject(AudioRecorderService);
  private readonly hub = inject(ChatHubService);

  readonly formatDuration = formatDuration;

  readonly draft = signal('');
  readonly validationError = signal<string | null>(null);
  readonly maxLength = MESSAGE_BODY_MAX_LENGTH;
  readonly bodyLength = computed(() => measureMessageBodyLength(this.draft()));
  readonly bodyTooLong = computed(() => isMessageBodyTooLong(this.draft()));
  readonly showCounter = computed(() => this.bodyLength() >= MESSAGE_BODY_COUNTER_THRESHOLD);
  readonly readyCount = computed(() => this.attachments.readyAttachmentIds().length);
  readonly submitDisabled = computed(() => {
    const hasText = !!this.draft().trim();
    const ready = this.readyCount();
    const uploading = this.attachments.hasActiveUploads();
    return (
      (!hasText && ready === 0 && !uploading) ||
      this.messages.sending() ||
      this.bodyTooLong() ||
      this.attachments.submitBlocked()
    );
  });
  private lastTyping = 0;

  constructor() {
    effect(() => {
      const prefill = this.channels.composerPrefill();
      if (prefill === null) return;
      const text = this.channels.consumeComposerPrefill();
      if (text) {
        this.draft.set(text);
      }
    });

    effect(() => {
      this.channels.activeChannel()?.id;
      this.attachments.clear();
      this.audioRecorder.reset();
      this.validationError.set(null);
    });

    effect(() => {
      if (this.audioRecorder.phase() !== 'recording') return;
      const canvas = document.querySelector('.composer__audio-panel canvas');
      drawAudioWaveform(canvas as HTMLCanvasElement | null, this.audioRecorder.liveWaveform());
    });
  }

  iconFor(file: File): string {
    return attachmentIcon(resolveContentType(file));
  }

  sizeFor(bytes: number): string {
    return formatFileSize(bytes);
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    input.value = '';
    this.queueFiles(files);
  }

  onPaste(event: ClipboardEvent): void {
    const files = collectFilesFromClipboard(event.clipboardData);
    if (!files.length) return;
    event.preventDefault();
    this.queueFiles(files);
  }

  queueFiles(files: File[]): void {
    const error = this.attachments.addFiles(files);
    this.validationError.set(error);
  }

  async startRecording(): Promise<void> {
    const error = await this.audioRecorder.start();
    this.validationError.set(error);
  }

  stopRecording(): void {
    this.audioRecorder.stop();
  }

  discardRecording(): void {
    this.audioRecorder.discard();
  }

  async sendRecording(): Promise<void> {
    const channelId = this.channels.activeChannel()?.id;
    if (!channelId) return;
    const recorded = await this.audioRecorder.buildRecordedAudio();
    if (!recorded) {
      this.validationError.set(this.audioRecorder.errorMessage());
      return;
    }

    const result = await this.attachments.uploadRecordedAudio(channelId, recorded);
    if (result.error) {
      this.validationError.set(result.error);
      return;
    }

    const ok = await this.messages.send('', result.attachmentId ? [result.attachmentId] : []);
    if (ok) {
      this.audioRecorder.reset();
      this.validationError.set(null);
    }
  }

  async onSubmit(event: Event): Promise<void> {
    event.preventDefault();
    const body = this.draft().trim();
    if (isMessageBodyTooLong(body)) return;

    const attachmentIds = await this.attachments.waitForReady();
    if (!body && attachmentIds.length === 0) return;

    const ok = await this.messages.send(body, attachmentIds);
    if (ok) {
      this.draft.set('');
      this.attachments.clear();
      this.validationError.set(null);
    }
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
