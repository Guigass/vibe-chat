export function formatAudioDuration(ms: number): string {
  const totalSec = Math.max(0, Math.floor(ms / 1000));
  const minutes = Math.floor(totalSec / 60);
  const seconds = totalSec % 60;
  return `${minutes}:${seconds.toString().padStart(2, '0')}`;
}

const FALLBACK_WAVEFORM = [28, 52, 36, 72, 44, 64, 32, 82, 48, 68, 38, 58, 30, 74, 42, 62];

export function drawAudioWaveform(
  canvas: HTMLCanvasElement | null | undefined,
  waveform: number[] | undefined,
  progress = 0,
): void {
  if (!canvas) return;
  const ctx = canvas.getContext('2d');
  if (!ctx) return;

  const width = canvas.width;
  const height = canvas.height;
  ctx.clearRect(0, 0, width, height);
  const playedColor = getComputedStyle(canvas).getPropertyValue('--vc-brand').trim() || '#0d9488';
  const remainingColor =
    getComputedStyle(canvas).getPropertyValue('--vc-ink-subtle').trim() || '#78716c';

  const supplied = waveform?.length ? waveform : FALLBACK_WAVEFORM;
  const samples = supplied.map((value) => Math.min(100, Math.max(0, value)));
  const normalizedProgress = Math.min(1, Math.max(0, Number.isFinite(progress) ? progress : 0));
  const playedBars = Math.round(normalizedProgress * samples.length);

  const barWidth = width / samples.length;
  samples.forEach((value, index) => {
    const barHeight = Math.max(2, (value / 100) * height);
    const x = index * barWidth;
    const y = (height - barHeight) / 2;
    const played = index < playedBars;
    ctx.fillStyle = played ? playedColor : remainingColor;
    ctx.globalAlpha = played ? 0.95 : waveform?.length ? 0.42 : 0.32;
    ctx.fillRect(x, y, Math.max(1, barWidth - 1), barHeight);
  });
  ctx.globalAlpha = 1;
}
