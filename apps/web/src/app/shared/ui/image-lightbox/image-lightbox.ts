import { Component, HostListener, effect, input, output, signal } from '@angular/core';

export interface LightboxImage {
  id: string;
  url: string;
  alt: string;
}

@Component({
  selector: 'vc-image-lightbox',
  standalone: true,
  template: `
    @if (open() && current(); as img) {
      <div class="lb-backdrop" (click)="close.emit()"></div>
      <div
        class="lb"
        role="dialog"
        aria-modal="true"
        aria-label="Visualização de imagem"
        (click)="$event.stopPropagation()"
      >
        <header class="lb__header">
          <p class="lb__title">{{ img.alt }}</p>
          <div class="lb__actions">
            @if (images().length > 1) {
              <span class="lb__count">{{ index() + 1 }} / {{ images().length }}</span>
            }
            <a [href]="img.url" target="_blank" rel="noopener noreferrer" download>
              Baixar
            </a>
            <button type="button" class="ghost" aria-label="Fechar" (click)="close.emit()">×</button>
          </div>
        </header>
        <div class="lb__stage">
          @if (images().length > 1) {
            <button
              type="button"
              class="lb__nav"
              aria-label="Imagem anterior"
              (click)="prev()"
            >
              ‹
            </button>
          }
          <img [src]="img.url" [alt]="img.alt" />
          @if (images().length > 1) {
            <button
              type="button"
              class="lb__nav"
              aria-label="Próxima imagem"
              (click)="next()"
            >
              ›
            </button>
          }
        </div>
      </div>
    }
  `,
  styles: `
    .lb-backdrop {
      position: fixed;
      inset: 0;
      background: color-mix(in srgb, var(--vc-ink) 55%, transparent);
      z-index: 50;
    }
    .lb {
      position: fixed;
      z-index: 51;
      inset: 1.25rem;
      display: flex;
      flex-direction: column;
      gap: 0.75rem;
      max-width: min(960px, calc(100vw - 2.5rem));
      max-height: calc(100vh - 2.5rem);
      margin: auto;
      padding: 0.85rem 1rem 1rem;
      border-radius: var(--vc-radius-md);
      background: var(--vc-surface);
      border: 1px solid var(--vc-border);
      box-shadow: var(--vc-shadow-md, 0 12px 40px color-mix(in srgb, var(--vc-ink) 25%, transparent));
    }
    .lb__header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 0.75rem;
    }
    .lb__title {
      margin: 0;
      font-size: 0.9rem;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .lb__actions {
      display: inline-flex;
      align-items: center;
      gap: 0.65rem;
      flex-shrink: 0;
    }
    .lb__actions a,
    .lb__actions button {
      border: 0;
      background: transparent;
      color: var(--vc-ink-muted);
      font: inherit;
      font-size: 0.82rem;
      cursor: pointer;
      text-decoration: none;
    }
    .lb__actions a:hover,
    .lb__actions button:hover {
      color: var(--vc-ink);
    }
    .lb__count {
      font-size: 0.75rem;
      color: var(--vc-ink-subtle);
    }
    .lb__stage {
      flex: 1;
      min-height: 0;
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 0.5rem;
    }
    .lb__stage img {
      max-width: 100%;
      max-height: min(70vh, 720px);
      object-fit: contain;
      border-radius: var(--vc-radius-sm);
    }
    .lb__nav {
      width: 2.25rem;
      height: 2.25rem;
      border: 1px solid var(--vc-border);
      border-radius: var(--vc-radius-md);
      background: var(--vc-surface);
      color: var(--vc-ink);
      font-size: 1.35rem;
      line-height: 1;
      cursor: pointer;
      flex-shrink: 0;
    }
    .lb__nav:hover {
      background: color-mix(in srgb, var(--vc-brand) 10%, var(--vc-surface));
    }
  `,
})
export class ImageLightbox {
  readonly open = input(false);
  readonly images = input<LightboxImage[]>([]);
  readonly startId = input<string | null>(null);
  readonly close = output<void>();

  readonly index = signal(0);

  constructor() {
    effect(() => {
      if (!this.open()) return;
      const list = this.images();
      const start = this.startId();
      const found = start ? list.findIndex((item) => item.id === start) : 0;
      this.index.set(found >= 0 ? found : 0);
    });
  }

  readonly current = () => {
    const list = this.images();
    if (!list.length) return null;
    return list[Math.min(this.index(), list.length - 1)] ?? null;
  };

  prev(): void {
    const len = this.images().length;
    if (len < 2) return;
    this.index.update((i) => (i - 1 + len) % len);
  }

  next(): void {
    const len = this.images().length;
    if (len < 2) return;
    this.index.update((i) => (i + 1) % len);
  }

  @HostListener('document:keydown', ['$event'])
  onKeydown(event: KeyboardEvent): void {
    if (!this.open()) return;
    if (event.key === 'Escape') {
      event.preventDefault();
      this.close.emit();
      return;
    }
    if (event.key === 'ArrowLeft') {
      event.preventDefault();
      this.prev();
    } else if (event.key === 'ArrowRight') {
      event.preventDefault();
      this.next();
    }
  }
}
