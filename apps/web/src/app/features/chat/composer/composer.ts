import { Component, computed, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { Button, IconButton, Textarea } from '../../../shared/ui';
import {
  applyMarkdownWrap,
  handleMarkdownShortcut,
  updateTextareaSelection,
} from '../../../shared/markdown/markdown-format';
import {
  composerMentionDisplay,
  detectMentionQuery,
  encodeMentionPlainText,
  filterMentionItems,
  formatMentionPlainText,
  insertMentionToken,
  MentionAutocompleteItem,
} from '../../../shared/markdown/mention-tokens';
import {
  detectSlashQuery,
  filterSlashCommands,
  insertSlashCommand,
  looksLikeSlashCommand,
  SlashCommandDef,
} from '../../../shared/markdown/slash-tokens';
import { AuthService } from '../../../core/auth/auth.service';
import { ApiService } from '../../../core/api/api.service';
import { MessageStore } from '../../../core/services/message.store';
import { replyPreviewText } from '../../../core/services/message-sync';
import { ChatHubService } from '../../../core/services/chat-hub.service';
import { ChannelStore } from '../../../core/services/channel.store';
import { DraftStoreService } from '../../../core/services/draft-store.service';
import {
  isMessageBodyTooLong,
  measureMessageBodyLength,
  MESSAGE_BODY_COUNTER_THRESHOLD,
  MESSAGE_BODY_MAX_LENGTH,
} from '../../../shared/models/chat.models';
import { AttachmentQueueService } from './attachment-queue.service';
import {
  attachmentIconKind,
  collectFilesFromClipboard,
  formatFileSize,
  formatVideoDuration,
  isVideoContentType,
  resolveContentType,
} from './attachment-upload';
import type { AttachmentIconKind, PendingAttachment } from './attachment-upload';
import { AudioRecorderService } from './audio-recorder.service';
import { formatDuration } from './audio-recorder';
import { drawAudioWaveform } from '../../../shared/utils/audio';
import { MentionAutocomplete } from './mention-autocomplete';
import { SlashAutocomplete } from './slash-autocomplete';
import { SlashCommandsService } from './slash-commands.service';
import { EmojiPicker } from '../../../shared/ui/emoji-picker/emoji-picker';
import { rememberRecentEmoji } from '../../../shared/emoji/emoji-data';

@Component({
  selector: 'vc-composer',
  standalone: true,
  imports: [Button, IconButton, Textarea, MentionAutocomplete, SlashAutocomplete, EmojiPicker],
  template: `
    <form class="composer" (submit)="onSubmit($event)">
      <div class="composer__main">
        @if (messages.replyTarget(); as cite) {
          <div class="composer__reply" role="status">
            <div class="composer__reply-meta">
              <strong>Respondendo a {{ cite.authorName }}</strong>
              <span>{{ citePreview(cite.body) }}</span>
            </div>
            <button
              type="button"
              class="ghost"
              aria-label="Cancelar citação"
              (click)="messages.clearReplyTarget()"
            >
              ×
            </button>
          </div>
        }
        @if (messages.editingMessage(); as editing) {
          <div class="composer__reply" role="status">
            <div class="composer__reply-meta">
              <strong>Editando mensagem</strong>
              <span>{{ citePreview(editing.body) }}</span>
            </div>
            <button
              type="button"
              class="ghost"
              aria-label="Cancelar edição"
              (click)="cancelEdit()"
            >
              ×
            </button>
          </div>
        }
        @if (attachments.items().length) {
          <ul class="composer__attachments" aria-label="Anexos pendentes">
            @for (item of attachments.items(); track item.localId) {
              <li class="composer__attachment" [class.is-failed]="item.status === 'failed'">
                <span class="composer__attachment-icon" aria-hidden="true">
                  @switch (iconKindFor(item.file)) {
                    @case ('image') {
                      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
                        <rect width="18" height="18" x="3" y="3" rx="2" ry="2" />
                        <circle cx="9" cy="9" r="2" />
                        <path d="m21 15-3.086-3.086a2 2 0 0 0-2.828 0L6 21" />
                      </svg>
                    }
                    @case ('pdf') {
                      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
                        <path d="M6 22a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h8a2.4 2.4 0 0 1 1.704.706l3.588 3.588A2.4 2.4 0 0 1 20 8v12a2 2 0 0 1-2 2z" />
                        <path d="M14 2v5a1 1 0 0 0 1 1h5" />
                        <path d="M10 9H8" />
                        <path d="M16 13H8" />
                        <path d="M16 17H8" />
                      </svg>
                    }
                    @case ('text') {
                      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
                        <path d="M6 22a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h8a2.4 2.4 0 0 1 1.704.706l3.588 3.588A2.4 2.4 0 0 1 20 8v12a2 2 0 0 1-2 2z" />
                        <path d="M14 2v5a1 1 0 0 0 1 1h5" />
                        <path d="M10 9H8" />
                        <path d="M16 13H8" />
                        <path d="M16 17H8" />
                      </svg>
                    }
                    @case ('video') {
                      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
                        <path d="m16 13 5.223 3.482a.5.5 0 0 0 .777-.416V7.87a.5.5 0 0 0-.752-.432L16 10.5" />
                        <rect x="2" y="6" width="14" height="12" rx="2" />
                      </svg>
                    }
                    @default {
                      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
                        <path d="M6 22a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h8a2.4 2.4 0 0 1 1.704.706l3.588 3.588A2.4 2.4 0 0 1 20 8v12a2 2 0 0 1-2 2z" />
                        <path d="M14 2v5a1 1 0 0 0 1 1h5" />
                      </svg>
                    }
                  }
                </span>
                @if (item.previewUrl) {
                  <video
                    class="composer__attachment-video"
                    [src]="item.previewUrl"
                    controls
                    preload="metadata"
                    [attr.aria-label]="'Prévia de ' + item.file.name"
                  ></video>
                }
                <div class="composer__attachment-meta">
                  <span class="composer__attachment-name">{{ item.file.name }}</span>
                  <span class="composer__attachment-size">
                    {{ sizeFor(item) }}
                    @if (item.durationMs) {
                      · {{ formatVideoDuration(item.durationMs) }}
                    }
                  </span>
                  @if (item.status === 'validating') {
                    <span class="composer__attachment-ready">Validando vídeo…</span>
                  } @else if (item.status === 'uploading' || item.status === 'queued') {
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

        @if (slash.notice(); as notice) {
          <aside
            class="composer__notice"
            [class.composer__notice--error]="notice.kind === 'error'"
            [attr.role]="notice.kind === 'error' ? 'alert' : 'status'"
          >
            <header>
              <strong>
                @switch (notice.kind) {
                  @case ('help') { Ajuda }
                  @case ('summary') { Resumo }
                  @case ('error') { Comando }
                  @default { Comando }
                }
              </strong>
              <button type="button" class="ghost" aria-label="Fechar" (click)="slash.clearNotice()">
                ×
              </button>
            </header>
            <p>{{ notice.text }}</p>
            @if (notice.lines?.length) {
              <ul>
                @for (line of notice.lines; track line) {
                  <li>{{ line }}</li>
                }
              </ul>
            }
          </aside>
        }

        <div class="composer__shell">
          <div class="composer__format" role="toolbar" aria-label="Formatação de texto">
            <vc-icon-button label="Negrito" (click)="applyFormat('bold')">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                <path d="M6 12h9a4 4 0 0 1 0 8H7a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1h7a4 4 0 0 1 0 8" />
              </svg>
            </vc-icon-button>
            <vc-icon-button label="Itálico" (click)="applyFormat('italic')">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                <line x1="19" x2="10" y1="4" y2="4" />
                <line x1="14" x2="5" y1="20" y2="20" />
                <line x1="15" x2="9" y1="4" y2="20" />
              </svg>
            </vc-icon-button>
            <vc-icon-button label="Riscado" (click)="applyFormat('strike')">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                <path d="M16 4H9a3 3 0 0 0-2.83 4" />
                <path d="M14 12a4 4 0 0 1 0 8H6" />
                <line x1="4" x2="20" y1="12" y2="12" />
              </svg>
            </vc-icon-button>
            <vc-icon-button label="Código inline" (click)="applyFormat('code')">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                <path d="m16 18 6-6-6-6" />
                <path d="m8 6-6 6 6 6" />
              </svg>
            </vc-icon-button>
            <div class="composer__emoji">
              <button
                type="button"
                class="composer__icon-btn"
                aria-label="Inserir emoji"
                aria-haspopup="dialog"
                [attr.aria-expanded]="emojiPickerOpen()"
                (click)="toggleEmojiPicker($event)"
              >
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                  <path d="M22 11v1a10 10 0 1 1-9-10" />
                  <path d="M8 14s1.5 2 4 2 4-2 4-2" />
                  <line x1="9" x2="9.01" y1="9" y2="9" />
                  <line x1="15" x2="15.01" y1="9" y2="9" />
                  <path d="M16 5h6" />
                  <path d="M19 2v6" />
                </svg>
              </button>
              <vc-emoji-picker
                [open]="emojiPickerOpen()"
                (select)="insertEmoji($event)"
                (closed)="emojiPickerOpen.set(false)"
              />
            </div>
          </div>

          <div class="composer__input-wrap">
            <vc-textarea
              #composerTextarea
              [(value)]="draft"
              [placeholder]="
                messages.editingMessage()
                  ? 'Editar mensagem'
                  : 'Mensagem em #' + (channels.activeChannel()?.name || 'channel')
              "
              [label]="''"
              (keydown)="onKeydown($event)"
              (textInput)="onInput()"
              (paste)="onPaste($event)"
            />
            <vc-mention-autocomplete
              [open]="mentionOpen()"
              [items]="mentionItems()"
              [activeIndex]="mentionActiveIndex()"
              (select)="applyMention($event)"
            />
            <vc-slash-autocomplete
              [open]="slashOpen()"
              [items]="slashItems()"
              [activeIndex]="slashActiveIndex()"
              (select)="applySlash($event)"
            />
          </div>

          <div class="composer__actions">
            @if (!messages.editingMessage()) {
            <label class="composer__attach">
              <input
                type="file"
                multiple
                [disabled]="messages.sending() || !attachments.canAcceptMore()"
                (change)="onFileSelected($event)"
                accept="image/png,image/jpeg,image/webp,image/gif,application/pdf,text/plain,video/mp4,video/webm"
                aria-label="Anexar arquivo"
              />
              <span class="composer__attach-face" aria-hidden="true">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
                  <path d="m16 6-8.414 8.586a2 2 0 0 0 2.829 2.829l8.414-8.586a4 4 0 1 0-5.657-5.657l-8.379 8.551a6 6 0 1 0 8.485 8.485l8.379-8.551" />
                </svg>
              </span>
            </label>
            @if (audioRecorder.supported) {
              @if (audioRecorder.phase() === 'idle') {
                <vc-icon-button
                  label="Gravar áudio"
                  [disabled]="messages.sending() || !attachments.canAcceptMore()"
                  (click)="startRecording()"
                >
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                    <path d="M12 19v3" />
                    <path d="M19 10v2a7 7 0 0 1-14 0v-2" />
                    <rect x="9" y="2" width="6" height="13" rx="3" />
                  </svg>
                </vc-icon-button>
              } @else if (audioRecorder.phase() === 'recording') {
                <div class="composer__audio-panel" aria-live="polite">
                  <span class="composer__audio-timer">{{ formatDuration(audioRecorder.elapsedMs()) }}</span>
                  <canvas #liveWave width="120" height="28" aria-hidden="true"></canvas>
                  <button type="button" class="composer__audio-btn" (click)="stopRecording()" aria-label="Parar gravação">
                      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                        <rect width="18" height="18" x="3" y="3" rx="2" />
                      </svg>
                      Parar
                    </button>
                  <button type="button" class="composer__audio-btn" (click)="discardRecording()" aria-label="Descartar gravação">
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                      <path d="M10 11v6" />
                      <path d="M14 11v6" />
                      <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6" />
                      <path d="M3 6h18" />
                      <path d="M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2" />
                    </svg>
                    Descartar
                  </button>
                </div>
              } @else if (audioRecorder.phase() === 'preview') {
                <div class="composer__audio-panel">
                  @if (audioRecorder.previewUrl(); as previewUrl) {
                    <audio [src]="previewUrl" controls aria-label="Prévia do áudio"></audio>
                  }
                  <button type="button" class="composer__audio-btn" (click)="discardRecording()" aria-label="Regravar áudio">
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                      <path d="M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8" />
                      <path d="M3 3v5h5" />
                    </svg>
                    Regravar
                  </button>
                </div>
              }
            } @else {
              <span class="composer__mic-hint" title="Use o anexo para enviar áudio">Mic indisponível</span>
            }
            }
            <vc-button
              type="submit"
              [disabled]="submitDisabled()"
              [loading]="messages.sending() || attachments.hasActiveUploads() || sendingAudio()"
            >
              {{ primarySubmitLabel() }}
            </vc-button>
          </div>
        </div>
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
    </form>
  `,
  styles: `
    :host {
      display: block;
      position: relative;
      z-index: 6;
      flex: 0 0 auto;
    }
    .composer {
      padding: var(--vc-composer-pad);
      border-top: 1px solid var(--vc-border);
      background: color-mix(in srgb, var(--vc-surface-elevated) 88%, transparent);
      overflow: visible;
    }
    .composer__main {
      display: grid;
      gap: var(--vc-space-2);
    }
    .composer__reply {
      display: grid;
      grid-template-columns: 1fr auto;
      gap: 0.45rem;
      align-items: start;
      padding: 0.35rem 0.5rem;
      border-left: 3px solid var(--vc-brand);
      border-radius: 0 var(--vc-radius-sm) var(--vc-radius-sm) 0;
      background: color-mix(in srgb, var(--vc-brand) 8%, transparent);
    }
    .composer__reply-meta {
      display: grid;
      gap: 0.1rem;
      min-width: 0;
    }
    .composer__reply-meta strong {
      font-size: 0.75rem;
      color: var(--vc-brand);
    }
    .composer__reply-meta span {
      font-size: 0.72rem;
      color: var(--vc-ink-muted);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .composer__shell {
      display: grid;
      grid-template-columns: 1fr auto;
      grid-template-rows: auto auto;
      gap: 0.15rem 0.35rem;
      padding: 0.3rem 0.4rem 0.35rem;
      border: 1px solid var(--vc-border);
      border-radius: var(--vc-radius-md);
      background: var(--vc-composer-bg);
      overflow: visible;
      transition:
        border-color var(--vc-dur-fast) var(--vc-ease-out),
        box-shadow var(--vc-dur-fast) var(--vc-ease-out);
    }
    .composer__shell:focus-within {
      border-color: color-mix(in srgb, var(--vc-brand) 55%, var(--vc-border));
      box-shadow: 0 0 0 1px color-mix(in srgb, var(--vc-brand) 22%, transparent);
    }
    .composer__input-wrap {
      position: relative;
      display: grid;
      gap: 0.25rem;
      grid-column: 1 / -1;
      grid-row: 1;
    }
    .composer__input-wrap ::ng-deep .vc-field__control {
      border: 0;
      background: transparent;
      min-height: 1.85rem;
      max-height: 8rem;
      padding: 0.2rem 0.35rem;
      line-height: 1.4;
      font-size: 0.92rem;
      border-radius: 0;
      box-shadow: none;
    }
    .composer__input-wrap ::ng-deep .vc-field__control:focus,
    .composer__input-wrap ::ng-deep .vc-field__control:focus-visible {
      outline: none;
      border: 0;
      box-shadow: none;
    }
    .composer__input-wrap vc-mention-autocomplete,
    .composer__input-wrap vc-slash-autocomplete {
      position: absolute;
      left: 0;
      right: 0;
      bottom: calc(100% + 0.35rem);
      z-index: 20;
    }
    .composer__format {
      display: flex;
      gap: 0;
      align-items: center;
      position: relative;
      grid-column: 1;
      grid-row: 2;
      min-width: 0;
    }
    .composer__format ::ng-deep .vc-icon-btn,
    .composer__icon-btn,
    .composer__actions ::ng-deep .vc-icon-btn {
      width: 1.7rem;
      height: 1.7rem;
    }
    .composer__icon-btn {
      display: inline-grid;
      place-items: center;
      border: 1px solid transparent;
      border-radius: var(--vc-radius-sm);
      background: transparent;
      color: var(--vc-ink-muted);
      cursor: pointer;
      padding: 0;
      transition:
        color var(--vc-dur-fast) var(--vc-ease-out),
        background var(--vc-dur-fast) var(--vc-ease-out);
    }
    .composer__icon-btn:hover,
    .composer__icon-btn:focus-visible {
      color: var(--vc-ink);
      background: color-mix(in srgb, var(--vc-brand) 12%, transparent);
    }
    .composer__emoji {
      position: relative;
    }
    .composer__emoji vc-emoji-picker {
      /* Anchored by CDK overlay; host only marks the origin box. */
      z-index: 0;
    }
    .composer__attachments {
      list-style: none;
      margin: 0;
      padding: 0;
      display: grid;
      gap: 0.35rem;
    }
    .composer__attachment {
      display: grid;
      grid-template-columns: auto 1fr auto;
      gap: 0.45rem;
      align-items: center;
      padding: 0.35rem 0.45rem;
      border: 1px solid var(--vc-border);
      border-radius: var(--vc-radius-sm);
      background: color-mix(in srgb, var(--vc-surface) 92%, transparent);
      font-size: 0.78rem;
    }
    .composer__attachment.is-failed {
      border-color: color-mix(in srgb, var(--vc-danger) 45%, var(--vc-border));
    }
    .composer__attachment-icon {
      display: inline-grid;
      place-items: center;
      width: 1.5rem;
      height: 1.5rem;
      color: var(--vc-ink-muted);
    }
    .composer__attachment-video {
      width: 120px;
      max-height: 72px;
      border-radius: var(--vc-radius-sm);
      background: #000;
      object-fit: contain;
      flex-shrink: 0;
    }
    .composer__attachment-meta {
      display: grid;
      gap: 0.1rem;
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
      font-size: 0.7rem;
    }
    .composer__attachment-error {
      color: var(--vc-danger);
      font-size: 0.7rem;
    }
    .composer__attachment-progress {
      width: 100%;
      height: 0.3rem;
      accent-color: var(--vc-brand);
    }
    .composer__attachment-actions {
      display: flex;
      gap: 0.3rem;
      align-items: center;
    }
    .composer__attachment-remove {
      font-size: 1rem;
      line-height: 1;
    }
    .composer__validation {
      margin: 0;
      font-size: 0.75rem;
      color: var(--vc-danger);
    }
    .composer__notice {
      padding: 0.45rem 0.6rem;
      border: 1px solid var(--vc-border);
      border-radius: var(--vc-radius-md);
      background: var(--vc-surface-elevated);
      animation: vc-fade-in-up var(--vc-dur-med, 180ms) var(--vc-ease-out, ease-out);
    }
    .composer__notice--error {
      border-color: color-mix(in srgb, var(--vc-danger) 45%, var(--vc-border));
    }
    .composer__notice header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 0.2rem;
    }
    .composer__notice p {
      margin: 0;
      font-size: 0.8rem;
      color: var(--vc-ink-muted);
      line-height: 1.35;
      white-space: pre-wrap;
    }
    .composer__notice ul {
      margin: 0.3rem 0 0;
      padding-left: 1.1rem;
      font-size: 0.75rem;
      color: var(--vc-ink-muted);
    }
    .composer__notice--error p {
      color: var(--vc-danger);
    }
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
      gap: 0.15rem;
      align-items: center;
      flex-wrap: wrap;
      grid-column: 2;
      grid-row: 2;
      justify-self: end;
    }
    .composer__actions ::ng-deep .vc-btn {
      min-height: 1.85rem;
      padding: 0.25rem 0.75rem;
      font-size: 0.82rem;
    }
    .composer__attach {
      position: relative;
      overflow: hidden;
      display: inline-grid;
      place-items: center;
      width: 1.7rem;
      height: 1.7rem;
      border-radius: var(--vc-radius-sm);
      color: var(--vc-ink-muted);
      cursor: pointer;
      transition:
        color var(--vc-dur-fast) var(--vc-ease-out),
        background var(--vc-dur-fast) var(--vc-ease-out);
    }
    .composer__attach:hover {
      color: var(--vc-ink);
      background: color-mix(in srgb, var(--vc-brand) 12%, transparent);
    }
    .composer__attach:has(input:disabled) {
      opacity: 0.45;
      cursor: not-allowed;
    }
    .composer__attach:has(input:disabled):hover {
      color: var(--vc-ink-muted);
      background: transparent;
    }
    .composer__attach input {
      position: absolute;
      inset: 0;
      opacity: 0;
      cursor: pointer;
    }
    .composer__attach:has(input:disabled) input {
      cursor: not-allowed;
    }
    .composer__attach-face {
      display: inline-grid;
      place-items: center;
      pointer-events: none;
    }
    .composer__mic-hint {
      color: var(--vc-ink-subtle);
      cursor: help;
      font-size: 0.68rem;
      padding: 0.2rem 0.35rem;
    }
    .composer__audio-panel {
      display: flex;
      flex-wrap: wrap;
      gap: 0.35rem;
      align-items: center;
      font-size: 0.72rem;
      color: var(--vc-ink-muted);
      flex: 1 1 auto;
      min-width: 0;
    }
    .composer__audio-timer {
      font-variant-numeric: tabular-nums;
      font-weight: 600;
      color: var(--vc-brand);
    }
    .composer__audio-btn {
      display: inline-flex;
      align-items: center;
      gap: 0.25rem;
      border: 0;
      background: transparent;
      color: var(--vc-brand);
      cursor: pointer;
      font: inherit;
      font-size: 0.75rem;
      font-weight: 600;
      padding: 0.15rem 0.3rem;
      border-radius: var(--vc-radius-sm);
    }
    .composer__audio-btn:hover,
    .composer__audio-btn:focus-visible {
      background: color-mix(in srgb, var(--vc-brand) 12%, transparent);
      color: var(--vc-ink);
    }
    .composer__audio-panel audio {
      max-width: 10rem;
      height: 1.6rem;
    }
    .composer__counter {
      margin: 0;
      font-size: 0.7rem;
      color: var(--vc-ink-subtle);
      text-align: right;
    }
    .composer__counter--over {
      color: var(--vc-danger);
    }
    @media (max-width: 720px) {
      .composer__shell {
        grid-template-columns: 1fr;
        grid-template-rows: auto auto auto;
      }
      .composer__format {
        grid-column: 1;
        grid-row: 2;
      }
      .composer__actions {
        grid-column: 1;
        grid-row: 3;
        justify-self: stretch;
        justify-content: flex-end;
      }
    }
  `,
})
export class Composer {
  private readonly composerTextarea = viewChild<Textarea>('composerTextarea');

  readonly messages = inject(MessageStore);
  readonly channels = inject(ChannelStore);
  readonly attachments = inject(AttachmentQueueService);
  readonly formatVideoDuration = formatVideoDuration;
  readonly audioRecorder = inject(AudioRecorderService);
  readonly slash = inject(SlashCommandsService);
  private readonly hub = inject(ChatHubService);
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly drafts = inject(DraftStoreService);

  readonly formatDuration = formatDuration;

  readonly draft = signal('');
  readonly validationError = signal<string | null>(null);
  readonly mentionOpen = signal(false);
  readonly mentionActiveIndex = signal(0);
  readonly mentionRemoteItems = signal<MentionAutocompleteItem[]>([]);
  readonly mentionContext = signal<{ query: string; atIndex: number } | null>(null);
  private readonly mentionAliases = signal<Record<string, string>>({});
  readonly slashOpen = signal(false);
  readonly slashActiveIndex = signal(0);
  readonly slashCatalog = signal<SlashCommandDef[]>([]);
  readonly slashContext = signal<{ query: string; slashIndex: number } | null>(null);
  readonly emojiPickerOpen = signal(false);
  readonly maxLength = MESSAGE_BODY_MAX_LENGTH;
  readonly bodyLength = computed(() => measureMessageBodyLength(this.draft()));
  readonly bodyTooLong = computed(() => isMessageBodyTooLong(this.draft()));
  readonly showCounter = computed(() => this.bodyLength() >= MESSAGE_BODY_COUNTER_THRESHOLD);
  readonly readyCount = computed(() => this.attachments.readyAttachmentIds().length);
  readonly sendingAudio = signal(false);
  /** Primary CTA — Salvar while editing (B-173); Enviar áudio while mic active. */
  readonly primarySubmitLabel = computed(() => {
    if (this.messages.editingMessage()) return 'Salvar';
    const phase = this.audioRecorder.phase();
    if (phase === 'recording' || phase === 'preview') return 'Enviar áudio';
    return 'Enviar';
  });
  readonly submitDisabled = computed(() => {
    if (this.submitting() || this.messages.sending() || this.sendingAudio()) return true;
    if (this.messages.editingMessage()) {
      return !this.draft().trim() || this.bodyTooLong();
    }
    const phase = this.audioRecorder.phase();
    if (phase === 'recording' || phase === 'preview') return false;
    const hasText = !!this.draft().trim();
    const ready = this.readyCount();
    const uploading = this.attachments.hasActiveUploads();
    return (
      (!hasText && ready === 0 && !uploading) ||
      this.bodyTooLong() ||
      this.attachments.submitBlocked()
    );
  });
  readonly mentionItems = computed(() => {
    const context = this.mentionContext();
    if (!context) return [];
    const base: MentionAutocompleteItem[] = [
      { kind: 'here', displayName: '@aqui', subtitle: 'Notifica quem está online' },
      { kind: 'channel', displayName: '@canal', subtitle: 'Notifica todos os membros' },
      ...this.mentionRemoteItems(),
    ];
    return filterMentionItems(base, context.query, {
      excludeUserId: this.auth.profile()?.id,
    });
  });
  readonly slashItems = computed(() => {
    const context = this.slashContext();
    if (!context) return [];
    return filterSlashCommands(this.slashCatalog(), context.query);
  });
  /** Sync gate so Enter×2 cannot start two sends before `messages.sending` flips. */
  private readonly submitting = signal(false);
  private lastTyping = 0;
  private mentionFetchTimer: ReturnType<typeof setTimeout> | null = null;
  private boundChannelId: string | null = null;
  private restoringDraft = false;
  /** Draft body+attachments snapshot taken when entering edit mode (B-086 restore). */
  private draftBeforeEdit: { body: string } | null = null;

  constructor() {
    effect(() => {
      const prefill = this.channels.composerPrefill();
      if (prefill === null) return;
      const text = this.channels.consumeComposerPrefill();
      if (text) {
        this.draft.set(text);
        this.persistDraftSoon();
      }
    });

    // BUG-017: Responder shows the cite bar but must also move focus to the textarea.
    effect(() => {
      if (!this.messages.replyTarget()) return;
      queueMicrotask(() => this.composerTextarea()?.nativeElement()?.focus());
    });

    // B-173: entering edit mode loads body into composer and focuses textarea.
    effect(() => {
      const editing = this.messages.editingMessage();
      if (!editing) return;
      untracked(() => {
        if (!this.draftBeforeEdit) {
          this.draftBeforeEdit = { body: this.draft() };
        }
        this.draft.set(this.toComposerDisplay(editing.body));
        this.attachments.clear();
        this.audioRecorder.reset();
        this.closeMentionMenu();
        this.closeSlashMenu();
        this.validationError.set(null);
        this.slash.clearNotice();
      });
      queueMicrotask(() => this.composerTextarea()?.nativeElement()?.focus());
    });

    effect(() => {
      // Depend only on the id signal. Reading `activeChannel()` would re-run on every
      // channels list refresh (unread/presence) and abort an in-progress mic recording.
      // Side effects must be untracked: reset() reads previewUrl and would otherwise
      // re-run this effect as soon as onstop builds a preview (BUG-004).
      const channelId = this.channels.activeChannelId();
      untracked(() => {
        void this.onActiveChannelChanged(channelId);
      });
    });

    effect(() => {
      this.draft();
      this.attachments.items();
      if (this.restoringDraft || this.messages.editingMessage()) return;
      const channelId = untracked(() => this.boundChannelId);
      if (!channelId) return;
      this.persistDraftSoon();
    });

    effect(() => {
      if (this.audioRecorder.phase() !== 'recording') return;
      const canvas = document.querySelector('.composer__audio-panel canvas');
      drawAudioWaveform(canvas as HTMLCanvasElement | null, this.audioRecorder.liveWaveform());
    });
  }

  citePreview(body: string): string {
    return replyPreviewText(body);
  }

  cancelEdit(): void {
    this.messages.clearEdit();
    this.restoreDraftAfterEdit();
    queueMicrotask(() => this.composerTextarea()?.nativeElement()?.focus());
  }

  private restoreDraftAfterEdit(): void {
    const snapshot = this.draftBeforeEdit;
    this.draftBeforeEdit = null;
    this.restoringDraft = true;
    try {
      this.draft.set(snapshot?.body ?? '');
    } finally {
      this.restoringDraft = false;
    }
    const channelId = this.boundChannelId;
    if (channelId) {
      this.persistDraftSoon();
    }
  }

  iconKindFor(file: File): AttachmentIconKind {
    return attachmentIconKind(resolveContentType(file));
  }

  sizeFor(item: PendingAttachment): string {
    return formatFileSize(item.restoredSizeBytes ?? item.file.size);
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    input.value = '';
    this.queueFiles(files);
  }

  onPaste(event: ClipboardEvent): void {
    if (this.messages.editingMessage()) return;
    const files = collectFilesFromClipboard(event.clipboardData);
    if (!files.length) return;
    event.preventDefault();
    this.queueFiles(files);
  }

  queueFiles(files: File[]): void {
    if (this.messages.editingMessage()) return;
    const error = this.attachments.addFiles(files);
    this.validationError.set(error);
  }

  async startRecording(): Promise<void> {
    const error = await this.audioRecorder.start();
    this.validationError.set(error);
  }

  stopRecording(): void {
    void this.audioRecorder.stop();
  }

  discardRecording(): void {
    this.audioRecorder.discard();
  }

  async sendRecording(): Promise<void> {
    const channelId = this.channels.activeChannel()?.id;
    if (!channelId) return;

    const recorded = await this.audioRecorder.buildRecordedAudio();
    if (!recorded) {
      this.validationError.set(
        this.audioRecorder.errorMessage() ?? 'Não foi possível preparar o áudio.',
      );
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
      this.attachments.clear();
      this.validationError.set(null);
      await this.drafts.remove(channelId);
      return;
    }

    this.validationError.set('Não foi possível enviar o áudio. Tente novamente.');
  }

  async onSubmit(event: Event): Promise<void> {
    event.preventDefault();
    if (this.submitting() || this.sendingAudio()) return;
    if (this.mentionOpen() || this.slashOpen()) return;

    const editing = this.messages.editingMessage();
    if (editing) {
      const body = this.toSendBody(this.draft());
      if (!body || isMessageBodyTooLong(body)) return;
      this.submitting.set(true);
      try {
        await this.messages.edit(editing.id, body);
        this.restoreDraftAfterEdit();
        this.validationError.set(null);
        this.slash.clearNotice();
      } finally {
        this.submitting.set(false);
      }
      return;
    }

    const phase = this.audioRecorder.phase();
    if (phase === 'recording' || phase === 'preview') {
      this.sendingAudio.set(true);
      try {
        if (phase === 'recording') {
          const recorded = await this.audioRecorder.stop();
          if (!recorded) {
            this.validationError.set(
              this.audioRecorder.errorMessage() ?? 'Não foi possível finalizar a gravação.',
            );
            return;
          }
        }
        await this.sendRecording();
      } finally {
        this.sendingAudio.set(false);
      }
      return;
    }

    const display = this.draft().trim();
    if (looksLikeSlashCommand(display)) {
      await this.runSlashCommand(display);
      return;
    }

    const body = this.toSendBody(display);
    if (isMessageBodyTooLong(body)) return;

    this.submitting.set(true);
    try {
      const attachmentIds = await this.attachments.waitForReady();
      if (!body && attachmentIds.length === 0) return;

      const channelId = this.boundChannelId;

      // Clear before await send so a second Enter cannot resubmit the same draft.
      this.draft.set('');
      this.attachments.clear();
      this.validationError.set(null);
      this.slash.clearNotice();
      if (channelId) {
        await this.drafts.remove(channelId);
      }

      const ok = await this.messages.send(body, attachmentIds);
      if (!ok) {
        this.draft.set(display);
        this.persistDraftSoon();
      }
    } finally {
      this.submitting.set(false);
    }
  }

  applyFormat(kind: 'bold' | 'italic' | 'strike' | 'code'): void {
    const textarea = this.composerTextarea()?.nativeElement();
    if (!textarea) return;

    const current = this.draft();
    const result = applyMarkdownWrap(
      current,
      textarea.selectionStart,
      textarea.selectionEnd,
      kind,
    );
    this.draft.set(result.value);
    updateTextareaSelection(textarea, result.value, result.selectionStart, result.selectionEnd);
  }

  toggleEmojiPicker(event: Event): void {
    event.stopPropagation();
    this.emojiPickerOpen.update((open) => !open);
    this.closeMentionMenu();
    this.closeSlashMenu();
  }

  insertEmoji(emoji: string): void {
    const textarea = this.composerTextarea()?.nativeElement();
    rememberRecentEmoji(emoji);
    this.emojiPickerOpen.set(false);

    const current = this.draft();
    const start = textarea?.selectionStart ?? current.length;
    const end = textarea?.selectionEnd ?? start;
    const next = `${current.slice(0, start)}${emoji}${current.slice(end)}`;
    this.draft.set(next);

    if (textarea) {
      const cursor = start + emoji.length;
      updateTextareaSelection(textarea, next, cursor, cursor);
    }
  }

  onInput(): void {
    const textarea = this.composerTextarea()?.nativeElement();
    if (!textarea) return;
    this.syncComposerMenus(textarea.value, textarea.selectionStart ?? textarea.value.length);
  }

  onKeydown(event: KeyboardEvent): void {
    if (this.slashOpen()) {
      if (event.key === 'ArrowDown') {
        event.preventDefault();
        const max = this.slashItems().length;
        if (!max) return;
        this.slashActiveIndex.update((index) => (index + 1) % max);
        return;
      }
      if (event.key === 'ArrowUp') {
        event.preventDefault();
        const max = this.slashItems().length;
        if (!max) return;
        this.slashActiveIndex.update((index) => (index - 1 + max) % max);
        return;
      }
      if (event.key === 'Escape') {
        event.preventDefault();
        this.closeSlashMenu();
        return;
      }
      if ((event.key === 'Enter' || event.key === 'Tab') && this.slashItems().length) {
        event.preventDefault();
        this.applySlash(this.slashItems()[this.slashActiveIndex()]);
        return;
      }
    }

    if (this.mentionOpen() || this.hasActiveMentionQuery()) {
      if (event.key === 'ArrowDown') {
        event.preventDefault();
        const max = this.mentionItems().length;
        if (!max) return;
        this.mentionActiveIndex.update((index) => (index + 1) % max);
        return;
      }
      if (event.key === 'ArrowUp') {
        event.preventDefault();
        const max = this.mentionItems().length;
        if (!max) return;
        this.mentionActiveIndex.update((index) => (index - 1 + max) % max);
        return;
      }
      if (event.key === 'Escape') {
        event.preventDefault();
        this.closeMentionMenu();
        return;
      }
      if (event.key === 'Enter' || event.key === 'Tab') {
        event.preventDefault();
        event.stopPropagation();
        const items = this.mentionItems();
        if (items.length) {
          const index = Math.min(Math.max(this.mentionActiveIndex(), 0), items.length - 1);
          this.applyMention(items[index]);
        }
        return;
      }
    }

    if (event.key === 'Escape' && this.messages.editingMessage()) {
      event.preventDefault();
      this.cancelEdit();
      return;
    }

    // B-173: ↑ with empty composer (cursor at start) edits last own persisted message.
    if (
      event.key === 'ArrowUp' &&
      !this.messages.editingMessage() &&
      !this.draft().trim() &&
      !this.slashOpen() &&
      !this.mentionOpen()
    ) {
      const textarea = this.composerTextarea()?.nativeElement();
      const cursor = textarea?.selectionStart ?? 0;
      if (cursor === 0) {
        const last = this.messages.lastOwnPersistedMessage();
        if (last) {
          event.preventDefault();
          this.messages.startEdit(last);
          return;
        }
      }
    }

    const shortcut = handleMarkdownShortcut(event);
    if (shortcut) {
      event.preventDefault();
      this.applyFormat(shortcut);
      return;
    }

    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      void this.onSubmit(event);
      return;
    }

    const textarea = this.composerTextarea()?.nativeElement();
    if (textarea) {
      queueMicrotask(() => {
        this.syncComposerMenus(textarea.value, textarea.selectionStart ?? textarea.value.length);
      });
    }

    const channelId = this.channels.activeChannel()?.id;
    if (!channelId) return;
    const now = Date.now();
    if (now - this.lastTyping > 1500) {
      this.lastTyping = now;
      void this.hub.sendTyping(channelId);
    }
  }

  applyMention(item: MentionAutocompleteItem): void {
    const context = this.mentionContext();
    const textarea = this.composerTextarea()?.nativeElement();
    if (!context || !textarea) return;

    if (item.kind === 'user' && item.userId) {
      const name = item.displayName.replace(/^@/, '').trim();
      if (name) {
        this.mentionAliases.update((current) => ({ ...current, [item.userId!]: name }));
      }
    }

    const display = composerMentionDisplay(item);
    const result = insertMentionToken(this.draft(), context.atIndex, context.query.length, display);
    this.draft.set(result.value);
    this.closeMentionMenu();
    updateTextareaSelection(textarea, result.value, result.cursor, result.cursor);
  }

  applySlash(item: SlashCommandDef): void {
    const textarea = this.composerTextarea()?.nativeElement();
    if (!textarea) return;
    const result = insertSlashCommand(this.draft(), item.name);
    this.draft.set(result.value);
    this.closeSlashMenu();
    updateTextareaSelection(textarea, result.value, result.cursor, result.cursor);
  }

  private async onActiveChannelChanged(channelId: string | null): Promise<void> {
    const previousId = this.boundChannelId;
    if (previousId === channelId) return;

    if (previousId) {
      await this.persistDraftNow(previousId);
    }

    this.boundChannelId = channelId;
    this.audioRecorder.reset();
    this.validationError.set(null);
    this.slash.clearNotice();
    this.messages.clearReplyTarget();
    this.messages.clearEdit();
    this.draftBeforeEdit = null;
    this.closeMentionMenu();
    this.closeSlashMenu();
    void this.ensureSlashCatalog();

    if (!channelId) {
      this.restoringDraft = true;
      this.draft.set('');
      this.attachments.clear();
      this.restoringDraft = false;
      return;
    }

    await this.restoreDraft(channelId);
  }

  private async restoreDraft(channelId: string): Promise<void> {
    this.restoringDraft = true;
    try {
      const saved = await this.drafts.get(channelId);
      const body = this.toComposerDisplay(saved?.body ?? '');
      this.draft.set(body);
      this.attachments.restoreReady(saved?.attachments ?? []);
      if (saved && body === saved.body && (saved.selectionStart != null || saved.selectionEnd != null)) {
        queueMicrotask(() => {
          const textarea = this.composerTextarea()?.nativeElement();
          if (!textarea) return;
          const start = saved.selectionStart ?? body.length;
          const end = saved.selectionEnd ?? start;
          updateTextareaSelection(textarea, body, start, end);
        });
      }
    } finally {
      this.restoringDraft = false;
    }
  }

  private persistDraftSoon(): void {
    const channelId = this.boundChannelId;
    if (!channelId || this.restoringDraft) return;
    const textarea = this.composerTextarea()?.nativeElement();
    this.drafts.scheduleSave(channelId, {
      body: this.draft(),
      attachments: this.attachments.readyAttachmentMetas(),
      selectionStart: textarea?.selectionStart,
      selectionEnd: textarea?.selectionEnd,
    });
  }

  private async persistDraftNow(channelId: string): Promise<void> {
    const textarea = this.composerTextarea()?.nativeElement();
    await this.drafts.saveNow(channelId, {
      body: this.draft(),
      attachments: this.attachments.readyAttachmentMetas(),
      selectionStart: textarea?.selectionStart,
      selectionEnd: textarea?.selectionEnd,
    });
  }

  private syncComposerMenus(text: string, cursor: number): void {
    if (this.messages.editingMessage()) {
      this.closeSlashMenu();
      this.syncMentionContext(text, cursor);
      return;
    }

    const slash = detectSlashQuery(text, cursor);
    if (slash) {
      this.closeMentionMenu();
      this.slashContext.set(slash);
      this.slashOpen.set(true);
      this.slashActiveIndex.set(0);
      void this.ensureSlashCatalog();
      return;
    }

    this.closeSlashMenu();
    this.syncMentionContext(text, cursor);
  }

  private syncMentionContext(text: string, cursor: number): void {
    const context = detectMentionQuery(text, cursor);
    if (!context) {
      this.closeMentionMenu();
      return;
    }

    this.mentionContext.set(context);
    this.mentionOpen.set(true);
    this.mentionActiveIndex.set(0);
    this.scheduleMentionFetch(context.query);
  }

  private mentionLabelMap(): Record<string, string> {
    return { ...this.channels.mentionLabels(), ...this.mentionAliases() };
  }

  private toComposerDisplay(source: string): string {
    return formatMentionPlainText(source, this.mentionLabelMap());
  }

  private toSendBody(source: string): string {
    return encodeMentionPlainText(source.trim(), this.mentionLabelMap());
  }

  private hasActiveMentionQuery(): boolean {
    const textarea = this.composerTextarea()?.nativeElement();
    if (!textarea) return this.mentionContext() !== null;
    return detectMentionQuery(
      textarea.value,
      textarea.selectionStart ?? textarea.value.length,
    ) !== null;
  }

  private async ensureSlashCatalog(): Promise<void> {
    const workspace = this.channels.activeWorkspace();
    if (!workspace) {
      this.slashCatalog.set([]);
      return;
    }
    try {
      const commands = await this.slash.listCommands(workspace.id);
      this.slashCatalog.set(commands);
    } catch {
      this.slashCatalog.set([]);
    }
  }

  private async runSlashCommand(raw: string): Promise<void> {
    if (this.submitting()) return;
    this.submitting.set(true);
    this.closeSlashMenu();
    this.validationError.set(null);
    try {
      const result = await this.slash.execute(raw);
      if (result.clearDraft) {
        const channelId = this.boundChannelId;
        this.draft.set('');
        if (channelId) {
          await this.drafts.remove(channelId);
        }
      }
    } finally {
      this.submitting.set(false);
    }
  }

  private scheduleMentionFetch(query: string): void {
    if (this.mentionFetchTimer) {
      clearTimeout(this.mentionFetchTimer);
    }

    const workspace = this.channels.activeWorkspace();
    const channel = this.channels.activeChannel();
    if (!workspace || !channel || this.channels.isDemo()) {
      this.mentionRemoteItems.set(
        this.channels.members().map((member) => ({
          kind: 'user' as const,
          userId: member.userId,
          displayName: member.displayName,
          email: member.email,
        })),
      );
      return;
    }

    this.mentionFetchTimer = setTimeout(() => {
      void this.api.getChannelMembers(workspace.id, channel.id, query).then((members) => {
        this.mentionRemoteItems.set(
          members.map((member) => ({
            kind: 'user' as const,
            userId: member.userId,
            displayName: member.displayName,
            email: member.email,
          })),
        );
      }).catch(() => {
        this.mentionRemoteItems.set([]);
      });
    }, 200);
  }

  private closeMentionMenu(): void {
    this.mentionOpen.set(false);
    this.mentionContext.set(null);
    this.mentionActiveIndex.set(0);
    if (this.mentionFetchTimer) {
      clearTimeout(this.mentionFetchTimer);
      this.mentionFetchTimer = null;
    }
  }

  private closeSlashMenu(): void {
    this.slashOpen.set(false);
    this.slashContext.set(null);
    this.slashActiveIndex.set(0);
  }
}
