import { describe, expect, it, vi } from 'vitest';
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
