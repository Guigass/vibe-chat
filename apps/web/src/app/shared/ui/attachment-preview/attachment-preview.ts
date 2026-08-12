import {
  Component,
  ElementRef,
  computed,
  effect,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import {
  attachmentFamilyIcon,
  classifyAttachmentPreview,
  formatAttachmentSize,
  isGifContentType,
  type AttachmentPreviewKind,
} from '../../attachments/attachment-preview';
import { MessageAttachment } from '../../models/chat.models';
import { AudioMessage } from '../audio-message/audio-message';

@Component({
  selector: 'vc-attachment-preview',
  standalone: true,
  imports: [AudioMessage],
  template: `
    @switch (previewKind()) {
      @case ('audio') {
        <vc-audio-message [attachment]="attachment()" [downloadUrl]="downloadUrl()" />
        @if (showTranscribe()) {
          <button type="button" class="vc-att__transcribe" (click)="transcribe.emit()">
            Transcrever
          </button>
        }
      }
      @case ('image') {
        @if (showPlaceholder()) {
          <div
            class="vc-att__placeholder"
            [style.aspect-ratio]="aspectRatio()"
            role="status"
            [attr.aria-label]="'Gerando preview de ' + attachment().fileName"
          ></div>
        } @else if (showFileCard()) {
          <button
            type="button"
            class="vc-att__card"
            [attr.title]="failedTitle()"
            (click)="openFile()"
          >
            <span class="vc-att__icon" aria-hidden="true">{{ icon() }}</span>
            <span class="vc-att__meta">
              <span class="vc-att__name">{{ attachment().fileName }}</span>
              <span class="vc-att__size">{{ sizeLabel() }}</span>
            </span>
          </button>
        } @else {
          <button
            type="button"
            class="vc-att__image-btn"
            [class.vc-att__image-btn--gif]="isGif()"
            [attr.aria-label]="'Abrir imagem ' + attachment().fileName"
            (click)="imageOpen.emit(attachment().id)"
            (mouseenter)="onGifEnter()"
            (mouseleave)="onGifLeave()"
            (focus)="onGifEnter()"
            (blur)="onGifLeave()"
          >
            @if (isGif()) {
              <canvas
                #gifCanvas
                class="vc-att__img"
                [class.vc-att__img--hidden]="gifHover()"
                aria-hidden="true"
              ></canvas>
            }
            <img
              #gifImg
              class="vc-att__img"
              [class.vc-att__img--hidden]="isGif() && !gifHover()"
              [src]="displayUrl()!"
              [alt]="attachment().fileName"
              loading="lazy"
              (load)="onImageLoad()"
              (error)="loadFailed.set(true)"
            />
          </button>
        }
      }
      @case ('pdf') {
        @if (showPlaceholder()) {
          <div
            class="vc-att__placeholder"
            [style.aspect-ratio]="aspectRatio()"
            role="status"
            [attr.aria-label]="'Gerando preview de ' + attachment().fileName"
          ></div>
        } @else if (displayUrl() && !loadFailed()) {
          <button
            type="button"
            class="vc-att__image-btn"
            [attr.aria-label]="'Abrir PDF ' + attachment().fileName"
            (click)="openFile()"
          >
            <img
              class="vc-att__img"
              [src]="displayUrl()!"
              [alt]="attachment().fileName"
              loading="lazy"
              (error)="loadFailed.set(true)"
            />
            <span class="vc-att__pdf-hint">
              PDF
              @if (attachment().pageCount; as pages) {
                · {{ pages }} {{ pages === 1 ? 'página' : 'páginas' }}
              }
              · {{ sizeLabel() }}
            </span>
          </button>
        } @else {
          <button
            type="button"
            class="vc-att__card"
            [attr.title]="failedTitle()"
            (click)="openFile()"
          >
            <span class="vc-att__icon" aria-hidden="true">{{ icon() }}</span>
            <span class="vc-att__meta">
              <span class="vc-att__name">{{ attachment().fileName }}</span>
              <span class="vc-att__size">
                PDF
                @if (attachment().pageCount; as pages) {
                  · {{ pages }} {{ pages === 1 ? 'página' : 'páginas' }}
                }
                · {{ sizeLabel() }}
              </span>
            </span>
          </button>
        }
      }
      @case ('video') {
        @if (downloadUrl() && !loadFailed()) {
          <div class="vc-att__video">
            <video
              controls
              preload="metadata"
              [poster]="previewUrl() ?? undefined"
              [src]="downloadUrl()!"
              (error)="loadFailed.set(true)"
            >
              Seu navegador não reproduz este vídeo.
            </video>
            <a
              class="vc-att__download"
              [href]="downloadUrl()!"
              target="_blank"
              rel="noopener noreferrer"
            >
              Baixar {{ attachment().fileName }}
            </a>
          </div>
        } @else {
          <button type="button" class="vc-att__card" (click)="openFile()">
            <span class="vc-att__icon" aria-hidden="true">{{ icon() }}</span>
            <span class="vc-att__meta">
              <span class="vc-att__name">{{ attachment().fileName }}</span>
              <span class="vc-att__size">Vídeo · {{ sizeLabel() }}</span>
            </span>
          </button>
        }
      }
      @default {
        <button type="button" class="vc-att__card" (click)="openFile()">
          <span class="vc-att__icon" aria-hidden="true">{{ icon() }}</span>
          <span class="vc-att__meta">
            <span class="vc-att__name">{{ attachment().fileName }}</span>
            <span class="vc-att__size">{{ sizeLabel() }}</span>
          </span>
        </button>
      }
    }
  `,
  styles: `
    :host {
      display: block;
      width: 100%;
    }
    .vc-att__card {
      display: flex;
      align-items: center;
      gap: 0.55rem;
      width: 100%;
      margin: 0;
      padding: 0.45rem 0.55rem;
      border: 1px solid var(--vc-border);
      border-radius: var(--vc-radius-sm);
      background: color-mix(in srgb, var(--vc-surface) 70%, transparent);
      color: inherit;
      font: inherit;
      text-align: left;
      cursor: pointer;
    }
    .vc-att__card:hover {
      border-color: color-mix(in srgb, var(--vc-brand) 35%, var(--vc-border));
    }
    .vc-att__icon {
      font-size: 1.15rem;
      line-height: 1;
      flex-shrink: 0;
    }
    .vc-att__meta {
      display: grid;
      gap: 0.1rem;
      min-width: 0;
    }
    .vc-att__name {
      font-size: 0.84rem;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
      color: var(--vc-ink);
    }
    .vc-att__size {
      font-size: 0.72rem;
      color: var(--vc-ink-subtle);
    }
    .vc-att__placeholder {
      width: 100%;
      max-height: 320px;
      min-height: 96px;
      border-radius: var(--vc-radius-sm);
      background: color-mix(in srgb, var(--vc-ink) 6%, transparent);
      animation: vc-att-pulse 1.2s ease-in-out infinite;
    }
    @keyframes vc-att-pulse {
      0%,
      100% {
        opacity: 0.55;
      }
      50% {
        opacity: 0.9;
      }
    }
    @media (prefers-reduced-motion: reduce) {
      .vc-att__placeholder {
        animation: none;
      }
    }
    .vc-att__image-btn {
      display: block;
      width: 100%;
      margin: 0;
      padding: 0;
      border: 0;
      background: transparent;
      cursor: pointer;
      border-radius: var(--vc-radius-sm);
      overflow: hidden;
      text-align: left;
    }
    .vc-att__img {
      display: block;
      width: 100%;
      max-height: 320px;
      height: auto;
      object-fit: contain;
      background: color-mix(in srgb, var(--vc-ink) 4%, transparent);
      border-radius: var(--vc-radius-sm);
    }
    .vc-att__img--hidden {
      position: absolute;
      width: 1px;
      height: 1px;
      opacity: 0;
      pointer-events: none;
    }
    .vc-att__pdf-hint {
      display: block;
      margin-top: 0.25rem;
      font-size: 0.72rem;
      color: var(--vc-ink-subtle);
    }
    .vc-att__video {
      display: grid;
      gap: 0.35rem;
    }
    .vc-att__video video {
      width: 100%;
      max-height: 320px;
      border-radius: var(--vc-radius-sm);
      background: #000;
    }
    .vc-att__download {
      font-size: 0.75rem;
      color: var(--vc-brand);
      text-decoration: none;
    }
    .vc-att__download:hover {
      text-decoration: underline;
    }
    .vc-att__transcribe {
      margin-top: 0.25rem;
      border: 0;
      background: transparent;
      color: var(--vc-brand);
      font: inherit;
      font-size: 0.72rem;
      cursor: pointer;
      padding: 0;
    }
  `,
})
export class AttachmentPreview {
  readonly attachment = input.required<MessageAttachment>();
  /** Original file URL (download / GIF hover / audio / video). */
  readonly downloadUrl = input<string | null>(null);
  /** Thumbnail URL when Ready (B-090). */
  readonly previewUrl = input<string | null>(null);
  readonly showTranscribe = input(false);
  readonly imageOpen = output<string>();
  readonly fileOpen = output<void>();
  readonly transcribe = output<void>();

  readonly loadFailed = signal(false);
  readonly gifHover = signal(false);
  private readonly gifCanvas = viewChild<ElementRef<HTMLCanvasElement>>('gifCanvas');
  private readonly gifImg = viewChild<ElementRef<HTMLImageElement>>('gifImg');
  private preferReducedMotion = false;

  readonly previewKind = computed<AttachmentPreviewKind>(() =>
    classifyAttachmentPreview(this.attachment().contentType, this.attachment().kind),
  );
  readonly icon = computed(() =>
    attachmentFamilyIcon(this.attachment().contentType, this.attachment().kind),
  );
  readonly sizeLabel = computed(() => formatAttachmentSize(this.attachment().sizeBytes));
  readonly isGif = computed(() => isGifContentType(this.attachment().contentType));
  readonly displayUrl = computed(() => {
    if (this.isGif() && this.gifHover() && this.downloadUrl()) {
      return this.downloadUrl();
    }
    return this.previewUrl() ?? this.downloadUrl();
  });
  readonly showPlaceholder = computed(() => {
    const status = this.attachment().thumbnailStatus;
    return status === 'Pending' && !this.previewUrl() && !this.loadFailed();
  });
  readonly showFileCard = computed(() => {
    if (this.loadFailed()) return true;
    if (this.attachment().thumbnailStatus === 'Failed') return true;
    if (this.showPlaceholder()) return false;
    return !this.displayUrl();
  });
  readonly aspectRatio = computed(() => {
    const w = this.attachment().width;
    const h = this.attachment().height;
    if (w && h && w > 0 && h > 0) {
      return `${w} / ${h}`;
    }
    return '4 / 3';
  });
  readonly failedTitle = computed(() =>
    this.attachment().thumbnailStatus === 'Failed'
      ? 'Preview indisponível — abrir arquivo original'
      : undefined,
  );

  constructor() {
    if (typeof window !== 'undefined' && typeof window.matchMedia === 'function') {
      this.preferReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    }
    effect(() => {
      this.downloadUrl();
      this.previewUrl();
      this.attachment();
      this.loadFailed.set(false);
      this.gifHover.set(false);
    });
  }

  onImageLoad(): void {
    if (!this.isGif()) return;
    const img = this.gifImg()?.nativeElement;
    const canvas = this.gifCanvas()?.nativeElement;
    if (!img || !canvas) return;
    const width = img.naturalWidth || img.width;
    const height = img.naturalHeight || img.height;
    if (!width || !height) return;
    canvas.width = width;
    canvas.height = height;
    const ctx = canvas.getContext('2d');
    ctx?.drawImage(img, 0, 0, width, height);
  }

  onGifEnter(): void {
    if (this.preferReducedMotion) return;
    this.gifHover.set(true);
  }

  onGifLeave(): void {
    this.gifHover.set(false);
  }

  openFile(): void {
    this.fileOpen.emit();
  }
}
