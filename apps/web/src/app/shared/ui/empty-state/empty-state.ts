import { Component, input } from '@angular/core';

@Component({
  selector: 'vc-empty-state',
  standalone: true,
  template: `
    <div class="vc-empty">
      <h3>{{ title() }}</h3>
      @if (description()) {
        <p>{{ description() }}</p>
      }
      <div class="vc-empty__actions">
        <ng-content />
      </div>
    </div>
  `,
  styles: `
    .vc-empty {
      text-align: center;
      padding: var(--vc-space-8) var(--vc-space-4);
      color: var(--vc-ink-muted);
    }
    h3 {
      margin: 0 0 var(--vc-space-2);
      color: var(--vc-ink);
      font-family: var(--vc-font-display);
    }
    p {
      margin: 0 auto var(--vc-space-4);
      max-width: 28rem;
      line-height: 1.5;
    }
  `,
})
export class EmptyState {
  readonly title = input.required<string>();
  readonly description = input<string>('');
}
