const ANCHOR_LIFETIME_MS = 1200;

export type TimelineScrollAnchor =
  | { kind: 'bottom' }
  | { kind: 'element'; target: () => HTMLElement | null }
  | { kind: 'prepend'; previousScrollHeight: number; previousScrollTop: number };

/** Latched stickiness: never remasure distance after content already grew (BUG-018). */
export function shouldStickTimelineToBottom(
  ownArrival: boolean,
  nearBottomLatch: boolean,
): boolean {
  return ownArrival || nearBottomLatch;
}

/**
 * Keeps the scroller glued to the latest message while the user is latched at
 * the bottom. Late layout (images, audio waves, grouping) only re-pins when
 * still enabled — user scroll away calls setPinned(false).
 */
export class TimelineStickyBottomPin {
  private resizeObserver: ResizeObserver | null = null;
  private frameId: number | null = null;
  private pinned = false;

  constructor(private readonly getScroller: () => HTMLElement | null) {}

  setPinned(pinned: boolean): void {
    if (!pinned) {
      this.pinned = false;
      this.teardown();
      return;
    }

    const alreadyPinned = this.pinned;
    const hadObserver = this.resizeObserver !== null;
    this.pinned = true;
    this.ensureObserver();
    const attachedNow = !hadObserver && this.resizeObserver !== null;
    // Snap on first enable or when the scroller finally mounts (cold open).
    // Do not re-snap on repeated setPinned(true) — that fights the wheel.
    if (!alreadyPinned || attachedNow) this.schedulePin();
  }

  /** Re-bind after view/list exists without forcing an extra snap by itself. */
  sync(): void {
    if (!this.pinned) return;
    const hadObserver = this.resizeObserver !== null;
    this.ensureObserver();
    if (!hadObserver && this.resizeObserver !== null) this.schedulePin();
  }

  destroy(): void {
    this.pinned = false;
    this.teardown();
  }

  private ensureObserver(): void {
    if (!this.pinned || typeof ResizeObserver === 'undefined') return;
    const scroller = this.getScroller();
    if (!scroller) return;

    if (!this.resizeObserver) {
      this.resizeObserver = new ResizeObserver(() => this.schedulePin());
      this.resizeObserver.observe(scroller);
    }

    const list = scroller.querySelector<HTMLElement>('.timeline__list');
    if (list) this.resizeObserver.observe(list);
  }

  private schedulePin(): void {
    if (!this.pinned || this.frameId !== null) return;
    this.frameId = requestAnimationFrame(() => {
      this.frameId = null;
      this.pinNow();
    });
  }

  private pinNow(): void {
    if (!this.pinned) return;
    this.ensureObserver();
    const scroller = this.getScroller();
    if (!scroller) return;
    scroller.scrollTop = scroller.scrollHeight;
  }

  private teardown(): void {
    if (this.frameId !== null) {
      cancelAnimationFrame(this.frameId);
      this.frameId = null;
    }
    this.resizeObserver?.disconnect();
    this.resizeObserver = null;
  }
}

export function applyTimelineScrollAnchor(
  scroller: HTMLElement,
  anchor: TimelineScrollAnchor,
): boolean {
  if (anchor.kind === 'bottom') {
    scroller.scrollTop = scroller.scrollHeight;
    return true;
  }

  if (anchor.kind === 'prepend') {
    const addedHeight = scroller.scrollHeight - anchor.previousScrollHeight;
    scroller.scrollTop = anchor.previousScrollTop + Math.max(0, addedHeight);
    return true;
  }

  const target = anchor.target();
  if (!target) return false;

  const scrollerRect = scroller.getBoundingClientRect();
  const targetRect = target.getBoundingClientRect();
  const targetCenter =
    targetRect.top - scrollerRect.top + scroller.scrollTop + targetRect.height / 2;
  scroller.scrollTop = Math.max(0, targetCenter - scroller.clientHeight / 2);
  return true;
}

/**
 * Keeps a newly-opened or explicitly targeted timeline position stable while
 * late layout work (media, grouping, sticky separators or shell resize) lands.
 * User input cancels the controller from Timeline so it never fights manual
 * navigation.
 */
export class TimelineScrollAnchorController {
  private frameId: number | null = null;
  private expiryTimer: ReturnType<typeof setTimeout> | null = null;
  private resizeObserver: ResizeObserver | null = null;
  private generation = 0;
  private anchored = false;

  constructor(private readonly scroller: HTMLElement) {}

  anchor(
    request: TimelineScrollAnchor,
    onAnchored?: () => void,
    onExpired?: () => void,
    onSettled?: () => void,
  ): void {
    this.cancel();
    const generation = this.generation;

    const apply = () => {
      if (generation !== this.generation) return;
      const applied = applyTimelineScrollAnchor(this.scroller, request);
      if (!applied) {
        this.scheduleFrame(apply);
        return;
      }

      if (!this.anchored) {
        this.anchored = true;
        onAnchored?.();
        // One extra painted frame covers Angular view insertion even when the
        // observed list had not acquired its final border box yet.
        this.scheduleFrame(apply);
      }
    };

    if (typeof ResizeObserver !== 'undefined') {
      this.resizeObserver = new ResizeObserver(() => this.scheduleFrame(apply));
      this.resizeObserver.observe(this.scroller);
      const list = this.scroller.querySelector<HTMLElement>('.timeline__list');
      if (list) this.resizeObserver.observe(list);
    }

    this.expiryTimer = setTimeout(() => {
      this.finish(generation, this.anchored ? onSettled : onExpired);
    }, ANCHOR_LIFETIME_MS);
    this.scheduleFrame(apply);
  }

  cancel(): void {
    this.generation += 1;
    this.anchored = false;
    if (this.frameId !== null) {
      cancelAnimationFrame(this.frameId);
      this.frameId = null;
    }
    if (this.expiryTimer !== null) {
      clearTimeout(this.expiryTimer);
      this.expiryTimer = null;
    }
    this.resizeObserver?.disconnect();
    this.resizeObserver = null;
  }

  private scheduleFrame(callback: () => void): void {
    if (this.frameId !== null) return;
    this.frameId = requestAnimationFrame(() => {
      this.frameId = null;
      callback();
    });
  }

  private finish(generation: number, onExpired?: () => void): void {
    if (generation !== this.generation) return;
    this.generation += 1;
    this.anchored = false;
    this.expiryTimer = null;
    if (this.frameId !== null) {
      cancelAnimationFrame(this.frameId);
      this.frameId = null;
    }
    this.resizeObserver?.disconnect();
    this.resizeObserver = null;
    onExpired?.();
  }
}
