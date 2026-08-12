import { DatePipe } from '@angular/common';
import { CdkContextMenuTrigger, CdkMenu, CdkMenuItem, CdkMenuTrigger } from '@angular/cdk/menu';
import type { ConnectedPosition } from '@angular/cdk/overlay';
import {
  Component,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import {
  ChatMessage,
  MessageAttachment,
  MessageForwardedFrom,
  MessageLinkPreview,
  REACTION_EMOJI_OPTIONS,
  isMessageBodyTooLong,
  measureMessageBodyLength,
  MESSAGE_BODY_COUNTER_THRESHOLD,
  MESSAGE_BODY_MAX_LENGTH,
} from '../../models/chat.models';
import {
  classifyAttachmentPreview,
  isGifContentType,
  menuActionsForMessage,
  type MessageMenuActionId,
} from '../../attachments/attachment-preview';
import { Avatar } from '../avatar/avatar';
import { AttachmentPreview } from '../attachment-preview/attachment-preview';
import { ImageLightbox, type LightboxImage } from '../image-lightbox/image-lightbox';
import { MarkdownBody } from '../../markdown/markdown-body';
import { ApiService } from '../../../core/api/api.service';
import { ChannelStore } from '../../../core/services/channel.store';
import { ThemeService } from '../../../core/services/theme.service';
import { EmojiPicker } from '../emoji-picker/emoji-picker';
import { rememberRecentEmoji } from '../../emoji/emoji-data';
import { environment } from '../../../../environments/environment';

const MINE_ACTION_MENU_POSITIONS: ConnectedPosition[] = [
  {
    originX: 'start',
    originY: 'bottom',
    overlayX: 'end',
    overlayY: 'top',
    offsetX: 0,
    offsetY: 4,
  },
  {
    originX: 'start',
    originY: 'top',
    overlayX: 'end',
    overlayY: 'bottom',
    offsetX: 0,
    offsetY: -4,
  },
  {
    originX: 'end',
    originY: 'bottom',
    overlayX: 'start',
    overlayY: 'top',
    offsetX: 0,
    offsetY: 4,
  },
  {
    originX: 'end',
    originY: 'top',
    overlayX: 'start',
    overlayY: 'bottom',
    offsetX: 0,
    offsetY: -4,
  },
];

const THEIRS_ACTION_MENU_POSITIONS: ConnectedPosition[] = [
  {
    originX: 'end',
    originY: 'bottom',
    overlayX: 'start',
    overlayY: 'top',
    offsetX: 0,
    offsetY: 4,
  },
  {
    originX: 'end',
    originY: 'top',
    overlayX: 'start',
    overlayY: 'bottom',
    offsetX: 0,
    offsetY: -4,
  },
  {
    originX: 'start',
    originY: 'bottom',
    overlayX: 'end',
    overlayY: 'top',
    offsetX: 0,
    offsetY: 4,
  },
  {
    originX: 'start',
    originY: 'top',
    overlayX: 'end',
    overlayY: 'bottom',
    offsetX: 0,
    offsetY: -4,
  },
];

@Component({
  selector: 'vc-message-bubble',
  standalone: true,
  imports: [
    Avatar,
    DatePipe,
    AttachmentPreview,
    ImageLightbox,
    MarkdownBody,
    EmojiPicker,
    CdkContextMenuTrigger,
    CdkMenuTrigger,
    CdkMenu,
    CdkMenuItem,
  ],
  template: `
    <article
      class="vc-msg vc-anim-fade-in"
      [class.vc-msg--mine]="message().mine"
      [class.vc-msg--plain]="surface() === 'plain'"
      [class.vc-msg--group-start]="groupRole() === 'start'"
      [class.vc-msg--group-middle]="groupRole() === 'middle'"
      [class.vc-msg--group-end]="groupRole() === 'end'"
      [class.vc-msg--grouped]="groupRole() === 'middle' || groupRole() === 'end'"
      [class.vc-msg--mentioned]="message().mentionsMe"
      [class.vc-msg--pinned]="message().isPinned"
      [class.vc-msg--deleted]="!!message().deletedAt"
      [class.vc-msg--highlight]="highlighted()"
      [attr.data-status]="message().status"
      [attr.data-group]="groupRole()"
      [attr.data-message-id]="message().id"
      [cdkContextMenuTriggerFor]="actionsMenu"
      [cdkContextMenuDisabled]="!showActions()"
      (cdkContextMenuOpened)="menuOpen.set(true)"
      (cdkContextMenuClosed)="menuOpen.set(false)"
      (touchstart)="onTouchStart($event)"
      (touchend)="onTouchEnd()"
      (touchmove)="onTouchEnd()"
      (touchcancel)="onTouchEnd()"
    >
      @if (!message().mine && surface() !== 'plain') {
        <div class="vc-msg__avatar-slot">
          @if (showAvatar()) {
            <vc-avatar [name]="message().authorName" [size]="avatarSize()" />
          }
        </div>
      }

      <div class="vc-msg__column">
        <div class="vc-msg__body">
          @if (showMeta()) {
            <header class="vc-msg__meta">
              <strong>{{ message().authorName }}</strong>
              <time [attr.datetime]="message().createdAt">{{
                message().createdAt | date: 'shortTime'
              }}</time>
              @if (message().editedAt && !message().deletedAt) {
                <span class="vc-msg__status">editada</span>
              }
              @if (message().isPinned && !message().deletedAt) {
                <span class="vc-msg__status vc-msg__status--pin" aria-label="Mensagem fixada"
                  >fixada</span
                >
              }
              @if (message().isSaved && !message().deletedAt) {
                <span class="vc-msg__status vc-msg__status--saved" aria-label="Mensagem salva"
                  >salva</span
                >
              }
              @if (message().status === 'sending') {
                <span class="vc-msg__status">enviando…</span>
              } @else if (message().status === 'sent') {
                <span class="vc-msg__status">enviada</span>
              } @else if (message().status === 'failed') {
                <span class="vc-msg__status vc-msg__status--fail">falhou</span>
              }
            </header>
          }

          <div class="vc-msg__content">
            @if (message().deletedAt) {
              <p class="vc-msg__deleted">Mensagem removida</p>
            } @else if (editing()) {
              <div class="vc-msg__edit">
                <textarea
                  [value]="draft()"
                  (input)="draft.set($any($event.target).value)"
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
                  <button type="button" [disabled]="editSaveDisabled()" (click)="saveEdit()">
                    Salvar
                  </button>
                  <button type="button" class="ghost" (click)="cancelEdit()">Cancelar</button>
                </div>
              </div>
            } @else {
              @if (message().forwardedFrom; as origin) {
                <p class="vc-msg__forwarded">
                  Encaminhada de
                  {{ formatForwardOrigin(origin) }}
                  · {{ origin.authorName }} · {{ origin.createdAt | date: 'shortDate' }}
                </p>
              }
              @if (message().editedAt && !message().deletedAt && !showMeta()) {
                <span class="vc-msg__edited-badge">editada</span>
              }
              @if (message().replyTo; as cite) {
                @if (cite.deleted) {
                  <div class="vc-msg__quote vc-msg__quote--deleted">Mensagem removida</div>
                } @else {
                  <button
                    type="button"
                    class="vc-msg__quote"
                    (click)="quoteClick.emit(cite.messageId)"
                  >
                    <strong>{{ cite.authorName }}</strong>
                    <span>{{ cite.preview }}</span>
                  </button>
                }
              }
              @if (message().body) {
                <vc-markdown-body [source]="message().body" [mentionLabels]="mentionLabels()" />
              }
              @if (visibleLinkPreview(); as preview) {
                @if ((preview.status ?? '').toLowerCase() === 'pending') {
                  <div
                    class="vc-msg__link-preview vc-msg__link-preview--pending"
                    aria-hidden="true"
                  ></div>
                } @else {
                  <a
                    class="vc-msg__link-preview"
                    [href]="preview.url"
                    target="_blank"
                    rel="noopener noreferrer"
                    [attr.aria-label]="linkPreviewLabel(preview)"
                  >
                    @if (preview.hasImage && linkPreviewImageUrl()) {
                      <img
                        class="vc-msg__link-preview-img"
                        [src]="linkPreviewImageUrl()!"
                        [alt]="preview.title || preview.siteName || 'Preview'"
                        loading="lazy"
                        (error)="onLinkPreviewImageError()"
                      />
                    }
                    <span class="vc-msg__link-preview-body">
                      @if (preview.siteName) {
                        <span class="vc-msg__link-preview-site">{{ preview.siteName }}</span>
                      }
                      @if (preview.title) {
                        <span class="vc-msg__link-preview-title">{{ preview.title }}</span>
                      }
                      @if (preview.description) {
                        <span class="vc-msg__link-preview-desc">{{ preview.description }}</span>
                      }
                    </span>
                  </a>
                }
              }
              @if (message().attachments?.length) {
                <ul class="vc-msg__attachments">
                  @for (attachment of message().attachments; track attachment.id) {
                    <li>
                      <vc-attachment-preview
                        [attachment]="attachment"
                        [previewUrl]="previewUrls()[attachment.id] ?? null"
                        [downloadUrl]="downloadUrls()[attachment.id] ?? null"
                        [showTranscribe]="attachment.kind === 'Audio' && transcribeEnabled()"
                        (imageOpen)="openLightbox($event)"
                        (fileOpen)="download(attachment)"
                        (transcribe)="transcribe(attachment)"
                      />
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
                        [attr.aria-label]="reactionAriaLabel(reaction.emoji)"
                        [title]="reactionTooltip(reaction.emoji)"
                        (mouseenter)="loadReactionTooltip(reaction.emoji)"
                        (focus)="loadReactionTooltip(reaction.emoji)"
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
          </div>
          @if (!showMeta()) {
            <time class="vc-msg__group-time" [attr.datetime]="message().createdAt">
              {{ message().createdAt | date: 'shortTime' }}
            </time>
          }
        </div>

        @if (showActions()) {
          <div
            class="vc-msg__toolbar"
            data-testid="msg-toolbar"
            [class.vc-msg__toolbar--pinned]="menuOpen()"
          >
            <button
              type="button"
              class="vc-msg__toolbar-btn vc-msg__more"
              aria-label="Ações da mensagem"
              aria-haspopup="menu"
              [cdkMenuTriggerFor]="actionsMenu"
              [cdkMenuPosition]="actionMenuPositions()"
              (cdkMenuOpened)="menuOpen.set(true)"
              (cdkMenuClosed)="menuOpen.set(false)"
            >
              <span class="vc-msg__more-dots" aria-hidden="true"></span>
            </button>
          </div>
        }
      </div>
    </article>

    <ng-template #actionsMenu>
      <div class="vc-msg-menu" cdkMenu>
        <div class="vc-msg-menu__reactions" role="group" aria-label="Adicionar reação">
          @for (emoji of emojiOptions; track emoji) {
            <button
              type="button"
              [attr.aria-label]="'Reagir com ' + emoji"
              (click)="onQuickReact(emoji)"
            >
              {{ emoji }}
            </button>
          }
          <div class="vc-msg__react-more">
            <button
              type="button"
              aria-label="Mais emojis"
              aria-haspopup="dialog"
              [attr.aria-expanded]="reactionPickerOpen()"
              (click)="toggleReactionPicker($event)"
            >
              🙂
            </button>
            <vc-emoji-picker
              [open]="reactionPickerOpen()"
              (select)="onQuickReact($event)"
              (closed)="reactionPickerOpen.set(false)"
            />
          </div>
        </div>
        @if (showReplyAction()) {
          <button type="button" cdkMenuItem class="vc-msg-menu__item" (click)="reply.emit()">
            Responder
          </button>
        }
        @for (item of menuItems(); track item.id) {
          <button
            type="button"
            cdkMenuItem
            class="vc-msg-menu__item"
            [class.vc-msg-menu__item--danger]="item.danger"
            (click)="onMenuAction(item.id)"
          >
            {{ item.label }}
          </button>
        }
      </div>
    </ng-template>

    <vc-image-lightbox
      [open]="lightboxOpen()"
      [images]="lightboxImages()"
      [startId]="lightboxStartId()"
      (close)="closeLightbox()"
    />
  `,
  styles: `
    :host {
      display: block;
      max-width: 100%;
      min-width: 0;
    }
    .vc-msg {
      --vc-msg-max: min(44rem, 100%);
      display: grid;
      grid-template-columns: var(--vc-msg-avatar) minmax(0, var(--vc-msg-max));
      gap: var(--vc-msg-gap);
      align-items: flex-start;
      width: fit-content;
      max-width: 100%;
      position: relative;
      -webkit-touch-callout: none;
    }
    .vc-msg--mine {
      --vc-msg-max: min(44rem, calc(100% - 2.75rem));
      margin-left: auto;
      grid-template-columns: minmax(0, var(--vc-msg-max));
    }
    .vc-msg--plain {
      width: fit-content;
      max-width: 100%;
      grid-template-columns: minmax(0, auto);
    }
    .vc-msg--plain.vc-msg--mine {
      margin-left: auto;
      align-self: flex-end;
    }
    .vc-msg--plain .vc-msg__column {
      width: fit-content;
      max-width: 100%;
      overflow: visible;
    }
    .vc-msg__avatar-slot {
      width: var(--vc-msg-avatar);
      flex-shrink: 0;
    }
    .vc-msg__column {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
      min-width: 0;
      width: 100%;
      max-width: var(--vc-msg-max);
      position: relative;
    }
    .vc-msg--mentioned:not(.vc-msg--mine) .vc-msg__body {
      border-left: 3px solid var(--vc-brand);
      padding-left: 0.55rem;
      background: color-mix(in srgb, var(--vc-brand) 8%, var(--vc-msg-theirs));
    }
    .vc-msg__body {
      display: flex;
      flex-direction: column;
      gap: 0.35rem;
      padding: var(--vc-msg-pad-block) var(--vc-msg-pad-inline);
      border-radius: var(--vc-radius-md);
      background: var(--vc-msg-theirs);
      border: 1px solid var(--vc-border);
      min-width: 0;
      width: 100%;
      box-sizing: border-box;
      position: relative;
    }
    .vc-msg--mine .vc-msg__body {
      background: var(--vc-msg-mine);
      border-color: color-mix(in srgb, var(--vc-brand) 28%, var(--vc-border));
    }
    .vc-msg--deleted .vc-msg__body {
      opacity: 0.72;
    }
    /* Grouping removes repeated identity, never the boundary of each message. */
    .vc-msg--plain .vc-msg__body {
      width: fit-content;
      max-width: 100%;
    }
    .vc-msg--plain.vc-msg--mentioned:not(.vc-msg--mine) .vc-msg__body {
      border-left: 3px solid var(--vc-brand);
      padding-left: 0.55rem;
      background: color-mix(in srgb, var(--vc-brand) 8%, var(--vc-msg-theirs));
    }
    .vc-msg__group-time {
      position: absolute;
      top: 50%;
      left: -3.35rem;
      width: 3rem;
      transform: translateY(-50%);
      text-align: right;
      font-size: 0.68rem;
      color: var(--vc-ink-subtle);
      white-space: nowrap;
      opacity: 0;
      pointer-events: none;
      transition: opacity var(--vc-dur-fast, 120ms) var(--vc-ease-out, ease);
    }
    .vc-msg:hover .vc-msg__group-time,
    .vc-msg:focus-within .vc-msg__group-time {
      opacity: 1;
    }
    .vc-msg--mine .vc-msg__group-time {
      left: calc(-3.35rem - 1.4rem);
    }
    @media (prefers-reduced-motion: reduce) {
      .vc-msg__group-time {
        transition: none;
      }
    }
    .vc-msg--highlight .vc-msg__body {
      outline: 2px solid color-mix(in srgb, var(--vc-brand) 55%, transparent);
      outline-offset: 2px;
      transition: outline-color var(--vc-dur-fast, 120ms) var(--vc-ease-out, ease);
    }
    .vc-msg__content {
      display: flex;
      flex-direction: column;
      gap: 0.4rem;
      min-width: 0;
    }
    .vc-msg__forwarded {
      margin: 0;
      min-width: 0;
      font-size: 0.78rem;
      color: var(--vc-ink-muted);
      overflow-wrap: anywhere;
    }
    .vc-msg__quote {
      display: grid;
      gap: 0.1rem;
      width: 100%;
      margin: 0;
      padding: 0.35rem 0.55rem;
      border: 0;
      border-left: 3px solid var(--vc-brand);
      border-radius: 0 var(--vc-radius-sm) var(--vc-radius-sm) 0;
      background: color-mix(in srgb, var(--vc-brand) 8%, transparent);
      color: inherit;
      font: inherit;
      text-align: left;
      cursor: pointer;
    }
    .vc-msg__quote strong {
      font-size: 0.78rem;
      color: var(--vc-brand);
    }
    .vc-msg__quote span {
      font-size: 0.8rem;
      color: var(--vc-ink-muted);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .vc-msg__quote--deleted {
      cursor: default;
      font-style: italic;
      color: var(--vc-ink-subtle);
      font-size: 0.8rem;
    }
    .vc-msg__meta {
      display: flex;
      flex-wrap: wrap;
      gap: 0.45rem;
      align-items: baseline;
    }
    .vc-msg__meta strong {
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
    .vc-msg__edited-badge {
      align-self: flex-end;
      font-size: 0.68rem;
      color: var(--vc-ink-subtle);
      font-weight: 500;
      margin-top: -0.15rem;
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
      margin: 0;
      padding: 0;
      display: grid;
      gap: 0.4rem;
      width: 100%;
    }
    .vc-msg__link-preview {
      display: flex;
      flex-direction: row;
      align-items: flex-start;
      gap: 0.55rem;
      /* fit-content: width 100% forced the stack bubble to --vc-msg-max
         and left a huge empty field when there is no thumbnail. */
      width: fit-content;
      max-width: min(22rem, 100%);
      box-sizing: border-box;
      margin-top: 0.35rem;
      padding: 0.45rem 0.55rem;
      border: 1px solid var(--vc-border);
      border-radius: var(--vc-radius-sm);
      background: color-mix(in srgb, var(--vc-surface-elevated) 88%, var(--vc-surface));
      color: inherit;
      text-decoration: none;
      overflow: hidden;
    }
    .vc-msg__link-preview:hover {
      border-color: color-mix(in srgb, var(--vc-brand) 35%, var(--vc-border));
    }
    .vc-msg__link-preview--pending {
      display: block;
      width: min(22rem, 100%);
      max-width: 100%;
      min-height: 2.75rem;
      pointer-events: none;
      background: color-mix(in srgb, var(--vc-surface-elevated) 70%, transparent);
    }
    .vc-msg__link-preview-img {
      flex: 0 0 4.5rem;
      width: 4.5rem;
      height: 4.5rem;
      object-fit: cover;
      border-radius: calc(var(--vc-radius-sm) - 2px);
      background: var(--vc-surface);
    }
    .vc-msg__link-preview-body {
      flex: 1 1 auto;
      display: grid;
      gap: 0.15rem;
      min-width: 0;
      max-width: 16rem;
    }
    .vc-msg__link-preview-site {
      font-size: 0.72rem;
      color: var(--vc-ink-muted);
      text-transform: uppercase;
      letter-spacing: 0.02em;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .vc-msg__link-preview-title {
      font-size: 0.88rem;
      font-weight: 600;
      color: var(--vc-ink);
      line-height: 1.25;
      display: -webkit-box;
      -webkit-line-clamp: 2;
      -webkit-box-orient: vertical;
      overflow: hidden;
    }
    .vc-msg__link-preview-desc {
      font-size: 0.78rem;
      color: var(--vc-ink-muted);
      line-height: 1.35;
      display: -webkit-box;
      -webkit-line-clamp: 2;
      -webkit-box-orient: vertical;
      overflow: hidden;
    }
    .vc-msg__transcript {
      margin: 0;
      font-size: 0.82rem;
      color: var(--vc-ink-muted);
      border-left: 2px solid var(--vc-border);
      padding-left: 0.55rem;
    }
    .vc-msg__reactions {
      list-style: none;
      margin: 0;
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
    .vc-msg__toolbar {
      --vc-msg-toolbar-shift: 100%;
      position: absolute;
      top: 0.4rem;
      right: 0;
      z-index: 5;
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 1.5rem;
      height: 1.5rem;
      margin: 0;
      padding: 0;
      border: 1px solid color-mix(in srgb, var(--vc-border) 72%, transparent);
      border-radius: 999px;
      background: color-mix(in srgb, var(--vc-surface) 92%, var(--vc-ink) 8%);
      opacity: 0;
      visibility: hidden;
      pointer-events: none;
      transform: translateX(var(--vc-msg-toolbar-shift)) scale(0.88);
      transition:
        opacity var(--vc-dur-fast, 120ms) var(--vc-ease-out, ease),
        transform var(--vc-dur-fast, 120ms) var(--vc-ease-out, ease),
        visibility 0s linear var(--vc-dur-fast, 120ms);
      box-shadow: 0 1px 3px color-mix(in srgb, var(--vc-ink) 10%, transparent);
    }
    .vc-msg--mine .vc-msg__toolbar {
      --vc-msg-toolbar-shift: -100%;
      right: auto;
      left: 0;
    }
    .vc-msg:hover .vc-msg__toolbar,
    .vc-msg:focus-within .vc-msg__toolbar,
    .vc-msg__toolbar--pinned {
      opacity: 1;
      visibility: visible;
      pointer-events: auto;
      transform: translateX(var(--vc-msg-toolbar-shift)) scale(1);
      transition-delay: 0s;
    }
    @media (hover: none) {
      .vc-msg__toolbar {
        opacity: 1;
        visibility: visible;
        pointer-events: auto;
        transform: translateX(var(--vc-msg-toolbar-shift)) scale(1);
        transition: none;
      }
    }
    @media (prefers-reduced-motion: reduce) {
      .vc-msg__toolbar {
        transition: none;
      }
    }
    .vc-msg-menu__reactions {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 0.05rem;
      width: 100%;
      padding: 0.2rem 0.25rem 0.45rem;
      margin-bottom: 0.2rem;
      border-bottom: 1px solid var(--vc-border-subtle);
    }
    .vc-msg__react-more {
      position: relative;
    }
    .vc-msg__react-more vc-emoji-picker {
      /* Anchored by CDK overlay; host only marks the origin box. */
      z-index: 0;
    }
    .vc-msg-menu__reactions button,
    .vc-msg__toolbar-btn {
      border: 0;
      background: transparent;
      color: var(--vc-ink-muted);
      font: inherit;
      font-size: 0.8rem;
      line-height: 1;
      cursor: pointer;
      padding: 0.25rem 0.35rem;
      border-radius: var(--vc-radius-sm);
    }
    .vc-msg-menu__reactions button:hover,
    .vc-msg-menu__reactions button:focus-visible,
    .vc-msg__toolbar-btn:hover,
    .vc-msg__toolbar-btn:focus-visible {
      color: var(--vc-ink);
      background: color-mix(in srgb, var(--vc-brand) 10%, transparent);
    }
    .vc-msg__more {
      display: grid;
      place-items: center;
      width: 100%;
      height: 100%;
      padding: 0;
      border-radius: inherit;
    }
    .vc-msg__more-dots {
      display: block;
      width: 0.16rem;
      height: 0.16rem;
      border-radius: 50%;
      background: currentColor;
      box-shadow: -0.3rem 0 currentColor, 0.3rem 0 currentColor;
    }
    .vc-msg__edit-actions {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
      align-items: center;
      margin-top: 0.45rem;
    }
    .vc-msg__edit-actions button {
      border: 0;
      background: transparent;
      color: var(--vc-ink-muted);
      font-size: 0.75rem;
      cursor: pointer;
      padding: 0;
    }
    .vc-msg__edit-actions button:hover {
      color: var(--vc-ink);
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
    .vc-msg-menu {
      display: flex;
      flex-direction: column;
      min-width: min(19rem, calc(100vw - 1rem));
      max-width: calc(100vw - 1rem);
      padding: 0.3rem;
      border: 1px solid var(--vc-border);
      border-radius: var(--vc-radius-md);
      background: var(--vc-surface);
      box-shadow: var(
        --vc-shadow-md,
        0 8px 24px color-mix(in srgb, var(--vc-ink) 18%, transparent)
      );
    }
    .vc-msg-menu__item {
      border: 0;
      background: transparent;
      color: var(--vc-ink);
      font: inherit;
      font-size: 0.84rem;
      text-align: left;
      padding: 0.45rem 0.65rem;
      border-radius: var(--vc-radius-sm);
      cursor: pointer;
    }
    .vc-msg-menu__item:hover,
    .vc-msg-menu__item:focus-visible {
      background: color-mix(in srgb, var(--vc-brand) 12%, transparent);
      outline: none;
    }
    .vc-msg-menu__item--danger {
      color: var(--vc-danger);
    }
  `,
})
export class MessageBubble {
  private readonly api = inject(ApiService);
  private readonly channels = inject(ChannelStore);
  private readonly theme = inject(ThemeService);
  private readonly contextMenu = viewChild(CdkContextMenuTrigger);

  readonly message = input.required<ChatMessage>();
  readonly showMeta = input(true);
  readonly showAvatar = input(true);
  /** Role inside an author/time group (B-088). */
  readonly groupRole = input<'start' | 'middle' | 'end' | 'single'>('single');
  /** `plain` = content only; outer chrome comes from timeline stack bubble. */
  readonly surface = input<'bubble' | 'plain'>('bubble');
  readonly showThreadAction = input(false);
  readonly showReplyAction = input(false);
  readonly showForwardAction = input(false);
  readonly showPinAction = input(false);
  readonly showSaveAction = input(false);
  readonly showMarkUnreadAction = input(false);
  readonly highlighted = input(false);
  readonly edit = output<string>();
  readonly delete = output<void>();
  readonly removeLinkPreview = output<void>();
  readonly openThread = output<void>();
  readonly reply = output<void>();
  readonly forward = output<void>();
  readonly quoteClick = output<string>();
  readonly react = output<string>();
  readonly pin = output<void>();
  readonly unpin = output<void>();
  readonly save = output<void>();
  readonly unsave = output<void>();
  readonly markUnread = output<void>();

  readonly editing = signal(false);
  readonly draft = signal('');
  readonly transcript = signal<string | null>(null);
  readonly downloadUrls = signal<Record<string, string>>({});
  readonly previewUrls = signal<Record<string, string>>({});
  readonly linkPreviewImageUrl = signal<string | null>(null);
  readonly emojiOptions = REACTION_EMOJI_OPTIONS;
  readonly actionMenuPositions = computed(() =>
    this.message().mine ? MINE_ACTION_MENU_POSITIONS : THEIRS_ACTION_MENU_POSITIONS,
  );
  readonly reactionPickerOpen = signal(false);
  readonly menuOpen = signal(false);
  readonly reactionTooltips = signal<Record<string, string>>({});
  readonly lightboxOpen = signal(false);
  readonly lightboxStartId = signal<string | null>(null);
  readonly maxLength = MESSAGE_BODY_MAX_LENGTH;
  readonly avatarSize = computed(() => (this.theme.density() === 'compact' ? 28 : 34));
  readonly editLength = computed(() => measureMessageBodyLength(this.draft()));
  readonly editTooLong = computed(() => isMessageBodyTooLong(this.draft()));
  readonly showEditCounter = computed(() => this.editLength() >= MESSAGE_BODY_COUNTER_THRESHOLD);
  readonly editSaveDisabled = computed(() => !this.draft().trim() || this.editTooLong());
  readonly transcribeEnabled = computed(
    () => environment.aiTranscribeEnabled && environment.aiSummarizeEnabled,
  );
  readonly mentionLabels = computed(() => this.channels.mentionLabels());
  readonly showActions = computed(
    () => !this.message().deletedAt && !this.editing() && this.message().status === 'persisted',
  );
  readonly menuItems = computed(() =>
    menuActionsForMessage({
      mine: this.message().mine,
      showForward: this.showForwardAction(),
      showThread: this.showThreadAction(),
      showPin: this.showPinAction(),
      isPinned: !!this.message().isPinned,
      showSave: this.showSaveAction(),
      isSaved: !!this.message().isSaved,
      showMarkUnread: this.showMarkUnreadAction(),
      replyCount: this.message().replyCount,
      hasLinkPreview: !!this.visibleLinkPreview(),
    }),
  );
  readonly visibleLinkPreview = computed(() => {
    const preview = this.message().linkPreview;
    if (!preview || this.message().deletedAt) return null;
    const status = (preview.status ?? 'Ready').toLowerCase();
    if (status === 'failed' || status === 'blocked') return null;
    if (status === 'pending' || status === 'ready') return preview;
    return null;
  });
  readonly lightboxImages = computed<LightboxImage[]>(() => {
    const urls = this.downloadUrls();
    const previews = this.previewUrls();
    return (this.message().attachments ?? [])
      .filter((a) => classifyAttachmentPreview(a.contentType, a.kind) === 'image')
      .map((a) => ({
        id: a.id,
        url: urls[a.id] ?? previews[a.id],
        alt: a.fileName,
      }))
      .filter((a): a is LightboxImage => !!a.url);
  });

  private longPressTimer: ReturnType<typeof setTimeout> | null = null;
  private longPressPoint: { x: number; y: number } | null = null;

  constructor() {
    effect(() => {
      const attachments = this.message().attachments ?? [];
      const channelId = this.message().channelId;
      for (const attachment of attachments) {
        const kind = classifyAttachmentPreview(attachment.contentType, attachment.kind);
        if (kind === 'image' || kind === 'pdf') {
          const status = attachment.thumbnailStatus;
          if (status === 'Ready' && !this.previewUrls()[attachment.id]) {
            void this.loadPreviewUrl(channelId, attachment.id);
          } else if (
            (!status || status === 'Failed') &&
            !this.downloadUrls()[attachment.id] &&
            kind === 'image'
          ) {
            // Legacy attachments without thumbnail pipeline — show original.
            void this.loadDownloadUrl(channelId, attachment.id);
          }
          if (isGifContentType(attachment.contentType) && !this.downloadUrls()[attachment.id]) {
            void this.loadDownloadUrl(channelId, attachment.id);
          }
        } else if ((kind === 'audio' || kind === 'video') && !this.downloadUrls()[attachment.id]) {
          void this.loadDownloadUrl(channelId, attachment.id);
        }
      }
    });

    effect(() => {
      const preview = this.visibleLinkPreview();
      const channelId = this.message().channelId;
      const messageId = this.message().id;
      if (!preview || !preview.hasImage || (preview.status ?? '').toLowerCase() === 'pending') {
        this.linkPreviewImageUrl.set(null);
        return;
      }
      if (!channelId || !messageId || this.linkPreviewImageUrl()) return;
      void this.loadLinkPreviewImage(channelId, messageId);
    });
  }

  linkPreviewLabel(preview: MessageLinkPreview): string {
    const title = (preview.title ?? '').trim();
    const site = (preview.siteName ?? '').trim();
    if (title && site) return `${title} — ${site}`;
    if (title) return title;
    if (site) return site;
    return preview.url;
  }

  onLinkPreviewImageError(): void {
    this.linkPreviewImageUrl.set(null);
  }

  formatForwardOrigin(origin: MessageForwardedFrom): string {
    const raw = (origin.channelName ?? '').trim();
    const looksLikeDmSlug = /^dm:/i.test(raw);
    const isDirect = origin.isDirect === true || looksLikeDmSlug || raw === 'DM';
    if (isDirect) {
      if (!raw || raw === 'DM' || looksLikeDmSlug) return 'DM';
      return raw.startsWith('@') ? raw : `@${raw}`;
    }
    if (!raw) return '#';
    if (raw.startsWith('#') || raw.startsWith('@')) return raw;
    return `#${raw}`;
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
      // Always fetch a fresh presigned URL — cached preview URLs expire (TTL ~300s).
      const result = await this.api.getAttachmentDownload(channelId, attachment.id);
      this.downloadUrls.update((current) => ({
        ...current,
        [attachment.id]: result.downloadUrl,
      }));
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

  toggleReactionPicker(event: Event): void {
    event.stopPropagation();
    this.reactionPickerOpen.update((open) => !open);
  }

  onQuickReact(emoji: string): void {
    rememberRecentEmoji(emoji);
    this.reactionPickerOpen.set(false);
    this.react.emit(emoji);
  }

  reactionTooltip(emoji: string): string {
    return this.reactionTooltips()[emoji] ?? '';
  }

  /** Stable accessible name — never replace "Reação {emoji}" with tooltip-only text. */
  reactionAriaLabel(emoji: string): string {
    const tip = this.reactionTooltip(emoji);
    return tip ? `Reação ${emoji}: ${tip}` : `Reação ${emoji}`;
  }

  async loadReactionTooltip(emoji: string): Promise<void> {
    if (this.reactionTooltips()[emoji]) return;
    const channelId = this.message().channelId;
    if (!channelId || this.channels.isDemo()) return;

    try {
      const result = await this.api.getReactionUsers(channelId, this.message().id, emoji);
      const names = result.users.slice(0, 10).map((user) => user.displayName);
      const extra = result.total > names.length ? ` e mais ${result.total - names.length}` : '';
      const text = names.length ? `${names.join(', ')}${extra}` : '';
      this.reactionTooltips.update((current) => ({ ...current, [emoji]: text }));
    } catch {
      // tooltip stays empty
    }
  }

  onMenuAction(id: MessageMenuActionId): void {
    switch (id) {
      case 'forward':
        this.forward.emit();
        break;
      case 'thread':
        this.openThread.emit();
        break;
      case 'edit':
        this.startEdit();
        break;
      case 'remove-link-preview':
        this.removeLinkPreview.emit();
        break;
      case 'delete':
        this.delete.emit();
        break;
      case 'pin':
        this.pin.emit();
        break;
      case 'unpin':
        this.unpin.emit();
        break;
      case 'save':
        this.save.emit();
        break;
      case 'unsave':
        this.unsave.emit();
        break;
      case 'mark-unread':
        this.markUnread.emit();
        break;
    }
  }

  openLightbox(attachmentId: string): void {
    const channelId = this.message().channelId;
    if (channelId && !this.downloadUrls()[attachmentId]) {
      void this.loadDownloadUrl(channelId, attachmentId);
    }
    this.lightboxStartId.set(attachmentId);
    this.lightboxOpen.set(true);
  }

  closeLightbox(): void {
    this.lightboxOpen.set(false);
    this.lightboxStartId.set(null);
  }

  onTouchStart(event: TouchEvent): void {
    if (!this.showActions() || event.touches.length !== 1) return;
    const touch = event.touches[0];
    this.longPressPoint = { x: touch.clientX, y: touch.clientY };
    this.clearLongPress();
    this.longPressTimer = setTimeout(() => {
      const point = this.longPressPoint;
      const trigger = this.contextMenu();
      if (!point || !trigger) return;
      event.preventDefault();
      trigger.open(point);
    }, 480);
  }

  onTouchEnd(): void {
    this.clearLongPress();
  }

  private clearLongPress(): void {
    if (this.longPressTimer) {
      clearTimeout(this.longPressTimer);
      this.longPressTimer = null;
    }
  }

  private async loadDownloadUrl(channelId: string, attachmentId: string): Promise<void> {
    try {
      const result = await this.api.getAttachmentDownload(channelId, attachmentId);
      this.downloadUrls.update((current) => ({ ...current, [attachmentId]: result.downloadUrl }));
    } catch {
      // preview stays on file card until URL resolves
    }
  }

  private async loadPreviewUrl(channelId: string, attachmentId: string): Promise<void> {
    try {
      const result = await this.api.getAttachmentThumbnail(channelId, attachmentId);
      this.previewUrls.update((current) => ({ ...current, [attachmentId]: result.downloadUrl }));
    } catch {
      // fall back to original when thumbnail endpoint is not ready
      if (!this.downloadUrls()[attachmentId]) {
        void this.loadDownloadUrl(channelId, attachmentId);
      }
    }
  }

  private async loadLinkPreviewImage(channelId: string, messageId: string): Promise<void> {
    try {
      const result = await this.api.getLinkPreviewImage(channelId, messageId);
      this.linkPreviewImageUrl.set(result.downloadUrl);
    } catch {
      // card stays text-only when image URL is unavailable
    }
  }
}
