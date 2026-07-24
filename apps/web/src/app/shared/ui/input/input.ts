import { Component, input, model, output } from '@angular/core';

@Component({
  selector: 'vc-input',
  standalone: true,
  template: `
    <label class="vc-field">
      @if (label()) {
        <span class="vc-field__label">{{ label() }}</span>
      }
      <input
        class="vc-field__control"
        [attr.id]="controlId() || null"
        [attr.type]="type()"
        [attr.placeholder]="placeholder()"
        [attr.aria-label]="ariaLabel() || label() || placeholder()"
        [disabled]="disabled()"
        [value]="value()"
        (input)="value.set(($any($event.target).value))"
        (focus)="focused.emit()"
        (blur)="blurred.emit()"
      />
    </label>
  `,
  styles: `
    .vc-field {
      display: grid;
      gap: 0.35rem;
      width: 100%;
    }
    .vc-field__label {
      font-size: 0.85rem;
      color: var(--vc-ink-muted);
      font-weight: 500;
    }
    .vc-field__control {
      width: 100%;
      min-height: 2.5rem;
      padding: 0.55rem 0.8rem;
      border-radius: var(--vc-radius-md);
      border: 1px solid var(--vc-border);
      background: var(--vc-surface-elevated);
      color: var(--vc-ink);
    }
    .vc-field__control::placeholder {
      color: var(--vc-ink-subtle);
    }
  `,
})
export class Input {
  readonly value = model('');
  readonly label = input('');
  readonly placeholder = input('');
  readonly type = input('text');
  readonly disabled = input(false);
  readonly controlId = input('');
  readonly ariaLabel = input('');
  readonly focused = output<void>();
  readonly blurred = output<void>();
}
