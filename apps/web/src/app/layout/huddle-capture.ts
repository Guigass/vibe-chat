import { Component, effect, inject, input, OnDestroy, output, signal } from '@angular/core';
import { AuthService } from '../core/auth/auth.service';
import { LiveApiService, LiveState } from '../core/api/live-api.service';
import { fillTemplate, translateErrorCode, ui } from '../core/i18n/strings';

@Component({
  selector: 'vc-huddle-capture',
  standalone: true,
  template: `
    @if (sharers().length) {
      <p class="capture__banner" role="status" data-testid="huddle-share-banner">
        {{ shareLabel() }}
      </p>
    }
    @if (notice(); as recording) {
      <div class="capture__banner capture__banner--record" role="status" data-testid="huddle-recording-banner">
        <p>{{ recording.state === 'recording' ? recordOn(recording.startedByName) : recordAsk(recording.startedByName) }}</p>
        @if (recording.state === 'consent') {
          <div class="capture__actions">
            <button type="button" data-testid="huddle-consent-accept" (click)="consent('accept')">
              {{ ui.huddleAcceptRecording }}
            </button>
            <button type="button" data-testid="huddle-consent-decline" (click)="consent('decline')">
              {{ ui.huddleDeclineRecording }}
            </button>
            <button type="button" data-testid="huddle-leave-before" (click)="leaveBefore()">
              {{ ui.huddleLeaveBefore }}
            </button>
          </div>
        } @else if (canRecord()) {
          <button type="button" data-testid="huddle-stop-recording" (click)="stopRecording()">
            {{ ui.huddleStopRecording }}
          </button>
        }
      </div>
    }
    @if (connected()) {
      <div class="capture__actions">
        @if (canShare()) {
          <button type="button" data-testid="huddle-share" (click)="toggleShare()">
            {{ sharingSelf() ? ui.huddleStopShare : ui.huddleShare }}
          </button>
        }
        @if (canRecord() && !notice()) {
          <button type="button" data-testid="huddle-record" (click)="startRecording()">
            {{ ui.huddleStartRecording }}
          </button>
        }
      </div>
    }
    @if (error()) {
      <p class="capture__error" role="alert">{{ error() }}</p>
    }
  `,
  styles: `
    :host { display: contents; }
    .capture__banner, .capture__error {
      margin: 0;
      font-size: var(--vc-text-sm);
      line-height: 1.35;
    }
    .capture__banner {
      position: fixed;
      top: 3.25rem;
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
    .capture__banner--record {
      top: 6.5rem;
      background: color-mix(in srgb, var(--vc-danger) 16%, var(--vc-surface-elevated));
    }
    .capture__banner p { margin: 0; }
    .capture__actions { display: flex; flex-wrap: wrap; gap: var(--vc-space-1); }
    .capture__actions button, .capture__banner button {
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
    .capture__actions button:focus-visible, .capture__banner button:focus-visible {
      outline: none;
      box-shadow: var(--vc-focus-ring);
    }
    .capture__error { color: var(--vc-danger); }
    @media (max-width: 960px) {
      .capture__banner, .capture__banner--record {
        top: 13rem;
        bottom: auto;
        left: var(--vc-space-3);
        right: var(--vc-space-3);
        width: auto;
        transform: none;
      }
      .capture__banner--record { top: 13rem; }
    }
  `,
})
export class HuddleCapture implements OnDestroy {
  private readonly api = inject(LiveApiService);
  private readonly auth = inject(AuthService);
  readonly ui = ui;
  readonly channelId = input<string | null>(null);
  readonly session = input<LiveState | null>(null);
  readonly connected = input(false);
  readonly canShare = input(false);
  readonly canRecord = input(false);
  readonly refresh = output<void>();
  readonly left = output<void>();
  readonly error = signal('');
  private display: MediaStream | null = null;
  private published = false;

