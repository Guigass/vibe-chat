import { describe, expect, it, vi } from 'vitest';
import {
  AUDIO_MAX_DURATION_MS,
  downsampleWaveform,
  extensionForMime,
  resolveAudioMimeType,
} from './audio-recorder';

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

  it('exposes the five minute client limit', () => {
    expect(AUDIO_MAX_DURATION_MS).toBe(300_000);
  });
});
