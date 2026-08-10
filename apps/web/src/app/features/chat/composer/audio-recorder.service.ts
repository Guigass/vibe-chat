import { Injectable, signal } from '@angular/core';
import {
  AUDIO_MAX_DURATION_MS,
  AUDIO_MAX_SIZE_BYTES,
  captureWaveformSample,
  downsampleWaveform,
  extensionForMime,
  isAudioRecordingSupported,
  normalizeAudioContentType,
  resolveAudioMimeType,
} from './audio-recorder';

export type AudioRecorderPhase = 'idle' | 'recording' | 'preview' | 'denied' | 'unsupported';

export interface RecordedAudio {
  blob: Blob;
  mimeType: string;
  fileName: string;
  durationMs: number;
  waveform: number[];
}

@Injectable({ providedIn: 'root' })
export class AudioRecorderService {
  private mediaStream: MediaStream | null = null;
  private mediaRecorder: MediaRecorder | null = null;
  private audioContext: AudioContext | null = null;
  private analyser: AnalyserNode | null = null;
  private waveformSamples: number[] = [];
  private waveformFrame = 0;
  private waveformTimer: number | null = null;
  private startedAt = 0;
  private chunks: Blob[] = [];
  private stopTimer: number | null = null;
  private discardOnStop = false;

  readonly phase = signal<AudioRecorderPhase>('idle');
  readonly elapsedMs = signal(0);
  readonly liveWaveform = signal<number[]>([]);
  readonly previewUrl = signal<string | null>(null);
  readonly previewBlob = signal<Blob | null>(null);
  readonly errorMessage = signal<string | null>(null);

  get supported(): boolean {
    return isAudioRecordingSupported();
  }

  async start(): Promise<string | null> {
    this.resetInternal(false);
    if (!this.supported) {
      this.phase.set('unsupported');
      return 'Gravação de áudio não é suportada neste navegador. Use anexar arquivo.';
    }

    const mimeType = resolveAudioMimeType();
    if (!mimeType) {
      this.phase.set('unsupported');
      return 'Nenhum formato de áudio compatível foi encontrado.';
    }

    try {
      this.mediaStream = await navigator.mediaDevices.getUserMedia({ audio: true });
    } catch {
      this.phase.set('denied');
      return 'Permissão de microfone negada. Você ainda pode anexar um arquivo de áudio.';
    }

    this.audioContext = new AudioContext();
    const source = this.audioContext.createMediaStreamSource(this.mediaStream);
    this.analyser = this.audioContext.createAnalyser();
    this.analyser.fftSize = 256;
    source.connect(this.analyser);

    this.chunks = [];
    this.waveformSamples = [];
    this.waveformFrame = 0;
    this.startedAt = Date.now();
    this.elapsedMs.set(0);
    this.liveWaveform.set([]);
    this.phase.set('recording');

    this.mediaRecorder = new MediaRecorder(this.mediaStream, { mimeType });
    this.mediaRecorder.ondataavailable = (event) => {
      if (event.data.size > 0) {
        this.chunks.push(event.data);
      }
    };
    this.mediaRecorder.onstop = () => void this.onRecorderStop(mimeType);
    this.mediaRecorder.start(250);

    this.waveformTimer = window.setInterval(() => {
      if (!this.analyser) return;
      const sample = captureWaveformSample(this.analyser);
      this.waveformSamples.push(sample);
      this.waveformFrame += 1;
      if (this.waveformFrame % 4 === 0) {
        this.liveWaveform.set([...this.waveformSamples.slice(-60)]);
      }
      const elapsed = Date.now() - this.startedAt;
      this.elapsedMs.set(elapsed);
      if (elapsed >= AUDIO_MAX_DURATION_MS) {
        this.stop();
      }
    }, 100);

    this.stopTimer = window.setTimeout(() => this.stop(), AUDIO_MAX_DURATION_MS);
    return null;
  }

  stop(): void {
    const recorder = this.mediaRecorder;
    if (!recorder) return;
    if (recorder.state === 'recording' || recorder.state === 'paused') {
      recorder.stop();
    }
  }

  discard(): void {
    if (this.mediaRecorder?.state === 'recording') {
      // Keep discardOnStop until onstop; resetInternal would clear the flag too early
      // and let onstop build an empty preview from already-cleared chunks.
      this.discardOnStop = true;
      this.stop();
      return;
    }
    this.resetInternal(true);
  }

  async buildRecordedAudio(): Promise<RecordedAudio | null> {
    const blob = this.previewBlob();
    if (!blob) {
      this.errorMessage.set('Nenhum áudio gravado para enviar.');
      return null;
    }

    const mimeType = normalizeAudioContentType(
      blob.type || resolveAudioMimeType() || 'audio/webm',
    );
    const durationMs = this.elapsedMs();
    if (durationMs <= 0 || blob.size <= 0) {
      this.errorMessage.set('Áudio inválido ou vazio. Grave novamente.');
      return null;
    }

    if (blob.size > AUDIO_MAX_SIZE_BYTES) {
      this.errorMessage.set('Áudio excede 10 MB.');
      return null;
    }

    return {
      blob,
      mimeType,
      fileName: `audio-${Date.now()}.${extensionForMime(mimeType)}`,
      durationMs,
      waveform: downsampleWaveform(this.waveformSamples),
    };
  }

  reset(): void {
    this.resetInternal(true);
  }

  private async onRecorderStop(mimeType: string): Promise<void> {
    this.clearTimers();
    if (this.discardOnStop) {
      this.discardOnStop = false;
      if (this.phase() !== 'idle') {
        this.resetInternal(true);
      }
      return;
    }

    // Channel switch / reset already left recording; do not resurrect a preview.
    if (this.phase() !== 'recording') {
      return;
    }

    const blob = new Blob(this.chunks, { type: mimeType });
    if (blob.size > AUDIO_MAX_SIZE_BYTES) {
      this.errorMessage.set('Áudio excede 10 MB.');
      this.resetInternal(true);
      return;
    }

    const url = URL.createObjectURL(blob);
    this.previewBlob.set(blob);
    this.previewUrl.set(url);
    this.elapsedMs.set(Date.now() - this.startedAt);
    this.phase.set('preview');
    await this.teardownStream();
  }

  private resetInternal(revokePreview: boolean): void {
    this.clearTimers();
    this.discardOnStop = false;
    if (revokePreview) {
      const url = this.previewUrl();
      if (url) URL.revokeObjectURL(url);
    }
    this.previewUrl.set(null);
    this.previewBlob.set(null);
    this.liveWaveform.set([]);
    this.elapsedMs.set(0);
    this.errorMessage.set(null);
    this.phase.set('idle');
    void this.teardownStream();
  }

  private clearTimers(): void {
    if (this.waveformTimer !== null) {
      window.clearInterval(this.waveformTimer);
      this.waveformTimer = null;
    }
    if (this.stopTimer !== null) {
      window.clearTimeout(this.stopTimer);
      this.stopTimer = null;
    }
  }

  private async teardownStream(): Promise<void> {
    // Capture locals before await so a concurrent start() cannot be wiped after close().
    const stream = this.mediaStream;
    const ctx = this.audioContext;
    this.mediaRecorder = null;
    this.chunks = [];
    this.analyser = null;
    this.mediaStream = null;
    this.audioContext = null;

    if (stream) {
      for (const track of stream.getTracks()) {
        track.stop();
      }
    }
    if (ctx && ctx.state !== 'closed') {
      await ctx.close().catch(() => undefined);
    }
  }
}
