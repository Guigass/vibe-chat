import { Component, input } from '@angular/core';

export type BadgeTone = 'accent' | 'muted' | 'success' | 'warn' | 'danger';

@Component({
  selector: 'vc-badge',
  standalone: true,
  template: `
    <span
      class="vc-badge"
      [class.vc-badge--accent]="tone() === 'accent'"
      [class.vc-badge--muted]="tone() === 'muted'"
      [class.vc-badge--success]="tone() === 'success'"
      [class.vc-badge--warn]="tone() === 'warn'"
      [class.vc-badge--danger]="tone() === 'danger'"
    >
      <ng-content />
    </span>
  `,
  styles: `
    .vc-badge {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      min-width: 1.25rem;
      height: 1.25rem;
      padding: 0 0.4rem;
      border-radius: var(--vc-radius-sm);
      font-size: 0.7rem;
      font-weight: 700;
      line-height: 1;
      white-space: nowrap;
    }
    .vc-badge--accent {
      background: var(--vc-brand);
      color: #042f2e;
    }
    .vc-badge--muted {
      background: color-mix(in srgb, var(--vc-ink) 10%, transparent);
      color: var(--vc-ink-muted);
    }
    .vc-badge--success {
      background: color-mix(in srgb, var(--vc-success) 16%, transparent);
      color: var(--vc-success);
    }
    .vc-badge--warn {
      background: color-mix(in srgb, var(--vc-warning) 16%, transparent);
      color: var(--vc-warning);
    }
    .vc-badge--danger {
      background: color-mix(in srgb, var(--vc-danger) 16%, transparent);
      color: var(--vc-danger);
    }
  `,
})
export class Badge {
  readonly tone = input<BadgeTone>('accent');
}
