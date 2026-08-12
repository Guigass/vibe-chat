/** @vitest-environment jsdom */

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  TimelineScrollAnchorController,
  TimelineStickyBottomPin,
  applyTimelineScrollAnchor,
  shouldStickTimelineToBottom,
} from './timeline-scroll';

function dimension(
  element: HTMLElement,
  name: 'scrollHeight' | 'clientHeight',
  value: () => number,
) {
  Object.defineProperty(element, name, { configurable: true, get: value });
}

describe('timeline scroll anchoring', () => {
  let resizeCallbacks: Array<ResizeObserverCallback>;

  beforeEach(() => {
    vi.useFakeTimers();
    vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) =>
      setTimeout(() => callback(performance.now()), 16),
    );
    vi.stubGlobal('cancelAnimationFrame', (id: ReturnType<typeof setTimeout>) => clearTimeout(id));
    resizeCallbacks = [];
    vi.stubGlobal(
      'ResizeObserver',
      class {
        constructor(callback: ResizeObserverCallback) {
          resizeCallbacks.push(callback);
        }
        observe() {}
        disconnect() {}
        unobserve() {}
      },
    );
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it('anchors at the latest scroll height', () => {
    const scroller = document.createElement('section');
    dimension(scroller, 'scrollHeight', () => 1400);

    expect(applyTimelineScrollAnchor(scroller, { kind: 'bottom' })).toBe(true);
    expect(scroller.scrollTop).toBe(1400);
  });

  it('keeps the viewport stable while prepended content settles (BUG-015)', () => {
    const scroller = document.createElement('section');
    const list = document.createElement('div');
    list.className = 'timeline__list';
    scroller.append(list);
    let height = 1000;
    dimension(scroller, 'scrollHeight', () => height);
    scroller.scrollTop = 120;
    const controller = new TimelineScrollAnchorController(scroller);

    controller.anchor({
      kind: 'prepend',
      previousScrollHeight: height,
      previousScrollTop: scroller.scrollTop,
    });

    height = 1300;
    vi.advanceTimersByTime(16);
    expect(scroller.scrollTop).toBe(420);

    height = 1450;
    vi.advanceTimersByTime(16);
    expect(scroller.scrollTop).toBe(570);

    height = 1520;
    resizeCallbacks[0]([], {} as ResizeObserver);
    vi.advanceTimersByTime(16);
    expect(scroller.scrollTop).toBe(640);

    controller.cancel();
    height = 1700;
    resizeCallbacks[0]([], {} as ResizeObserver);
    vi.advanceTimersByTime(16);
    expect(scroller.scrollTop).toBe(640);
  });

  it('centers a message inside the timeline without scrolling an ancestor', () => {
    const scroller = document.createElement('section');
    const target = document.createElement('article');
    scroller.scrollTop = 200;
    dimension(scroller, 'clientHeight', () => 400);
    vi.spyOn(scroller, 'getBoundingClientRect').mockReturnValue({
      top: 100,
      height: 400,
    } as DOMRect);
    vi.spyOn(target, 'getBoundingClientRect').mockReturnValue({
      top: 700,
      height: 80,
    } as DOMRect);

    expect(applyTimelineScrollAnchor(scroller, { kind: 'element', target: () => target })).toBe(
      true,
    );
    expect(scroller.scrollTop).toBe(640);
  });

  it('reapplies the anchor after late list resize and stops after cancellation', () => {
    const scroller = document.createElement('section');
    const list = document.createElement('div');
    list.className = 'timeline__list';
    scroller.append(list);
    let height = 900;
    dimension(scroller, 'scrollHeight', () => height);
    const anchored = vi.fn();
    const controller = new TimelineScrollAnchorController(scroller);

    controller.anchor({ kind: 'bottom' }, anchored);
    vi.advanceTimersByTime(32);
    expect(scroller.scrollTop).toBe(900);
    expect(anchored).toHaveBeenCalledTimes(1);

    height = 1500;
    resizeCallbacks[0]([], {} as ResizeObserver);
    vi.advanceTimersByTime(16);
    expect(scroller.scrollTop).toBe(1500);
    expect(anchored).toHaveBeenCalledTimes(1);

    controller.cancel();
    height = 2200;
    resizeCallbacks[0]([], {} as ResizeObserver);
    vi.advanceTimersByTime(32);
    expect(scroller.scrollTop).toBe(1500);
  });

  it('settles only after the stabilization window while keeping late resize anchored', () => {
    const scroller = document.createElement('section');
    const list = document.createElement('div');
    list.className = 'timeline__list';
    scroller.append(list);
    let height = 900;
    dimension(scroller, 'scrollHeight', () => height);
    const anchored = vi.fn();
    const settled = vi.fn();
    const controller = new TimelineScrollAnchorController(scroller);

    controller.anchor({ kind: 'bottom' }, anchored, undefined, settled);
    vi.advanceTimersByTime(32);
    expect(anchored).toHaveBeenCalledTimes(1);
    expect(settled).not.toHaveBeenCalled();

    height = 1500;
    resizeCallbacks[0]([], {} as ResizeObserver);
    vi.advanceTimersByTime(16);
    expect(scroller.scrollTop).toBe(1500);
    expect(settled).not.toHaveBeenCalled();

    vi.advanceTimersByTime(1200);
    expect(settled).toHaveBeenCalledTimes(1);
  });

  it('waits for a requested message to enter the rendered page', () => {
    const scroller = document.createElement('section');
    const target = document.createElement('article');
    dimension(scroller, 'clientHeight', () => 300);
    vi.spyOn(scroller, 'getBoundingClientRect').mockReturnValue({ top: 0 } as DOMRect);
    vi.spyOn(target, 'getBoundingClientRect').mockImplementation(
      () => ({ top: 450 - scroller.scrollTop, height: 50 }) as DOMRect,
    );
    let rendered = false;
    const anchored = vi.fn();
    const controller = new TimelineScrollAnchorController(scroller);

    controller.anchor({ kind: 'element', target: () => (rendered ? target : null) }, anchored);
    vi.advanceTimersByTime(32);
    expect(anchored).not.toHaveBeenCalled();

    rendered = true;
    vi.advanceTimersByTime(32);
    expect(anchored).toHaveBeenCalledTimes(1);
    expect(scroller.scrollTop).toBe(325);
  });
});

