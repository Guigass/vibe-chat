import { describe, expect, it, vi } from 'vitest';
import { drawAudioWaveform, formatAudioDuration } from './audio';

interface DrawnBar {
  color: string | CanvasGradient | CanvasPattern;
  alpha: number;
  height: number;
}

function canvasHarness() {
  const bars: DrawnBar[] = [];
  const context = {
    fillStyle: '',
    globalAlpha: 1,
    clearRect: vi.fn(),
    fillRect: vi.fn(function (this: typeof context, _x: number, _y: number, _w: number, h: number) {
      bars.push({ color: this.fillStyle, alpha: this.globalAlpha, height: h });
    }),
  };
  const canvas = document.createElement('canvas');
  canvas.width = 180;
  canvas.height = 36;
  vi.spyOn(canvas, 'getContext').mockReturnValue(context as unknown as CanvasRenderingContext2D);
  return { canvas, bars };
}

describe('audio waveform rendering (BUG-014)', () => {
  it('formats duration as minutes and seconds', () => {
    expect(formatAudioDuration(65_999)).toBe('1:05');
  });

  it('draws deterministic fallback bars instead of a flat line', () => {
    const { canvas, bars } = canvasHarness();

    drawAudioWaveform(canvas, undefined);

    expect(bars.length).toBeGreaterThan(8);
    expect(new Set(bars.map((bar) => bar.height)).size).toBeGreaterThan(3);
  });

  it('distinguishes played and remaining samples and clamps progress', () => {
    const { canvas, bars } = canvasHarness();

    drawAudioWaveform(canvas, [20, 40, 60, 80], 0.5);

    expect(bars.map((bar) => bar.alpha)).toEqual([0.95, 0.95, 0.42, 0.42]);
    expect(bars[0].color).not.toBe(bars[3].color);

    bars.length = 0;
    drawAudioWaveform(canvas, [20, 40], 5);
    expect(bars.every((bar) => bar.alpha === 0.95)).toBe(true);
  });
});
