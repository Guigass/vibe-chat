import { Component, effect, inject, input, output, signal } from '@angular/core';
import { LiveApiService, LiveNote, LiveState } from '../core/api/live-api.service';
import { fillTemplate, translateErrorCode, ui } from '../core/i18n/strings';

@Component({
  selector: 'vc-huddle-notes',
  standalone: true,
  template: `
    @if (!review()) {
      @if (banner(); as item) {
        <div class="notes__banner" role="status" data-testid="huddle-notes-banner">
          <p>{{ item }}</p>
          @if (transcript()?.state === 'consent') {
            <div class="notes__actions">
              <button type="button" data-testid="huddle-notes-accept" (click)="consent('accept')">
                {{ ui.huddleAcceptNotes }}
              </button>
              <button type="button" data-testid="huddle-notes-decline" (click)="consent('decline')">
                {{ ui.huddleDeclineNotes }}
              </button>
              @if (canTranscribe()) {
                <button type="button" data-testid="huddle-notes-cancel" (click)="cancel()">
                  {{ ui.huddleCancelNotes }}
                </button>
              }
            </div>
          }
        </div>
      }
      @if (connected() && canTranscribe() && !transcript()) {
        <button type="button" data-testid="huddle-notes-start" (click)="start()">
          {{ ui.huddleStartNotes }}
        </button>
      }
      @if (error()) {
        <p class="notes__error" role="alert">{{ error() }}</p>
      }
    } @else if (note(); as item) {
      <section class="notes" data-testid="huddle-notes-review" [attr.aria-label]="ui.huddleNotesReady">
        <p class="notes__title">{{ ui.huddleNotesReady }}</p>
        @if (item.state === 'failed') {
          <p class="notes__error" role="alert">{{ ui.huddleNotesFailed }}</p>
        } @else {
          <p class="notes__meta">{{ ui.huddleNoteConfidence }}</p>
          <p class="notes__summary">{{ item.summary }}</p>
          <ul class="notes__list">
            @for (segment of item.segments; track segment.startMs) {
              <li>
                <span>{{ citation(segment.startMs, segment.endMs) }}</span>
                <span>{{ segment.text }}</span>
              </li>
            }
          </ul>
          <ul class="notes__list">
            @for (suggestion of item.suggestions; track suggestion.id) {
              <li>
                <span>{{ suggestion.kind === 'task' ? ui.huddleSuggestionTask : ui.huddleSuggestionDecision }}</span>
                <span>{{ citation(suggestion.startMs, suggestion.endMs) }}</span>
                <span>{{ suggestion.text }}</span>
                @if (suggestion.state === 'suggested') {
                  <button type="button" [attr.data-testid]="'huddle-notes-publish-' + suggestion.kind" (click)="reviewSuggestion(suggestion.id, 'confirm')">
                    {{ ui.huddleConfirmSuggestion }}
                  </button>
                  <button type="button" (click)="reviewSuggestion(suggestion.id, 'dismiss')">
                    {{ ui.huddleDismissSuggestion }}
                  </button>
                }
              </li>
            }
          </ul>
        }
        @if (canTranscribe()) {
          @if (!confirmingDelete()) {
            <button type="button" data-testid="huddle-notes-delete" (click)="confirmingDelete.set(true)">
              {{ ui.huddleDeleteNotes }}
            </button>
          } @else {
            <button type="button" data-testid="huddle-notes-delete-confirm" (click)="remove()">
              {{ ui.huddleDeleteNotesConfirm }}
            </button>
          }
        }
        @if (error()) {
          <p class="notes__error" role="alert">{{ error() }}</p>
        }
      </section>
    }
  `,
  styles: `
    :host { display: contents; }
    .notes__banner, .notes__error, .notes__title, .notes__meta, .notes__summary { margin: 0; font-size: var(--vc-text-sm); line-height: 1.35; }
    .notes__banner {
      position: fixed;
      top: 10.75rem;
      left: 50%;
      transform: translateX(-50%);
      z-index: var(--vc-z-dropdown);
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--vc-space-2);
      width: min(28rem, calc(100vw - 2rem));
      padding: var(--vc-space-2) var(--vc-space-3);
      border: 1px solid var(--vc-border);
      border-radius: var(--vc-radius-md);
      background: color-mix(in srgb, var(--vc-accent) 14%, var(--vc-surface-elevated));
      color: var(--vc-ink);
      box-shadow: var(--vc-shadow-soft);
    }
    .notes, .notes__list { display: flex; flex-direction: column; align-items: stretch; gap: var(--vc-space-2); }
    .notes { padding-top: var(--vc-space-2); border-top: 1px solid var(--vc-border); }
    .notes__title { font-weight: 600; }
    .notes__meta, .notes__list { color: var(--vc-ink-muted); }
    .notes__summary { color: var(--vc-ink); }
    .notes__list { list-style: none; margin: 0; padding: 0; }
    .notes__list li { display: flex; flex-wrap: wrap; gap: var(--vc-space-2); align-items: baseline; }
    .notes__actions, .notes__list li { display: flex; flex-wrap: wrap; gap: var(--vc-space-1); }
    .notes__banner button, .notes button, :host > button {
      margin: 0;
      border: 1px solid var(--vc-border);
      border-radius: var(--vc-radius-md);
      background: transparent;
      color: var(--vc-ink);
      font: inherit;
      font-size: var(--vc-text-sm);
      line-height: 1.2;
      padding: 0.25rem 0.6rem;
      cursor: pointer;
    }
    .notes__banner button:focus-visible, .notes button:focus-visible, :host > button:focus-visible {
      outline: none;
      box-shadow: var(--vc-focus-ring);
    }
    .notes__error { color: var(--vc-danger); }
    @media (max-width: 960px) {
      .notes__banner {
        top: auto;
        bottom: 7.5rem;
        left: var(--vc-space-3);
        right: var(--vc-space-3);
        width: auto;
        transform: none;
      }
    }
  `,
})
export class HuddleNotes {
  private readonly api = inject(LiveApiService);
  readonly ui = ui;
  readonly channelId = input<string | null>(null);
  readonly session = input<LiveState | null>(null);
  readonly connected = input(false);
  readonly canTranscribe = input(false);
  readonly review = input(false);
  readonly refresh = output<void>();
  readonly error = signal('');
  readonly note = signal<LiveNote | null>(null);
  readonly confirmingDelete = signal(false);

