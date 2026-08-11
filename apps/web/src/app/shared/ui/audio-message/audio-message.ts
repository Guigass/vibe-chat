import {
  Component,
  DestroyRef,
  ElementRef,
  computed,
  effect,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { drawAudioWaveform, formatAudioDuration } from '../../utils/audio';
import { MessageAttachment } from '../../models/chat.models';

const PLAYBACK_RATES = [1, 1.5, 2] as const;

@Component({
  selector: 'vc-audio-message',
  standalone: true,
  template: `
    <div class="vc-audio" [attr.aria-label]="ariaLabel()">
      <button
        type="button"
        class="vc-audio__play"
        [attr.aria-label]="playing() ? 'Pausar áudio' : 'Reproduir áudio'"
        (click)="togglePlayback()"
        [disabled]="!downloadUrl()"
      >
        {{ playing() ? '❚❚' : '▶' }}
      </button>

      <div class="vc-audio__body">
        <canvas
          #waveCanvas
          class="vc-audio__wave"
          width="180"
          height="36"
          aria-hidden="true"
        ></canvas>
        <div class="vc-audio__meta">
          <span>{{ elapsedLabel() }}</span>
          <span>{{ durationLabel() }}</span>
        </div>
        <input
          type="range"
          min="0"
          max="1000"
          [value]="progressValue()"
          (input)="onScrub($event)"
          aria-label="Progresso do áudio"
          [disabled]="!downloadUrl()"
        />
      </div>

      <button
        type="button"
        class="vc-audio__speed"
        (click)="cycleSpeed()"
        [attr.aria-label]="'Velocidade ' + speed() + 'x'"
        [disabled]="!downloadUrl()"
      >
        {{ speed() }}×
      </button>
    </div>
  `,
  styles: `
    .vc-audio {
      display: grid;
      grid-template-columns: auto 1fr auto;
      gap: 0.55rem;
      align-items: center;
      margin-top: 0.35rem;
      padding: 0.45rem 0.55rem;
      border-radius: var(--vc-radius-sm);
      background: color-mix(in srgb, var(--vc-surface) 90%, var(--vc-brand));
      border: 1px solid var(--vc-border);
      min-width: 14rem;
    }
    .vc-audio__play,
    .vc-audio__speed {
      border: 0;
      background: transparent;
      color: var(--vc-brand);
      cursor: pointer;
      font: inherit;
      font-size: 0.82rem;
      padding: 0.15rem 0.25rem;
    }
    .vc-audio__play:disabled,
    .vc-audio__speed:disabled,
    input[type='range']:disabled {
      opacity: 0.45;
      cursor: not-allowed;
    }
    .vc-audio__body {
      display: grid;
      gap: 0.2rem;
    }
    .vc-audio__wave {
      width: 100%;
      height: 36px;
      display: block;
    }
    .vc-audio__meta {
      display: flex;
      justify-content: space-between;
      font-size: 0.68rem;
      color: var(--vc-ink-subtle);
    }
    input[type='range'] {
      width: 100%;
    }
  `,
})
export class AudioMessage {
  private readonly destroyRef = inject(DestroyRef);
  readonly attachment = input.required<MessageAttachment>();
  readonly downloadUrl = input<string | null>(null);

  readonly waveCanvas = viewChild<ElementRef<HTMLCanvasElement>>('waveCanvas');

  readonly playing = signal(false);
  readonly elapsedMs = signal(0);
  readonly speed = signal<(typeof PLAYBACK_RATES)[number]>(1);
  private audio: HTMLAudioElement | null = null;

  readonly durationLabel = computed(() => formatAudioDuration(this.attachment().durationMs ?? 0));
  readonly elapsedLabel = computed(() => formatAudioDuration(this.elapsedMs()));
  readonly progressValue = computed(() => {
    const duration = this.attachment().durationMs ?? 0;
    if (duration <= 0) return 0;
    return Math.round((this.elapsedMs() / duration) * 1000);
  });
  readonly playbackProgress = computed(() => this.progressValue() / 1000);
  readonly ariaLabel = computed(() => `Mensagem de áudio, duração ${this.durationLabel()}`);

  constructor() {
    effect(() => {
      drawAudioWaveform(
        this.waveCanvas()?.nativeElement,
        this.attachment().waveform,
        this.playbackProgress(),
      );
    });
    this.destroyRef.onDestroy(() => this.stopAudio());
  }

  togglePlayback(): void {
    const url = this.downloadUrl();
    if (!url) return;

    if (!this.audio) {
      this.audio = new Audio(url);
      this.audio.playbackRate = this.speed();
      this.audio.ontimeupdate = () =>
        this.elapsedMs.set(Math.round((this.audio?.currentTime ?? 0) * 1000));
      this.audio.onended = () => {
        this.playing.set(false);
        this.elapsedMs.set(0);
      };
    }

    if (this.playing()) {
      this.audio.pause();
      this.playing.set(false);
      return;
    }

    void this.audio
      .play()
      .then(() => this.playing.set(true))
      .catch(() => this.playing.set(false));
  }

  cycleSpeed(): void {
    const current = PLAYBACK_RATES.indexOf(this.speed());
    const next = PLAYBACK_RATES[(current + 1) % PLAYBACK_RATES.length];
    this.speed.set(next);
    if (this.audio) {
      this.audio.playbackRate = next;
    }
  }

  onScrub(event: Event): void {
    const value = Number((event.target as HTMLInputElement).value);
    const duration = this.attachment().durationMs ?? 0;
    if (!this.audio || duration <= 0) return;
    this.audio.currentTime = (value / 1000) * (duration / 1000);
    this.elapsedMs.set(Math.round(this.audio.currentTime * 1000));
  }

  private stopAudio(): void {
    if (!this.audio) return;
    this.audio.pause();
    this.audio = null;
    this.playing.set(false);
  }
}
