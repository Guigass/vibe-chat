import { Component, inject } from '@angular/core';
import { LocaleService } from '../../../core/i18n/locale.service';
import { LOCALE_OPTION_NAMES, SUPPORTED_LOCALES, type AppLocale, isAppLocale } from '../../../core/i18n/locale';
import { ui } from '../../../core/i18n/strings';

@Component({
  selector: 'vc-locale-control',
  standalone: true,
  template: `
    <label class="locale-control">
      <span class="vc-sr-only">{{ label }}</span>
      <select
        data-testid="locale-select"
        [value]="locales.locale()"
        [attr.aria-label]="label"
        (change)="onChange($event)"
      >
        @for (id of options; track id) {
          <option [value]="id">{{ names[id] }}</option>
        }
      </select>
    </label>
  `,
  styles: `
    .locale-control select {
      font: inherit;
      font-size: var(--vc-text-xs, 0.8rem);
      color: inherit;
      background: transparent;
      border: 1px solid var(--vc-border-subtle, currentColor);
      border-radius: var(--vc-radius-sm, 0.4rem);
      padding: 0.2rem 0.4rem;
      cursor: pointer;
    }
  `,
})
export class LocaleControl {
  readonly locales = inject(LocaleService);
  readonly options = SUPPORTED_LOCALES;
  readonly names = LOCALE_OPTION_NAMES;
  readonly label = ui.language;

  onChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    if (isAppLocale(value)) {
      void this.locales.apply(value as AppLocale);
    }
  }
}
