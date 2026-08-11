import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { TimelineScrollAnchorController, applyTimelineScrollAnchor } from './timeline-scroll';

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
