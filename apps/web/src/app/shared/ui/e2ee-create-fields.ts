import { Component, output } from '@angular/core';
import { ui } from '../../core/i18n/strings';

@Component({
  selector: 'vc-e2ee-create-fields',
  standalone: true,
  template: `
    <p class="note">{{ ui.e2eeCapabilityBody }}</p>
    <label class="note">
      <input type="checkbox" data-testid="e2ee-ack" (change)="onChange($event)" />
      {{ ui.e2eeAcknowledge }}
    </label>
  `,
  styles: `
    .note {
      display: flex;
      gap: var(--vc-space-2);
      align-items: flex-start;
      margin: 0;
      color: var(--vc-ink-muted);
      font-size: 0.82rem;
      line-height: 1.4;
    }
  `,
})
export class E2eeCreateFields {
  readonly ui = ui;
  readonly acknowledged = output<boolean>();

  onChange(event: Event): void {
    this.acknowledged.emit((event.target as HTMLInputElement).checked);
  }
}
