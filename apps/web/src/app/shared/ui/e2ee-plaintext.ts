import { Component, effect, input, signal } from '@angular/core';
import { isE2eeEnvelope, openText } from '../../core/e2ee/e2ee-crypto';
import { ui } from '../../core/i18n/strings';
import { MarkdownBody } from '../markdown/markdown-body';

@Component({
  selector: 'vc-e2ee-plaintext',
  standalone: true,
  imports: [MarkdownBody],
  template: `
    @if (text(); as value) {
      <vc-markdown-body [source]="value" />
    } @else {
      <p class="vc-msg__e2ee">{{ ui.e2eeLocked }}</p>
    }
  `,
  styles: `
    .vc-msg__e2ee {
      margin: 0;
      color: var(--vc-ink-muted);
      font-size: var(--vc-text-sm);
    }
  `,
})
export class E2eePlaintext {
  readonly source = input.required<string>();
  readonly text = signal<string | null>(null);
  readonly ui = ui;

  constructor() {
    effect(() => {
      const body = this.source();
      if (!isE2eeEnvelope(body)) {
        this.text.set(body);
        return;
      }
      this.text.set(null);
      void openText(body).then((opened) => {
        if (this.source() === body) this.text.set(opened);
      });
    });
  }
}
