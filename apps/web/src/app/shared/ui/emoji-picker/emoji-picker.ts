import {
  Component,
  ElementRef,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import {
  CdkConnectedOverlay,
  CdkOverlayOrigin,
  Overlay,
  type ConnectedPosition,
} from '@angular/cdk/overlay';
import { ScrollingModule } from '@angular/cdk/scrolling';
import {
  categoryLabel,
  loadEmojiCatalog,
  readRecentEmojis,
  rememberRecentEmoji,
  searchEmojis,
  type EmojiCatalog,
  type EmojiCategory,
  type EmojiLocale,
} from '../../emoji/emoji-data';

@Component({
  selector: 'vc-emoji-picker',
  standalone: true,
  imports: [ScrollingModule, CdkConnectedOverlay, CdkOverlayOrigin],
  template: `
    <span class="emoji-picker__origin" cdkOverlayOrigin #origin="cdkOverlayOrigin"></span>
    <ng-template
      cdkConnectedOverlay
      [cdkConnectedOverlayOrigin]="origin"
      [cdkConnectedOverlayOpen]="open()"
      [cdkConnectedOverlayPositions]="positions"
      [cdkConnectedOverlayPush]="true"
      [cdkConnectedOverlayViewportMargin]="8"
      [cdkConnectedOverlayScrollStrategy]="scrollStrategy"
      [cdkConnectedOverlayHasBackdrop]="true"
      [cdkConnectedOverlayBackdropClass]="'cdk-overlay-transparent-backdrop'"
      (backdropClick)="closed.emit()"
      (detach)="onDetach()"
    >
      <div
        class="emoji-picker"
        role="dialog"
        aria-modal="true"
        [attr.aria-label]="'Seletor de emoji'"
        (keydown)="onPanelKeydown($event)"
      >
        <div class="emoji-picker__search">
          <input
            #searchInput
            type="search"
            [value]="query()"
            (input)="onSearchInput($event)"
            placeholder="Buscar emoji…"
            aria-label="Buscar emoji"
          />
        </div>

        @if (query().trim()) {
          <div class="emoji-picker__section">
            <p class="emoji-picker__heading">Resultados</p>
            <cdk-virtual-scroll-viewport itemSize="36" class="emoji-picker__viewport">
              <div class="emoji-picker__grid">
                @for (emoji of searchResults(); track emoji) {
                  <button
                    type="button"
                    class="emoji-picker__emoji"
                    [class.is-active]="emoji === activeEmoji()"
                    [attr.aria-label]="'Inserir ' + emoji"
                    (click)="pick(emoji)"
                  >
                    {{ emoji }}
                  </button>
                }
              </div>
            </cdk-virtual-scroll-viewport>
          </div>
        } @else {
          <div class="emoji-picker__tabs" role="tablist" aria-label="Categorias de emoji">
            @if (recentEmojis().length) {
              <button
                type="button"
                role="tab"
                [attr.aria-selected]="activeCategoryId() === 'recent'"
                [class.is-active]="activeCategoryId() === 'recent'"
                (click)="activeCategoryId.set('recent')"
              >
                Recentes
              </button>
            }
            @for (category of categories(); track category.id) {
              <button
                type="button"
                role="tab"
                [attr.aria-selected]="activeCategoryId() === category.id"
                [class.is-active]="activeCategoryId() === category.id"
                (click)="activeCategoryId.set(category.id)"
              >
                {{ labelFor(category) }}
              </button>
            }
          </div>

          <div class="emoji-picker__section">
            <p class="emoji-picker__heading">{{ activeHeading() }}</p>
            <cdk-virtual-scroll-viewport itemSize="36" class="emoji-picker__viewport">
              <div class="emoji-picker__grid">
                @for (emoji of visibleEmojis(); track emoji) {
                  <button
                    type="button"
                    class="emoji-picker__emoji"
                    [class.is-active]="emoji === activeEmoji()"
                    [attr.aria-label]="'Inserir ' + emoji"
                    (click)="pick(emoji)"
                  >
                    {{ emoji }}
                  </button>
                }
              </div>
            </cdk-virtual-scroll-viewport>
          </div>
        }
      </div>
    </ng-template>
  `,
  styles: `
    :host {
      position: absolute;
      inset: 0;
      display: block;
      pointer-events: none;
    }
    .emoji-picker__origin {
      display: block;
      width: 100%;
      height: 100%;
    }
    .emoji-picker {
      width: min(20rem, 92vw);
      border: 1px solid var(--vc-border);
      border-radius: var(--vc-radius-md);
      background: var(--vc-surface-elevated);
      box-shadow: var(--vc-shadow-md, 0 8px 24px rgba(0, 0, 0, 0.12));
      padding: 0.45rem;
      display: grid;
      gap: 0.45rem;
    }
    .emoji-picker__search input {
      width: 100%;
      border: 1px solid var(--vc-border);
      border-radius: var(--vc-radius-sm);
      background: var(--vc-surface);
      color: var(--vc-ink);
      font: inherit;
      font-size: 0.82rem;
      padding: 0.4rem 0.55rem;
    }
    .emoji-picker__tabs {
      display: flex;
      flex-wrap: wrap;
      gap: 0.25rem;
    }
    .emoji-picker__tabs button {
      border: 0;
      background: transparent;
      color: var(--vc-ink-muted);
      font: inherit;
      font-size: 0.72rem;
      padding: 0.25rem 0.45rem;
      border-radius: var(--vc-radius-sm);
      cursor: pointer;
    }
    .emoji-picker__tabs button.is-active,
    .emoji-picker__tabs button:hover,
    .emoji-picker__tabs button:focus-visible {
      background: color-mix(in srgb, var(--vc-brand) 14%, transparent);
      color: var(--vc-ink);
    }
    .emoji-picker__heading {
      margin: 0 0 0.25rem;
      font-size: 0.72rem;
      color: var(--vc-ink-subtle);
    }
    .emoji-picker__viewport {
      height: 11rem;
      width: 100%;
    }
    .emoji-picker__grid {
      display: grid;
      grid-template-columns: repeat(8, minmax(0, 1fr));
      gap: 0.15rem;
      padding-bottom: 0.25rem;
    }
    .emoji-picker__emoji {
      border: 0;
      background: transparent;
      font-size: 1.15rem;
      line-height: 1;
      width: 2rem;
      height: 2rem;
      border-radius: var(--vc-radius-sm);
      cursor: pointer;
    }
    .emoji-picker__emoji:hover,
    .emoji-picker__emoji:focus-visible,
    .emoji-picker__emoji.is-active {
      background: color-mix(in srgb, var(--vc-brand) 14%, transparent);
    }
  `,
})
export class EmojiPicker {
  private readonly overlay = inject(Overlay);
  readonly scrollStrategy = this.overlay.scrollStrategies.reposition();

  readonly open = input(false);
  readonly locale = input<EmojiLocale>('pt');
  readonly select = output<string>();
  readonly closed = output<void>();

  readonly query = signal('');
  readonly catalog = signal<EmojiCatalog | null>(null);
  readonly activeCategoryId = signal('smileys');
  readonly activeEmoji = signal<string | null>(null);
  readonly recentEmojis = signal<string[]>(readRecentEmojis());

  readonly positions: ConnectedPosition[] = [
    {
      originX: 'start',
      originY: 'top',
      overlayX: 'start',
      overlayY: 'bottom',
      offsetY: -6,
    },
    {
      originX: 'start',
      originY: 'bottom',
      overlayX: 'start',
      overlayY: 'top',
      offsetY: 6,
    },
    {
      originX: 'end',
      originY: 'top',
      overlayX: 'end',
      overlayY: 'bottom',
      offsetY: -6,
    },
    {
      originX: 'end',
      originY: 'bottom',
      overlayX: 'end',
      overlayY: 'top',
      offsetY: 6,
    },
  ];

  private readonly searchInput = viewChild<ElementRef<HTMLInputElement>>('searchInput');

  readonly categories = computed(() => this.catalog()?.categories ?? []);
  readonly searchResults = computed(() => {
    const catalog = this.catalog();
    if (!catalog) return [];
    return searchEmojis(catalog, this.query(), this.locale());
  });
  readonly visibleEmojis = computed(() => {
    if (this.activeCategoryId() === 'recent') {
      return this.recentEmojis();
    }
    const category = this.categories().find((item) => item.id === this.activeCategoryId());
    return category?.emojis ?? [];
  });
  readonly activeHeading = computed(() => {
    if (this.activeCategoryId() === 'recent') {
      return this.locale() === 'pt' ? 'Usados recentemente' : 'Recently used';
    }
    const category = this.categories().find((item) => item.id === this.activeCategoryId());
    return category ? categoryLabel(category, this.locale()) : '';
  });

  constructor() {
    void loadEmojiCatalog().then((catalog) => {
      this.catalog.set(catalog);
      if (this.recentEmojis().length) {
        this.activeCategoryId.set('recent');
      } else if (catalog.categories[0]) {
        this.activeCategoryId.set(catalog.categories[0].id);
      }
    });

    effect(() => {
      if (!this.open()) {
        this.query.set('');
        this.activeEmoji.set(null);
        return;
      }
      queueMicrotask(() => this.searchInput()?.nativeElement.focus());
    });
  }

  onDetach(): void {
    if (this.open()) {
      this.closed.emit();
    }
  }

  labelFor(category: EmojiCategory): string {
    return categoryLabel(category, this.locale());
  }

  onSearchInput(event: Event): void {
    this.query.set((event.target as HTMLInputElement).value);
    this.activeEmoji.set(null);
  }

  pick(emoji: string): void {
    rememberRecentEmoji(emoji);
    this.recentEmojis.set(readRecentEmojis());
    this.select.emit(emoji);
    this.closed.emit();
  }

  onPanelKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      event.preventDefault();
      this.closed.emit();
      return;
    }

    const emojis = this.query().trim() ? this.searchResults() : this.visibleEmojis();
    if (!emojis.length) return;

    const currentIndex = this.activeEmoji()
      ? emojis.indexOf(this.activeEmoji()!)
      : -1;

    if (event.key === 'ArrowRight') {
      event.preventDefault();
      const next = currentIndex < 0 ? 0 : Math.min(currentIndex + 1, emojis.length - 1);
      this.activeEmoji.set(emojis[next] ?? null);
    } else if (event.key === 'ArrowLeft') {
      event.preventDefault();
      const next = currentIndex < 0 ? 0 : Math.max(currentIndex - 1, 0);
      this.activeEmoji.set(emojis[next] ?? null);
    } else if (event.key === 'ArrowDown') {
      event.preventDefault();
      const next = currentIndex < 0 ? 0 : Math.min(currentIndex + 8, emojis.length - 1);
      this.activeEmoji.set(emojis[next] ?? null);
    } else if (event.key === 'ArrowUp') {
      event.preventDefault();
      const next = currentIndex < 0 ? 0 : Math.max(currentIndex - 8, 0);
      this.activeEmoji.set(emojis[next] ?? null);
    } else if (event.key === 'Enter' && this.activeEmoji()) {
      event.preventDefault();
      this.pick(this.activeEmoji()!);
    }
  }
}
