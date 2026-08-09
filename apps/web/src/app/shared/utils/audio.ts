export function formatAudioDuration(ms: number): string {
  const totalSec = Math.max(0, Math.floor(ms / 1000));
  const minutes = Math.floor(totalSec / 60);
  const seconds = totalSec % 60;
  return `${minutes}:${seconds.toString().padStart(2, '0')}`;
}

export function drawAudioWaveform(
  canvas: HTMLCanvasElement | null | undefined,
  waveform: number[] | undefined,
): void {
  if (!canvas) return;
  const ctx = canvas.getContext('2d');
  if (!ctx) return;

  const width = canvas.width;
  const height = canvas.height;
  ctx.clearRect(0, 0, width, height);
  ctx.fillStyle = getComputedStyle(canvas).getPropertyValue('--vc-brand').trim() || '#0d9488';

  const samples = waveform ?? [];
  if (!samples.length) {
    ctx.globalAlpha = 0.35;
    ctx.fillRect(0, height / 2 - 1, width, 2);
    ctx.globalAlpha = 1;
    return;
  }

  const barWidth = width / samples.length;
  samples.forEach((value, index) => {
    const barHeight = Math.max(2, (value / 100) * height);
    const x = index * barWidth;
    const y = (height - barHeight) / 2;
    ctx.globalAlpha = 0.85;
    ctx.fillRect(x, y, Math.max(1, barWidth - 1), barHeight);
  });
  ctx.globalAlpha = 1;
}
