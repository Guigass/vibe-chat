import { Component, input } from '@angular/core';
import { VcTooltip } from '../tooltip/tooltip';

@Component({
  selector: 'vc-icon-button',
  standalone: true,
  imports: [VcTooltip],
  template: `
    <button
      class="vc-icon-btn"
      type="button"
      [disabled]="disabled()"
      [attr.aria-label]="label()"
      [attr.title]="overlayTooltip() ? null : label()"
      [vcTooltip]="overlayTooltip() ? label() : null"
      [tooltipDisabled]="!overlayTooltip()"
      position="right"
    >
      <ng-content />
    </button>
  `,
  styles: `
    .vc-icon-btn {
      width: 2.25rem;
      height: 2.25rem;
      display: inline-grid;
      place-items: center;
      border: 1px solid transparent;
      border-radius: var(--vc-radius-md);
      background: transparent;
      color: var(--vc-ink-muted);
      cursor: pointer;
      transition:
        color var(--vc-dur-fast) var(--vc-ease-out),
        background var(--vc-dur-fast) var(--vc-ease-out);
    }
    .vc-icon-btn:hover:not(:disabled) {
      color: var(--vc-ink);
      background: color-mix(in srgb, var(--vc-brand) 12%, transparent);
    }
    .vc-icon-btn:disabled {
      opacity: 0.45;
      cursor: not-allowed;
    }
  `,
})
export class IconButton {
  readonly label = input.required<string>();
  readonly disabled = input(false);
  /** When true, show BrnTooltip instead of native title (compact rail). */
  readonly overlayTooltip = input(false);
}