  constructor() {
    effect(() => {
      const channelId = this.channelId();
      const session = this.session();
      const transcript = session?.transcript;
      if (!this.review() || !channelId || !session || !transcript || (transcript.state !== 'ready' && transcript.state !== 'failed')) {
        this.note.set(null);
        return;
      }
      void this.load(channelId, session.sessionId, transcript.id);
    });
  }

  transcript(): LiveState['transcript'] {
    const item = this.session()?.transcript;
    return item && item.state !== 'deleted' && item.state !== 'declined' ? item : null;
  }

  banner(): string {
    const item = this.transcript();
    if (!item) {
      return '';
    }
    if (item.state === 'consent') {
      return fillTemplate(ui.huddleNotesConsent, { name: item.startedByName });
    }
    if (item.state === 'failed') {
      return ui.huddleNotesFailed;
    }
    if (item.state === 'ready') {
      return ui.huddleNotesReady;
    }
    return ui.huddleNotesQueued;
  }

  citation(startMs: number, endMs: number): string {
    return fillTemplate(ui.huddleCitation, { start: clock(startMs), end: clock(endMs) });
  }

  async start(): Promise<void> {
    const channelId = this.channelId();
    const sessionId = this.session()?.sessionId;
    if (!channelId || !sessionId) {
      return;
    }
    await this.run(() => this.api.liveTranscribe(channelId, sessionId, crypto.randomUUID()));
  }

  async consent(decision: 'accept' | 'decline'): Promise<void> {
    const channelId = this.channelId();
    const session = this.session();
    if (!channelId || !session?.transcript) {
      return;
    }
    await this.run(() => this.api.liveTranscriptConsent(channelId, session.sessionId, session.transcript!.id, decision));
  }

  async cancel(): Promise<void> {
    const channelId = this.channelId();
    const session = this.session();
    if (!channelId || !session?.transcript) {
      return;
    }
    await this.run(() => this.api.liveTranscriptCancel(channelId, session.sessionId, session.transcript!.id));
  }

  async reviewSuggestion(suggestionId: string, action: 'confirm' | 'dismiss'): Promise<void> {
    const channelId = this.channelId();
    const session = this.session();
    if (!channelId || !session?.transcript) {
      return;
    }
    await this.run(() =>
      this.api.liveSuggestionReview(channelId, session.sessionId, session.transcript!.id, suggestionId, action),
    );
  }

  async remove(): Promise<void> {
    const channelId = this.channelId();
    const session = this.session();
    if (!channelId || !session?.transcript) {
      return;
    }
    await this.run(() => this.api.liveTranscriptDelete(channelId, session.sessionId, session.transcript!.id));
    this.confirmingDelete.set(false);
  }

  private async load(channelId: string, sessionId: string, transcriptId: string): Promise<void> {
    try {
      this.note.set(await this.api.liveTranscript(channelId, sessionId, transcriptId));
    } catch (error) {
      this.error.set(describe(error));
    }
  }

  private async run(action: () => Promise<unknown>): Promise<void> {
    this.error.set('');
    try {
      await action();
      this.refresh.emit();
    } catch (error) {
      this.error.set(describe(error));
    }
  }
}

function clock(ms: number): string {
  const total = Math.max(0, Math.floor(ms / 1000));
  const minutes = Math.floor(total / 60);
  const seconds = total % 60;
  return `${minutes}:${seconds.toString().padStart(2, '0')}`;
}

function describe(error: unknown): string {
  const message = error instanceof Error ? error.message : '';
  try {
    const body = JSON.parse(message) as { error?: string };
    return translateErrorCode(body.error);
  } catch {
    return ui.huddleNotesError;
  }
}
