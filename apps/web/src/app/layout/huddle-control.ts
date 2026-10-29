import { A11yModule } from '@angular/cdk/a11y';
import { Component, effect, inject, OnDestroy, signal } from '@angular/core';
import { LiveApiService, LiveHealth, LiveState } from '../core/api/live-api.service';
import { fillTemplate, translateErrorCode, ui } from '../core/i18n/strings';
import { ChannelStore } from '../core/services/channel.store';
import { HuddleCapture } from './huddle-capture';
import { HuddleNotes } from './huddle-notes';

const reactions = ['👍', '✅', '👀', '🎉'] as const;

@Component({
  selector: 'vc-huddle-control',
  standalone: true,
  imports: [A11yModule, HuddleCapture, HuddleNotes],
  template: `
    @if (available()) {
      <div class="huddle">
        <vc-huddle-capture
          [channelId]="channelId"
          [session]="state()"
          [connected]="connected()"
          [canShare]="canShare()"
          [canRecord]="canRecord()"
          (refresh)="refreshState()"
          (left)="leave()"
        />
        <vc-huddle-notes [channelId]="channelId" [session]="state()" [connected]="connected()" [canTranscribe]="canTranscribe()" (refresh)="refreshState()" />
        <button
          type="button"
          class="huddle__toggle"
          data-testid="huddle-toggle"
          [attr.aria-expanded]="open()"
          (click)="toggle()"
        >
          {{ inSession() ? ui.huddleJoin : ui.huddleStart }}
        </button>
        @if (open()) {
          <div
            class="huddle__panel"
            role="dialog"
            data-testid="huddle-panel"
            [attr.aria-label]="ui.huddleStart"
            cdkTrapFocus
            [cdkTrapFocusAutoCapture]="true"
            (keydown.escape)="close()"
          >
            <p class="huddle__title">{{ ui.huddleConsent }}</p>
            <p class="huddle__meta">{{ ui.huddlePreflight }}</p>
            @if (health(); as info) {
              <p class="huddle__meta" data-testid="huddle-capacity">
                {{ capacityLabel(info) }}
              </p>
            }
            @if (!videoAllowed()) {
              <p class="huddle__meta">{{ ui.huddleVideoOff }}</p>
            }
            @if (!providerHealthy()) {
              <p class="huddle__error" role="alert">{{ ui.huddleUnavailable }}</p>
            }
            <vc-huddle-notes [channelId]="channelId" [session]="state()" [canTranscribe]="canTranscribe()" [review]="true" (refresh)="refreshState()" />
            <p class="huddle__label">{{ ui.huddleParticipants }}</p>
            @if (!participants().length) {
              <p class="huddle__meta">{{ ui.huddleEmpty }}</p>
            } @else {
              <ul class="huddle__people">
                @for (person of participants(); track person.userId) {
                  <li>
                    <span>{{ person.displayName }}</span>
                    @if (person.audioConnected) {
                      <span>{{ ui.huddleConnected }}</span>
                    }
                    @if (person.sharingScreen) {
                      <span>{{ ui.huddleSharing }}</span>
                    }
                    @if (person.reaction) {
                      <span aria-hidden="true">{{ person.reaction }}</span>
                    }
                  </li>
                }
              </ul>
            }
            @if (connected()) {
              <div class="huddle__actions">
                <button type="button" data-testid="huddle-mute" (click)="toggleMute()">
                  {{ muted() ? ui.huddleUnmute : ui.huddleMute }}
                </button>
                <button type="button" data-testid="huddle-camera" [disabled]="!videoAllowed()" (click)="toggleCamera()">
                  {{ ui.huddleCamera }}
                </button>
                @for (emoji of reactionChoices; track emoji) {
                  <button type="button" [attr.aria-label]="ui.huddleReaction" (click)="react(emoji)">{{ emoji }}</button>
                }
                <button type="button" data-testid="huddle-leave" (click)="leave()">{{ ui.huddleLeave }}</button>
              </div>
            } @else if (providerHealthy()) {
              <div class="huddle__actions">
                <button type="button" data-testid="huddle-confirm" [disabled]="busy()" (click)="confirm()">
                  {{ ui.huddleConfirm }}
                </button>
              </div>
            }
            @if (error()) {
              <p class="huddle__error" role="alert">{{ error() }}</p>
            }
            <button type="button" class="huddle__close" (click)="close()">{{ ui.huddleClose }}</button>
          </div>
        }
      </div>
    }
  `,
  styles: `
    :host { display: contents; }
    .huddle { position: relative; }
    .huddle__toggle, .huddle__actions button, .huddle__close {
      margin: 0; border: 1px solid var(--vc-border); border-radius: var(--vc-radius-md);
      background: transparent; color: var(--vc-ink); font: inherit; font-size: var(--vc-text-sm);
      line-height: 1.2; padding: 0.25rem 0.6rem; cursor: pointer;
    }
    .huddle__toggle:focus-visible, .huddle__actions button:focus-visible, .huddle__close:focus-visible {
      outline: none; box-shadow: var(--vc-focus-ring);
    }
    .huddle__toggle[aria-expanded='true'] { background: color-mix(in srgb, var(--vc-accent) 16%, var(--vc-surface)); }
    .huddle__panel {
      position: absolute; top: calc(100% + 0.35rem); right: 0; z-index: var(--vc-z-overlay);
      display: flex; flex-direction: column; align-items: stretch; gap: var(--vc-space-2);
      width: min(20rem, 82vw); max-height: min(24rem, 70vh); overflow: auto; padding: var(--vc-space-3);
      border: 1px solid var(--vc-border); border-radius: var(--vc-radius-md);
      background: var(--vc-surface-elevated); box-shadow: var(--vc-shadow-soft); color: var(--vc-ink); text-align: left;
    }
    .huddle__title, .huddle__meta, .huddle__label, .huddle__error { margin: 0; font-size: var(--vc-text-sm); }
    .huddle__meta, .huddle__label { color: var(--vc-ink-muted); }
    .huddle__error { color: var(--vc-danger); }
    .huddle__people { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: var(--vc-space-1); }
    .huddle__people li { display: flex; flex-wrap: wrap; gap: var(--vc-space-2); align-items: baseline; font-size: var(--vc-text-sm); }
    .huddle__actions { display: flex; flex-wrap: wrap; gap: var(--vc-space-1); }
    .huddle__actions button:disabled { opacity: 0.45; cursor: not-allowed; }
    .huddle__close { align-self: flex-start; }
    @media (max-width: 960px) {
      .huddle__panel {
        position: fixed; top: 11rem; left: var(--vc-space-3); right: var(--vc-space-3); width: auto;
        max-height: min(18rem, 52vh);
      }
    }
  `,
})
export class HuddleControl implements OnDestroy {
  private readonly channels = inject(ChannelStore);
  private readonly api = inject(LiveApiService);
  readonly ui = ui;
  readonly reactionChoices = reactions;
  readonly available = signal(false);
  readonly open = signal(false);
  readonly busy = signal(false);
  readonly connected = signal(false);
  readonly muted = signal(false);
  readonly error = signal<string | null>(null);
  readonly health = signal<LiveHealth | null>(null);
  readonly state = signal<LiveState | null>(null);
  readonly participants = signal<LiveState['participants']>([]);
  readonly videoAllowed = signal(false);
  readonly canShare = signal(false);
  readonly canRecord = signal(false);
  readonly canTranscribe = signal(false);
  readonly providerHealthy = signal(false);
  readonly inSession = signal(false);
  private mic: MediaStream | null = null;
  private poll: ReturnType<typeof setInterval> | null = null;
  channelId: string | null = null;
  private workspaceId: string | null = null;