  constructor() {
    effect(() => {
      const mine = this.sharingSelf();
      if (mine) {
        this.published = true;
        return;
      }
      if (this.published) {
        this.published = false;
        this.releaseDisplay();
      }
    });
  }

  ngOnDestroy(): void {
    this.releaseDisplay();
  }

  sharers(): LiveState['participants'] {
    return this.session()?.participants.filter((person) => person.sharingScreen) ?? [];
  }

  sharingSelf(): boolean {
    const self = this.auth.profile()?.id;
    return !!self && this.sharers().some((person) => person.userId === self);
  }

  shareLabel(): string {
    if (this.sharingSelf()) {
      return ui.huddleShareBannerYou;
    }
    return fillTemplate(ui.huddleShareBanner, { name: this.sharers()[0]?.displayName ?? '' });
  }

  notice(): LiveState['recording'] {
    const recording = this.session()?.recording;
    return recording && (recording.state === 'consent' || recording.state === 'recording') ? recording : null;
  }

  recordAsk(name: string): string {
    return fillTemplate(ui.huddleRecordingConsent, { name });
  }

  recordOn(name: string): string {
    return fillTemplate(ui.huddleRecordingOn, { name });
  }

  async toggleShare(): Promise<void> {
    const channelId = this.channelId();
    const sessionId = this.session()?.sessionId;
    if (!channelId || !sessionId) {
      return;
    }
    this.error.set('');
    try {
      if (this.sharingSelf()) {
        await this.api.liveShareStop(channelId, sessionId);
        this.releaseDisplay();
        this.refresh.emit();
        return;
      }
      const display = await navigator.mediaDevices.getDisplayMedia({ video: true, audio: false });
      this.releaseDisplay();
      this.display = display;
      display.getVideoTracks()[0]?.addEventListener('ended', () => {
        void this.stopShare();
      });
      await this.api.liveShare(channelId, sessionId);
      this.refresh.emit();
    } catch (error) {
      this.releaseDisplay();
      this.error.set(this.describe(error) || ui.huddleShareFailed);
    }
  }

  async startRecording(): Promise<void> {
    const channelId = this.channelId();
    const sessionId = this.session()?.sessionId;
    if (!channelId || !sessionId) {
      return;
    }
    this.error.set('');
    try {
      await this.api.liveRecord(channelId, sessionId, crypto.randomUUID());
      this.refresh.emit();
    } catch (error) {
      this.error.set(this.describe(error));
    }
  }

  async consent(decision: 'accept' | 'decline'): Promise<void> {
    const channelId = this.channelId();
    const session = this.session();
    if (!channelId || !session?.recording) {
      return;
    }
    this.error.set('');
    try {
      await this.api.liveConsent(channelId, session.sessionId, session.recording.id, decision);
      this.refresh.emit();
    } catch (error) {
      this.error.set(this.describe(error));
    }
  }

  async stopRecording(): Promise<void> {
    const channelId = this.channelId();
    const session = this.session();
    if (!channelId || !session?.recording) {
      return;
    }
    this.error.set('');
    try {
      await this.api.liveStopRecording(channelId, session.sessionId, session.recording.id);
      this.refresh.emit();
    } catch (error) {
      this.error.set(this.describe(error));
    }
  }

  leaveBefore(): void {
    this.releaseDisplay();
    this.left.emit();
  }

  private async stopShare(): Promise<void> {
    const channelId = this.channelId();
    const sessionId = this.session()?.sessionId;
    this.releaseDisplay();
    if (!channelId || !sessionId) {
      return;
    }
    await this.api.liveShareStop(channelId, sessionId).catch(() => undefined);
    this.refresh.emit();
  }

  private describe(error: unknown): string {
    const message = error instanceof Error ? error.message : '';
    try {
      const body = JSON.parse(message) as { error?: string };
      return translateErrorCode(body.error);
    } catch {
      return ui.huddleShareFailed;
    }
  }

  private releaseDisplay(): void {
    this.display?.getTracks().forEach((track) => track.stop());
    this.display = null;
  }
}
