import { Component, input, output } from '@angular/core';
import { SlashCommandDef } from '../../../shared/markdown/slash-tokens';

@Component({
  selector: 'vc-slash-autocomplete',
  standalone: true,
  template: `
    @if (open() && items().length) {
      <ul class="slash-menu" role="listbox" [attr.aria-label]="'Comandos'">
        @for (item of items(); track item.name; let index = $index) {
          <li>
            <button
              type="button"
              role="option"
              [attr.aria-selected]="index === activeIndex()"
              [class.is-active]="index === activeIndex()"
              (mousedown.prevent)="select.emit(item)"
            >
              <span class="slash-menu__usage">{{ item.usage }}</span>
              <span class="slash-menu__desc">{{ item.description }}</span>
            </button>
          </li>
        }
      </ul>
    }
  `,
  styles: `
    .slash-menu {
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
    .slash-menu button {
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
    .slash-menu button.is-active,
    .slash-menu button:hover,
    .slash-menu button:focus-visible {
      background: color-mix(in srgb, var(--vc-brand) 14%, transparent);
    }
    .slash-menu__usage {
      font-weight: 600;
      font-family: var(--vc-font-mono, ui-monospace, monospace);
      font-size: 0.9rem;
    }
    .slash-menu__desc {
      font-size: 0.75rem;
      color: var(--vc-ink-muted);
    }
  `,
})
export class SlashAutocomplete {
  readonly open = input(false);
  readonly items = input<SlashCommandDef[]>([]);
  readonly activeIndex = input(0);
  readonly select = output<SlashCommandDef>();
}
