import { Component, DestroyRef, effect, inject, input, signal } from '@angular/core';
import { ChatMessage } from '../models/chat.models';
import { ui } from '../../core/i18n/strings';

/** A per-conversation high-water mark: history, edits and optimistic echoes stay silent. */
export class MessageAnnouncementTracker {
  private scope: string | null = null;
  private maxSeq = 0;
  private initialized = false;

  update(scope: string | null, messages: readonly ChatMessage[], loading: boolean): boolean {
    const max = messages.reduce((seq, message) => Math.max(seq, message.seq ?? 0), 0);
    if (scope !== this.scope || loading || !this.initialized) {
      this.scope = scope;
      this.maxSeq = max;
      this.initialized = !!scope && !loading;
      return false;
    }
    const added = max > this.maxSeq;
    this.maxSeq = Math.max(this.maxSeq, max);
    return added;
  }
}

@Component({
  selector: 'vc-message-announcer',
  standalone: true,
  styles: ':host { display: contents; }',
  template: `<span class="vc-sr-only" role="status" aria-live="polite" aria-atomic="true">{{ announcement() }}</span>`,
})
export class MessageAnnouncer {
  readonly scope = input<string | null>(null);
  readonly messages = input<readonly ChatMessage[]>([]);
  readonly loading = input(false);
  readonly announcement = signal('');
  private readonly tracker = new MessageAnnouncementTracker();
  private timer: ReturnType<typeof setTimeout> | undefined;
  private previousScope: string | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.timer));
    effect(() => {
      const scope = this.scope();
      const loading = this.loading();
      const incoming = this.tracker.update(scope, this.messages(), loading);
      if (scope !== this.previousScope || loading) {
        clearTimeout(this.timer);
        this.announcement.set('');
      }
      this.previousScope = scope;
      if (!incoming) return;
      // One short announcement per burst; never copy message bodies into a live region.
      clearTimeout(this.timer);
      this.announcement.set('');
      this.timer = setTimeout(() => this.announcement.set(ui.timelineNewMessages), 150);
    });
  }
}
