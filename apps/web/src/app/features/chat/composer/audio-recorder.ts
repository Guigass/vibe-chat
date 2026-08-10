export const AUDIO_MAX_DURATION_MS = 300_000;
export const AUDIO_MAX_SIZE_BYTES = 10 * 1024 * 1024;
export const AUDIO_WAVEFORM_MAX_POINTS = 100;

const MIME_CANDIDATES = [
  'audio/webm;codecs=opus',
  'audio/ogg;codecs=opus',
  'audio/mp4',
  'audio/webm',
  'audio/ogg',
] as const;

export function isAudioRecordingSupported(): boolean {
  return typeof window !== 'undefined'
    && window.isSecureContext
    && typeof MediaRecorder !== 'undefined'
    && typeof navigator.mediaDevices?.getUserMedia === 'function';
}

export function resolveAudioMimeType(): string | null {
  if (typeof MediaRecorder === 'undefined') {
    return null;
  }

  for (const mime of MIME_CANDIDATES) {
    if (MediaRecorder.isTypeSupported(mime)) {
      return mime;
    }
  }

  return null;
}

export function extensionForMime(mime: string): string {
  if (mime.includes('mp4')) return 'm4a';
  if (mime.includes('ogg')) return 'ogg';
  return 'webm';
}

/** Strip codec parameters so initiate/PUT use a stable base type (e.g. audio/webm). */
export function normalizeAudioContentType(mime: string): string {
  const trimmed = mime.trim();
  if (!trimmed) return 'audio/webm';
  const base = trimmed.split(';', 1)[0]?.trim().toLowerCase();
  return base || 'audio/webm';
}

export function formatDuration(ms: number): string {
  const totalSec = Math.max(0, Math.floor(ms / 1000));
  const minutes = Math.floor(totalSec / 60);
  const seconds = totalSec % 60;
  return `${minutes}:${seconds.toString().padStart(2, '0')}`;
}

export function downsampleWaveform(samples: number[], targetPoints = AUDIO_WAVEFORM_MAX_POINTS): number[] {
  if (samples.length <= targetPoints) {
    return samples.map((v) => Math.round(Math.min(100, Math.max(0, v))));
  }

  const result: number[] = [];
  const bucketSize = samples.length / targetPoints;
  for (let i = 0; i < targetPoints; i += 1) {
    const start = Math.floor(i * bucketSize);
    const end = Math.min(samples.length, Math.floor((i + 1) * bucketSize));
    const slice = samples.slice(start, Math.max(start + 1, end));
    const avg = slice.reduce((sum, v) => sum + v, 0) / slice.length;
    result.push(Math.round(Math.min(100, Math.max(0, avg))));
  }

  return result;
}

export function captureWaveformSample(analyser: AnalyserNode): number {
  const data = new Uint8Array(analyser.fftSize);
  analyser.getByteTimeDomainData(data);
  let peak = 0;
  for (let i = 0; i < data.length; i += 1) {
    const deviation = Math.abs(data[i] - 128);
    if (deviation > peak) {
      peak = deviation;
    }
  }

  return Math.round(Math.min(100, (peak / 128) * 100));
}