  constructor() {
    effect(() => {
      const workspaceId = this.channels.activeWorkspace()?.id ?? null;
      const channelId = this.channels.activeChannel()?.id ?? null;
      if (workspaceId === this.workspaceId && channelId === this.channelId) {
        return;
      }
      this.workspaceId = workspaceId;
      this.channelId = channelId;
      this.resetLocal();
      void this.loadAvailability();
    });
  }

  ngOnDestroy(): void {
    this.stopPoll();
    this.releaseMic();
  }

  capacityLabel(info: LiveHealth): string {
    return fillTemplate(ui.huddleCapacity, { active: info.activeSessions, max: info.maxSessions });
  }

  toggle(): void {
    this.open.update((value) => !value);
    if (this.open()) {
      void this.refreshState();
    }
  }

  close(): void {
    this.open.set(false);
  }

  async confirm(): Promise<void> {
    const channelId = this.channelId;
    if (!channelId || this.busy()) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    try {
      let session = this.state();
      if (!session || session.state !== 'active') {
        session = await this.api.liveStart(channelId, crypto.randomUUID());
        this.applyState(session);
      }
      const grant = await this.api.liveJoin(channelId, session.sessionId);
      this.videoAllowed.set(grant.canPublishVideo);
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true, video: false });
      this.releaseMic();
      this.mic = stream;
      const next = await this.api.liveConnect(channelId, session.sessionId, grant.token);
      this.applyState(next);
      this.connected.set(true);
      this.inSession.set(true);
    } catch (error) {
      this.releaseMic();
      this.connected.set(false);
      this.error.set(this.describe(error));
      if (this.state()?.sessionId && this.channelId) {
        await this.api.liveLeave(this.channelId, this.state()!.sessionId).catch(() => undefined);
      }
    } finally {
      this.busy.set(false);
    }
  }

  async toggleMute(): Promise<void> {
    const channelId = this.channelId;
    const sessionId = this.state()?.sessionId;
    if (!channelId || !sessionId) {
      return;
    }
    const muted = !this.muted();
    this.mic?.getAudioTracks().forEach((track) => {
      track.enabled = !muted;
    });
    try {
      this.applyState(await this.api.liveMedia(channelId, sessionId, { muted }));
      this.muted.set(muted);
    } catch (error) {
      this.error.set(this.describe(error));
    }
  }

  async toggleCamera(): Promise<void> {
    const channelId = this.channelId;
    const sessionId = this.state()?.sessionId;
    if (!channelId || !sessionId || !this.videoAllowed()) {
      return;
    }
    const camera = !this.state()?.participants.some((person) => person.cameraOn) ;
    try {
      if (camera) {
        const video = await navigator.mediaDevices.getUserMedia({ video: true });
        video.getTracks().forEach((track) => this.mic?.addTrack(track));
      } else {
        this.mic?.getVideoTracks().forEach((track) => {
          track.stop();
          this.mic?.removeTrack(track);
        });
      }
      this.applyState(await this.api.liveMedia(channelId, sessionId, { camera }));
    } catch (error) {
      this.error.set(this.describe(error));
    }
  }

  async react(emoji: string): Promise<void> {
    const channelId = this.channelId;
    const sessionId = this.state()?.sessionId;
    if (!channelId || !sessionId) {
      return;
    }
    try {
      this.applyState(await this.api.liveReact(channelId, sessionId, emoji));
    } catch (error) {
      this.error.set(this.describe(error));
    }
  }

  async leave(): Promise<void> {
    const channelId = this.channelId;
    const sessionId = this.state()?.sessionId;
    this.releaseMic();
    this.connected.set(false);
    this.inSession.set(false);
    if (!channelId || !sessionId) {
      return;
    }
    try {
      this.applyState(await this.api.liveLeave(channelId, sessionId));
    } catch (error) {
      this.error.set(this.describe(error));
    }
  }

  private async loadAvailability(): Promise<void> {
    const workspaceId = this.workspaceId;
    if (!workspaceId) {
      this.available.set(false);
      return;
    }
    try {
      const health = await this.api.liveHealth(workspaceId);
      this.health.set(health);
      this.providerHealthy.set(health.healthy);
      this.videoAllowed.set(health.videoEnabled);
      this.canShare.set(health.canShare);
      this.canRecord.set(health.canRecord);
      this.canTranscribe.set(health.canTranscribe);
      this.available.set(health.enabled);
      this.startPoll();
      await this.refreshState();
    } catch {
      this.available.set(false);
      this.health.set(null);
    }
  }

  async refreshState(): Promise<void> {
    const channelId = this.channelId;
    if (!channelId || !this.available()) {
      return;
    }
    try {
      this.applyState(await this.api.liveState(channelId));
    } catch {
      this.state.set(null);
      this.participants.set([]);
      if (!this.connected()) {
        this.inSession.set(false);
      }
    }
  }

  private applyState(state: LiveState): void {
    this.state.set(state);
    this.participants.set(state.participants);
    this.videoAllowed.set(state.videoAllowed);
    this.inSession.set(state.state === 'active' && state.participants.length > 0);
  }

  private describe(error: unknown): string {
    const message = error instanceof Error ? error.message : '';
    try {
      const body = JSON.parse(message) as { error?: string };
      return translateErrorCode(body.error);
    } catch {
      return ui.huddleUnavailable;
    }
  }

  private resetLocal(): void {
    this.close();
    this.releaseMic();
    this.connected.set(false);
    this.muted.set(false);
    this.error.set(null);
    this.state.set(null);
    this.participants.set([]);
    this.inSession.set(false);
    this.available.set(false);
    this.canTranscribe.set(false);
  }

  private startPoll(): void {
    this.stopPoll();
    this.poll = setInterval(() => void this.refreshState(), 4000);
  }

  private stopPoll(): void {
    if (this.poll) {
      clearInterval(this.poll);
      this.poll = null;
    }
  }

  private releaseMic(): void {
    this.mic?.getTracks().forEach((track) => track.stop());
    this.mic = null;
  }
}
