import { Component, input } from '@angular/core';
import { TypingState } from '../../models/chat.models';

@Component({
  selector: 'vc-typing-indicator',
  standalone: true,
  template: `
    @if (users().length) {
      <div class="vc-typing" aria-live="polite">
        <span class="vc-typing__dots" aria-hidden="true">
          <i></i><i></i><i></i>
        </span>
        <span>
          @if (users().length === 1) {
            {{ users()[0].displayName }} está digitando…
          } @else {
            {{ users().length }} pessoas digitando…
          }
        </span>
      </div>
    }
  `,
  styles: `
    .vc-typing {
      display: inline-flex;
      align-items: center;
      gap: 0.55rem;
      min-height: 1.5rem;
      color: var(--vc-ink-muted);
      font-size: 0.85rem;
    }
    .vc-typing__dots {
      display: inline-flex;
      gap: 0.2rem;
    }
    .vc-typing__dots i {
      width: 0.35rem;
      height: 0.35rem;
      border-radius: 50%;
      background: var(--vc-brand);
      display: block;
      animation: vc-typing-dot 1.1s ease-in-out infinite;
    }
    .vc-typing__dots i:nth-child(2) {
      animation-delay: 0.15s;
    }
    .vc-typing__dots i:nth-child(3) {
      animation-delay: 0.3s;
    }
  `,
})
export class TypingIndicator {
  readonly users = input<TypingState[]>([]);
}