describe('timeline sticky bottom (BUG-018)', () => {
  let resizeCallbacks: Array<ResizeObserverCallback>;

  beforeEach(() => {
    vi.useFakeTimers();
    vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) =>
      setTimeout(() => callback(performance.now()), 16),
    );
    vi.stubGlobal('cancelAnimationFrame', (id: ReturnType<typeof setTimeout>) => clearTimeout(id));
    resizeCallbacks = [];
    vi.stubGlobal(
      'ResizeObserver',
      class {
        constructor(callback: ResizeObserverCallback) {
          resizeCallbacks.push(callback);
        }
        observe() {}
        disconnect() {}
        unobserve() {}
      },
    );
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it('sticks from the nearBottom latch without remasuring after growth', () => {
    expect(shouldStickTimelineToBottom(false, true)).toBe(true);
    expect(shouldStickTimelineToBottom(false, false)).toBe(false);
  });

  it('forces stick on ownArrival even when the latch is away', () => {
    expect(shouldStickTimelineToBottom(true, false)).toBe(true);
  });

  it('keeps pinning to the latest height while latched, then stops when unpinned', () => {
    const scroller = document.createElement('section');
    const list = document.createElement('div');
    list.className = 'timeline__list';
    scroller.append(list);
    let height = 900;
    dimension(scroller, 'scrollHeight', () => height);
    scroller.scrollTop = 800;

    const pin = new TimelineStickyBottomPin(() => scroller);
    pin.setPinned(true);
    vi.advanceTimersByTime(16);
    expect(scroller.scrollTop).toBe(900);

    height = 1400;
    resizeCallbacks[0]([], {} as ResizeObserver);
    vi.advanceTimersByTime(16);
    expect(scroller.scrollTop).toBe(1400);

    pin.setPinned(false);
    height = 1800;
    resizeCallbacks[0]([], {} as ResizeObserver);
    vi.advanceTimersByTime(16);
    expect(scroller.scrollTop).toBe(1400);

    pin.destroy();
  });

  it('does not re-snap to bottom when setPinned(true) repeats during user scroll', () => {
    const scroller = document.createElement('section');
    const list = document.createElement('div');
    list.className = 'timeline__list';
    scroller.append(list);
    let height = 1200;
    dimension(scroller, 'scrollHeight', () => height);

    const pin = new TimelineStickyBottomPin(() => scroller);
    pin.setPinned(true);
    vi.advanceTimersByTime(16);
    expect(scroller.scrollTop).toBe(1200);

    scroller.scrollTop = 1100;
    pin.setPinned(true);
    vi.advanceTimersByTime(16);
    expect(scroller.scrollTop).toBe(1100);

    height = 1500;
    resizeCallbacks[0]([], {} as ResizeObserver);
    vi.advanceTimersByTime(16);
    expect(scroller.scrollTop).toBe(1500);

    pin.destroy();
  });

  it('arms the pin when the scroller mounts after a cold setPinned(true)', () => {
    const scroller = document.createElement('section');
    const list = document.createElement('div');
    list.className = 'timeline__list';
    scroller.append(list);
    let height = 800;
    dimension(scroller, 'scrollHeight', () => height);
    let ready: HTMLElement | null = null;

    const pin = new TimelineStickyBottomPin(() => ready);
    pin.setPinned(true);
    vi.advanceTimersByTime(16);
    expect(scroller.scrollTop).toBe(0);

    ready = scroller;
    pin.setPinned(true);
    vi.advanceTimersByTime(16);
    expect(scroller.scrollTop).toBe(800);

    height = 1100;
    pin.sync();
    resizeCallbacks.at(-1)?.([], {} as ResizeObserver);
    vi.advanceTimersByTime(16);
    expect(scroller.scrollTop).toBe(1100);

    pin.destroy();
  });
});
