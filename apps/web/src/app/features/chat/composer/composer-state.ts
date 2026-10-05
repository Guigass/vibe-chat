import { Injectable, signal } from '@angular/core';

/** Mutable composer session owned by one `vc-composer` instance. */
@Injectable()
export class ComposerState {
  readonly draft = signal('');
  readonly validationError = signal<string | null>(null);
  readonly submitting = signal(false);
  readonly sendingAudio = signal(false);
  readonly requireAck = signal(false);
  readonly ackByLocal = signal('');
  boundChannelId: string | null = null;
  restoringDraft = false;
  draftBeforeEdit: { body: string } | null = null;
  lastTyping = 0;
  textarea: () => HTMLTextAreaElement | null = () => null;
}
