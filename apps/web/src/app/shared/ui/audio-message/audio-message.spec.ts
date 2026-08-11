import '@angular/compiler';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AudioMessage } from './audio-message';

class FakeAudio {
  currentTime = 0;
  playbackRate = 1;
  ontimeupdate: (() => void) | null = null;
  onended: (() => void) | null = null;
  readonly play = vi.fn().mockResolvedValue(undefined);
  readonly pause = vi.fn();
}

describe('AudioMessage playback waveform (BUG-014)', () => {
  let fakeAudio: FakeAudio;
  let fillRect: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    fakeAudio = new FakeAudio();
    vi.stubGlobal(
      'Audio',
      vi.fn(function AudioMock() {
        return fakeAudio;
      }),
    );
    fillRect = vi.fn();
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue({
      clearRect: vi.fn(),
      fillRect,
      fillStyle: '',
      globalAlpha: 1,
    } as unknown as CanvasRenderingContext2D);
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
    TestBed.resetTestingModule();
  });

  it('redraws the bars as timeupdate and scrub advance playback without pausing', async () => {
    const fixture = TestBed.createComponent(AudioMessage);
    fixture.componentRef.setInput('attachment', {
      id: 'audio-1',
      fileName: 'voice.webm',
      contentType: 'audio/webm',
      sizeBytes: 1024,
      kind: 'Audio',
      durationMs: 10_000,
      waveform: [20, 40, 60, 80],
    });
    fixture.componentRef.setInput('downloadUrl', 'https://example.test/voice.webm');
    fixture.detectChanges();
    await fixture.whenStable();
    const initialDraws = fillRect.mock.calls.length;

    fixture.componentInstance.togglePlayback();
    await Promise.resolve();
    expect(fixture.componentInstance.playing()).toBe(true);

    fakeAudio.currentTime = 5;
    fakeAudio.ontimeupdate?.();
    fixture.detectChanges();
    await fixture.whenStable();
    expect(fixture.componentInstance.progressValue()).toBe(500);
    expect(fillRect.mock.calls.length).toBeGreaterThan(initialDraws);
    expect(fakeAudio.pause).not.toHaveBeenCalled();

    fixture.componentInstance.onScrub({ target: { value: '750' } } as unknown as Event);
    expect(fakeAudio.currentTime).toBe(7.5);
    expect(fixture.componentInstance.progressValue()).toBe(750);

    fakeAudio.onended?.();
    expect(fixture.componentInstance.progressValue()).toBe(0);
    expect(fixture.componentInstance.playing()).toBe(false);
  });
});
