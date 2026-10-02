import { afterEach, describe, expect, it, vi } from 'vitest';
import { ReadCursorScheduler } from './message-read-cursor';

describe('ReadCursorScheduler', () => {
  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it('coalesces a higher seq for the same channel', async () => {
    vi.useFakeTimers();
    vi.spyOn(document, 'hasFocus').mockReturnValue(true);
    const persist = vi.fn().mockResolvedValue(undefined);
    const cursor = new ReadCursorScheduler(persist);

    cursor.schedule('c1', 4, true);
    cursor.schedule('c1', 2, true);
    cursor.schedule('c1', 9, true);
    expect(persist).not.toHaveBeenCalled();

    await vi.advanceTimersByTimeAsync(1000);
    expect(persist).toHaveBeenCalledTimes(1);
    expect(persist).toHaveBeenCalledWith('c1', 9);
  });

  it('skips scheduling when persistence is not allowed', async () => {
    vi.useFakeTimers();
    const persist = vi.fn().mockResolvedValue(undefined);
    const cursor = new ReadCursorScheduler(persist);

    cursor.schedule('c1', 4, false);
    await vi.advanceTimersByTimeAsync(1000);
    expect(persist).not.toHaveBeenCalled();
  });

  it('flushes the pending cursor immediately', () => {
    vi.useFakeTimers();
    vi.spyOn(document, 'hasFocus').mockReturnValue(true);
    const persist = vi.fn().mockResolvedValue(undefined);
    const cursor = new ReadCursorScheduler(persist);

    cursor.schedule('c1', 3, true);
    cursor.flushPending();
    expect(persist).toHaveBeenCalledWith('c1', 3);

    vi.advanceTimersByTime(1000);
    expect(persist).toHaveBeenCalledTimes(1);
  });
});
