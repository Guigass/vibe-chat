import { Component, computed, inject, input, output, signal } from '@angular/core';
import { A11yModule } from '@angular/cdk/a11y';
import { MessageHistoryApi } from '../../../core/api/message-history-api.service';
import { ChannelStore } from '../../../core/services/channel.store';
import { ui } from '../../../core/i18n/strings';
import { idsEqual } from '../../../core/services/message-sync';

@Component({
  selector: 'vc-message-move-dialog',
  standalone: true,
  imports: [A11yModule],
  template: `
    @if (open()) {
      <div class="mv-backdrop" (click)="closed.emit()"></div>
      <div
        class="mv"
        role="dialog"
        aria-modal="true"
        aria-labelledby="mv-title"
        cdkTrapFocus
        [cdkTrapFocusAutoCapture]="true"
        (keydown.escape)="$event.stopPropagation(); closed.emit()"
      >
        <header class="mv__header">
          <h2 id="mv-title">{{ ui.moveTitle }}</h2>
          <button type="button" (click)="closed.emit()" [attr.aria-label]="ui.historyClose">×</button>
        </header>
        @if (error()) {
          <p role="alert">{{ ui.moveError }}</p>
        }
        <ul class="mv__list">
          @for (channel of targets(); track channel.id) {
            <li>
              <button type="button" [disabled]="busy()" (click)="confirm(channel.id)">{{ channel.name }}</button>
            </li>
          } @empty {
            <li>{{ ui.moveEmpty }}</li>
          }
        </ul>
      </div>
    }
  `,
  styles: `
    .mv-backdrop { position: fixed; inset: 0; background: color-mix(in srgb, var(--vc-ink) 35%, transparent); }
    .mv { position: fixed; inset: 12vh auto auto 50%; transform: translateX(-50%); width: min(24rem, calc(100vw - 2rem));
      max-height: 70vh; overflow: auto; background: var(--vc-surface); color: var(--vc-ink);
      border: 1px solid var(--vc-border); border-radius: var(--vc-radius, 12px); padding: 1rem; z-index: 30; }
    .mv__header { display: flex; justify-content: space-between; align-items: center; }
    .mv__list { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 0.35rem; }
    .mv__list button { width: 100%; text-align: start; }
  `,
})
export class MessageMoveDialog {
  private readonly api = inject(MessageHistoryApi);
  private readonly channels = inject(ChannelStore);
  readonly ui = ui;
  readonly open = input(false);
  readonly channelId = input('');
  readonly messageId = input('');
  readonly threadId = input<string | null>(null);
  readonly closed = output<void>();
  readonly moved = output<void>();
  readonly busy = signal(false);
  readonly error = signal(false);
  readonly targets = computed(() =>
    this.channels.channels().filter(
      (channel) => !idsEqual(channel.id, this.channelId()) && channel.type !== 'Announcement',
    ),
  );

  async confirm(targetChannelId: string): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set(false);
    try {
      await this.api.move(
        this.channelId(),
        this.messageId(),
        targetChannelId,
        this.threadId() ? 'thread' : 'message',
      );
      this.moved.emit();
      this.closed.emit();
    } catch {
      this.error.set(true);
    } finally {
      this.busy.set(false);
    }
  }
}
