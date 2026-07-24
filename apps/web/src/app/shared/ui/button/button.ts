import { Component, input } from '@angular/core';

@Component({
  selector: 'vc-button',
  standalone: true,
  template: `
    <button
      class="vc-btn"
      [class.vc-btn--primary]="variant() === 'primary'"
      [class.vc-btn--ghost]="variant() === 'ghost'"
      [class.vc-btn--subtle]="variant() === 'subtle'"
      [class.vc-btn--block]="block()"
      [disabled]="disabled() || loading()"
      [attr.type]="type()"
    >
      @if (loading()) {
        <span class="vc-btn__spinner" aria-hidden="true"></span>
      }
      <ng-content />
    </button>
  `,
  styles: `
    .vc-btn {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      gap: 0.5rem;
      min-height: 2.5rem;
      padding: 0.55rem 1.1rem;
      border-radius: var(--vc-radius-md);
      border: 1px solid transparent;
      cursor: pointer;
      font-weight: 600;
      letter-spacing: -0.01em;
      transition:
        background var(--vc-dur-fast) var(--vc-ease-out),
        border-color var(--vc-dur-fast) var(--vc-ease-out),
        transform var(--vc-dur-fast) var(--vc-ease-out);
    }
    .vc-btn:hover:not(:disabled) {
      transform: translateY(-1px);
    }
    .vc-btn:disabled {
      opacity: 0.55;
      cursor: not-allowed;
      transform: none;
    }
    .vc-btn--primary {
      background: var(--vc-brand);
      color: #042f2e;
    }
    .vc-btn--primary:hover:not(:disabled) {
      background: var(--vc-brand-hover);
    }
    .vc-btn--ghost {
      background: transparent;
      border-color: var(--vc-border);
      color: var(--vc-ink);
    }
    .vc-btn--subtle {
      background: var(--vc-brand-soft);
      color: var(--vc-brand-ink);
    }
    .vc-btn--block {
      width: 100%;
    }
    .vc-btn__spinner {
      width: 0.9rem;
      height: 0.9rem;
      border-radius: 50%;
      border: 2px solid currentColor;
      border-right-color: transparent;
      animation: vc-spin 0.7s linear infinite;
    }
    @keyframes vc-spin {
      to {
        transform: rotate(360deg);
      }
    }
  `,
})
export class Button {
  readonly variant = input<'primary' | 'ghost' | 'subtle'>('primary');
  readonly type = input<'button' | 'submit'>('button');
  readonly disabled = input(false);
  readonly loading = input(false);
  readonly block = input(false);
}
