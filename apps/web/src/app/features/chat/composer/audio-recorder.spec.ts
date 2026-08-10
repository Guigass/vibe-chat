import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import {
  AUDIO_MAX_DURATION_MS,
  downsampleWaveform,
  extensionForMime,
  normalizeAudioContentType,
  resolveAudioMimeType,
} from './audio-recorder';
import { AudioRecorderService } from './audio-recorder.service';

describe('audio-recorder utilities', () => {
  it('selects the first supported MIME type', () => {
    const isTypeSupported = vi.fn((mime: string) => mime.startsWith('audio/webm'));
    vi.stubGlobal('MediaRecorder', { isTypeSupported });

    expect(resolveAudioMimeType()).toBe('audio/webm;codecs=opus');
    vi.unstubAllGlobals();
  });

  it('downsamples waveform to the max point count', () => {
    const input = Array.from({ length: 240 }, (_, i) => i % 100);
    const result = downsampleWaveform(input, 100);
    expect(result).toHaveLength(100);
    expect(result.every((v) => v >= 0 && v <= 100)).toBe(true);
  });

  it('maps MIME types to file extensions', () => {
    expect(extensionForMime('audio/mp4')).toBe('m4a');
    expect(extensionForMime('audio/ogg;codecs=opus')).toBe('ogg');
    expect(extensionForMime('audio/webm')).toBe('webm');
  });

  it('normalizes audio content types by stripping codec parameters', () => {
    expect(normalizeAudioContentType('audio/webm;codecs=opus')).toBe('audio/webm');
    expect(normalizeAudioContentType('audio/ogg;codecs=opus')).toBe('audio/ogg');
    expect(normalizeAudioContentType('  audio/mp4  ')).toBe('audio/mp4');
    expect(normalizeAudioContentType('')).toBe('audio/webm');
  });

  it('exposes the five minute client limit', () => {
    expect(AUDIO_MAX_DURATION_MS).toBe(300_000);
  });
});

describe('AudioRecorderService.buildRecordedAudio', () => {
  it('returns null with an error when the preview blob is empty', async () => {
    const service = new AudioRecorderService();
    service.previewBlob.set(new Blob([], { type: 'audio/webm;codecs=opus' }));
    service.elapsedMs.set(1_500);

    const result = await service.buildRecordedAudio();

    expect(result).toBeNull();
    expect(service.errorMessage()).toContain('inválido ou vazio');
  });

  it('normalizes mime type for a valid preview blob', async () => {
    const service = new AudioRecorderService();
    service.previewBlob.set(new Blob([new Uint8Array([1, 2, 3, 4])], { type: 'audio/webm;codecs=opus' }));
    service.elapsedMs.set(1_500);

    const result = await service.buildRecordedAudio();

    expect(result).not.toBeNull();
    expect(result?.mimeType).toBe('audio/webm');
    expect(result?.durationMs).toBe(1_500);
    expect(result?.fileName).toMatch(/\.webm$/);
  });

  it('ignores late onstop after reset left the recording phase', async () => {
    const service = new AudioRecorderService();
    service.phase.set('idle');
    await (
      service as unknown as {
        onRecorderStop: (mime: string) => Promise<void>;
      }
    ).onRecorderStop('audio/webm');

    expect(service.phase()).toBe('idle');
    expect(service.previewBlob()).toBeNull();
  });
});

describe('AudioRecorderService MediaRecorder lifecycle', () => {
  class FakeMediaRecorder {
    state: 'inactive' | 'recording' | 'paused' = 'inactive';
    ondataavailable: ((event: { data: Blob }) => void) | null = null;
    onstop: (() => void) | null = null;

    constructor(
      _stream: MediaStream,
      _options?: MediaRecorderOptions,
    ) {}

    start(_timeslice?: number): void {
      this.state = 'recording';
      queueMicrotask(() => {
        this.ondataavailable?.({
          data: new Blob([new Uint8Array([1, 2, 3, 4])], { type: 'audio/webm' }),
        });
      });
    }

    stop(): void {
      // Real MediaRecorder stays "recording" until onstop runs.
      queueMicrotask(() => {
        this.state = 'inactive';
        this.ondataavailable?.({
          data: new Blob([new Uint8Array([5, 6])], { type: 'audio/webm' }),
        });
        this.onstop?.();
      });
    }
  }

  beforeEach(() => {
    vi.stubGlobal('isSecureContext', true);
    vi.stubGlobal('MediaRecorder', Object.assign(FakeMediaRecorder, {
      isTypeSupported: (mime: string) => mime.startsWith('audio/webm'),
    }));

    const track = { stop: vi.fn() };
    vi.stubGlobal('navigator', {
      mediaDevices: {
        getUserMedia: vi.fn().mockResolvedValue({
          getTracks: () => [track],
        }),
      },
    });

    vi.stubGlobal(
      'AudioContext',
      class {
        state = 'running';
        createMediaStreamSource() {
          return { connect: vi.fn() };
        }
        createAnalyser() {
          return {
            fftSize: 256,
            getByteTimeDomainData: (data: Uint8Array) => {
              data.fill(128);
            },
          };
        }
        close() {
          this.state = 'closed';
          return Promise.resolve();
        }
      },
    );

    vi.stubGlobal('URL', {
      createObjectURL: vi.fn(() => 'blob:audio-preview'),
      revokeObjectURL: vi.fn(),
    });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('stop() resolves with recorded audio from onstop (send-while-recording path)', async () => {
    const service = new AudioRecorderService();
    const startError = await service.start();
    expect(startError).toBeNull();
    expect(service.phase()).toBe('recording');

    // Let the initial dataavailable land.
    await Promise.resolve();
    await Promise.resolve();

    const recorded = await service.stop();

    expect(recorded).not.toBeNull();
    expect(recorded!.blob.size).toBeGreaterThan(0);
    expect(recorded!.mimeType).toBe('audio/webm');
    expect(service.phase()).toBe('preview');
    expect(service.previewUrl()).toBe('blob:audio-preview');
  });

  it('discard during recording settles stop waiters with null and returns to idle', async () => {
    const service = new AudioRecorderService();
    await service.start();
    await Promise.resolve();

    const stopPromise = service.stop();
    // Concurrent discard while stop is waiting on onstop.
    service.discard();
    const recorded = await stopPromise;

    expect(recorded).toBeNull();
    expect(service.phase()).toBe('idle');
    expect(service.previewBlob()).toBeNull();
  });
});
