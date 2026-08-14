import { Component, input, output } from '@angular/core';
import { MentionAutocompleteItem } from '../../../shared/markdown/mention-tokens';

@Component({
  selector: 'vc-mention-autocomplete',
  standalone: true,
  template: `
    @if (open() && items().length) {
      <ul class="mention-menu" role="listbox" [attr.aria-label]="'Menções'">
        @for (item of items(); track trackItem(item); let index = $index) {
          <li>
            <button
              type="button"
              role="option"
              [attr.aria-selected]="index === activeIndex()"
              [class.is-active]="index === activeIndex()"
              (mousedown)="onPick($event, item)"
              (click)="onPick($event, item)"
            >
              <span class="mention-menu__name">{{ item.displayName }}</span>
              @if (item.subtitle) {
                <span class="mention-menu__subtitle">{{ item.subtitle }}</span>
              }
            </button>
          </li>
        }
      </ul>
    }
  `,
  styles: `
    :host {
      display: block;
    }
    .mention-menu {
      list-style: none;
      margin: 0;
      padding: 0.25rem;
      border: 1px solid var(--vc-border);
      border-radius: var(--vc-radius-md);
      background: var(--vc-surface-elevated);
      box-shadow: var(--vc-shadow-md, 0 8px 24px rgba(0, 0, 0, 0.12));
      max-height: 14rem;
      overflow: auto;
    }
    .mention-menu button {
      width: 100%;
      display: grid;
      gap: 0.1rem;
      text-align: left;
      border: 0;
      background: transparent;
      color: var(--vc-ink);
      border-radius: var(--vc-radius-sm);
      padding: 0.4rem 0.55rem;
      cursor: pointer;
      font: inherit;
    }
    .mention-menu button.is-active,
    .mention-menu button:hover,
    .mention-menu button:focus-visible {
      background: color-mix(in srgb, var(--vc-brand) 14%, transparent);
    }
    .mention-menu__name {
      font-weight: 600;
    }
    .mention-menu__subtitle {
      font-size: 0.75rem;
      color: var(--vc-ink-muted);
    }
  `,
})
export class MentionAutocomplete {
  readonly open = input(false);
  readonly items = input<MentionAutocompleteItem[]>([]);
  readonly activeIndex = input(0);
  readonly select = output<MentionAutocompleteItem>();

  trackItem(item: MentionAutocompleteItem): string {
    return item.kind === 'user' ? `user-${item.userId}` : item.kind;
  }

  onPick(event: Event, item: MentionAutocompleteItem): void {
    event.preventDefault();
    event.stopPropagation();
    this.select.emit(item);
  }
}
