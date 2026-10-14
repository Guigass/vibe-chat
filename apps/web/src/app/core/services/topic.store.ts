import { Injectable, signal } from '@angular/core';

/** Inbox asks the topic board to open a followed topic after the channel is selected. */
@Injectable({ providedIn: 'root' })
export class TopicStore {
  readonly pendingId = signal<string | null>(null);

  requestOpen(topicId: string): void {
    this.pendingId.set(topicId);
  }
}
