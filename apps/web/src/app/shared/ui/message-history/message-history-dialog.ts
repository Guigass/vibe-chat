import { DatePipe } from '@angular/common';
import { Component, effect, inject, input, output, signal } from '@angular/core';
import { A11yModule } from '@angular/cdk/a11y';
import { MessageHistoryApi } from '../../../core/api/message-history-api.service';
import { ui } from '../../../core/i18n/strings';
import { MessageVersionItem } from '../../../shared/messaging/message-history';

@Component({
  selector: 'vc-message-history-dialog',
  standalone: true,
  imports: [A11yModule, DatePipe],
  template: `
    @if (open()) {
      <div class="mh-backdrop" (click)="closed.emit()"></div>
      <div
        class="mh"
        role="dialog"
        aria-modal="true"
        aria-labelledby="mh-title"
        cdkTrapFocus
        [cdkTrapFocusAutoCapture]="true"
        (keydown.escape)="$event.stopPropagation(); closed.emit()"
      >
        <header class="mh__header">
          <h2 id="mh-title">{{ ui.historyTitle }}</h2>
          <button type="button" (click)="closed.emit()" [attr.aria-label]="ui.historyClose">×</button>
        </header>
        @if (error()) {
          <p role="alert">{{ ui.historyError }}</p>
        } @else if (loading()) {
          <p>{{ ui.loading }}</p>
        } @else if (!versions().length) {
          <p>{{ ui.historyEmpty }}</p>
        } @else {
          <ol class="mh__list">
            @for (version of versions(); track version.version) {
              <li>
                <span>{{ version.version }}</span>
                <time [attr.datetime]="version.createdAt">{{ version.createdAt | date: 'short' }}</time>
                <p>{{ version.body }}</p>
              </li>
            }
          </ol>
          @if (current()) {
            <p class="mh__current"><strong>{{ ui.historyCurrent }}</strong> {{ current() }}</p>
          }
        }
      </div>
    }
  `,
  styles: `
    .mh-backdrop { position: fixed; inset: 0; background: color-mix(in srgb, var(--vc-ink) 35%, transparent); }
    .mh { position: fixed; inset: 12vh auto auto 50%; transform: translateX(-50%); width: min(32rem, calc(100vw - 2rem));
      max-height: 70vh; overflow: auto; background: var(--vc-surface); color: var(--vc-ink);
      border: 1px solid var(--vc-border); border-radius: var(--vc-radius, 12px); padding: 1rem; z-index: 30; }
    .mh__header { display: flex; justify-content: space-between; align-items: center; gap: 1rem; }
    .mh__list { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 0.75rem; }
    .mh__list p, .mh__current { white-space: pre-wrap; }
  `,
})
export class MessageHistoryDialog {
  private readonly api = inject(MessageHistoryApi);
  readonly ui = ui;
  readonly open = input(false);
  readonly channelId = input('');
  readonly messageId = input('');
  readonly closed = output<void>();
  readonly loading = signal(false);
  readonly error = signal(false);
  readonly versions = signal<MessageVersionItem[]>([]);
  readonly current = signal('');

  constructor() {
    effect(() => {
      const visible = this.open();
      const channelId = this.channelId();
      const messageId = this.messageId();
      if (!visible || !channelId || !messageId) return;
      this.loading.set(true);
      this.error.set(false);
      void this.api.history(channelId, messageId).then(
        (payload) => {
          this.versions.set(payload.versions ?? []);
          this.current.set(payload.currentBody ?? '');
          this.loading.set(false);
        },
        () => {
          this.versions.set([]);
          this.current.set('');
          this.error.set(true);
          this.loading.set(false);
        },
      );
    });
  }
}
