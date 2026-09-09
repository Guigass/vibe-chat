import { Component, ElementRef, afterRenderEffect, inject, input, viewChild } from '@angular/core';
import { LocaleService } from '../../../core/i18n/locale.service';
import { LOCALE_OPTION_NAMES, SUPPORTED_LOCALES, type AppLocale, isAppLocale } from '../../../core/i18n/locale';
import { ui } from '../../../core/i18n/strings';

@Component({
  selector: 'vc-locale-control',
  standalone: true,
  template: `
    <label class="locale-control" [class.locale-control--field]="variant() === 'field'">
      <span class="vc-sr-only">{{ label }}</span>
      <select
        #selectEl
        data-testid="locale-select"
        [attr.aria-label]="label"
        (change)="onChange($event)"
      >
        @for (id of options; track id) {
          <option [value]="id" [selected]="id === locales.locale()">{{ names[id] }}</option>
        }
      </select>
    </label>
  `,
  styles: `
    .locale-control {
      display: inline-flex;
      align-items: center;
    }
    .locale-control select {
      font: inherit;
      font-size: 0.8rem;
      color: inherit;
      background: color-mix(in srgb, var(--vc-surface-elevated) 70%, transparent);
      border: 1px solid var(--vc-border);
      border-radius: var(--vc-radius-sm);
      padding: 0.25rem 0.45rem;
      min-height: 2rem;
      cursor: pointer;
    }
    .locale-control select:focus-visible {
      outline: none;
      box-shadow: var(--vc-focus-ring);
    }
    .locale-control--field {
      display: block;
      width: 100%;
    }
    .locale-control--field select {
      width: 100%;
      min-height: 2.5rem;
      padding: 0.55rem 0.8rem;
      border-radius: var(--vc-radius-md);
      background: var(--vc-surface-elevated);
      color: var(--vc-ink);
      font-size: inherit;
    }
  `,
})
export class LocaleControl {
  readonly locales = inject(LocaleService);
  readonly options = SUPPORTED_LOCALES;
  readonly names = LOCALE_OPTION_NAMES;
  readonly label = ui.language;
  readonly variant = input<'compact' | 'field'>('compact');
  private readonly selectEl = viewChild<ElementRef<HTMLSelectElement>>('selectEl');

  constructor() {
    // Native <select> ignores [value] when @for options land after first paint
    // (settings panel instance opened while locale is already `en`).
    afterRenderEffect(() => {
      const locale = this.locales.locale();
      const el = this.selectEl()?.nativeElement;
      if (el && el.value !== locale) {
        el.value = locale;
      }
    });
  }

  onChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    if (isAppLocale(value)) {
      void this.locales.apply(value as AppLocale);
    }
  }
}
