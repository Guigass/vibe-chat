import { Component, input } from '@angular/core';

@Component({
  selector: 'vc-badge',
  standalone: true,
  template: `
    <span class="vc-badge" [class.vc-badge--accent]="tone() === 'accent'" [class.vc-badge--muted]="tone() === 'muted'">
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
    }
    .vc-badge--accent {
      background: var(--vc-brand);
      color: #042f2e;
    }
    .vc-badge--muted {
      background: color-mix(in srgb, var(--vc-ink) 10%, transparent);
      color: var(--vc-ink-muted);
    }
  `,
})
export class Badge {
  readonly tone = input<'accent' | 'muted'>('accent');
}
