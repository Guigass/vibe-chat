import { Component, input, model } from '@angular/core';

export interface SelectOption {
  value: string;
  label: string;
}

@Component({
  selector: 'vc-select',
  standalone: true,
  template: `
    <label class="vc-field">
      @if (label()) {
        <span class="vc-field__label">{{ label() }}</span>
      }
      <select
        class="vc-field__control"
        [attr.aria-label]="ariaLabel() || label() || null"
        [disabled]="disabled()"
        [value]="value()"
        (change)="onChange($event)"
      >
        @for (option of options(); track option.value) {
          <option [value]="option.value">{{ option.label }}</option>
        }
        <ng-content />
      </select>
    </label>
  `,
  styles: `
    :host { display: block; min-width: 0; }
    .vc-field {
      display: grid;
      gap: var(--vc-space-1);
      width: 100%;
      min-width: 0;
    }
    .vc-field__label {
      font-size: var(--vc-text-sm);
      color: var(--vc-ink-muted);
      font-weight: 500;
    }
    .vc-field__control {
      width: 100%;
      min-height: var(--vc-control-height);
      padding: var(--vc-space-2) var(--vc-space-3);
      border-radius: var(--vc-radius-md);
      border: 1px solid var(--vc-border);
      background: var(--vc-surface-elevated);
      color: var(--vc-ink);
      font: inherit;
    }
  `,
})
export class Select {
  readonly value = model('');
  readonly label = input('');
  readonly ariaLabel = input('');
  readonly disabled = input(false);
  readonly options = input<SelectOption[]>([]);

  onChange(event: Event): void {
    const target = event.target;
    if (target instanceof HTMLSelectElement) {
      this.value.set(target.value);
    }
  }
}
