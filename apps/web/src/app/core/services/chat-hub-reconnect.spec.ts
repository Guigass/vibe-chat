import { describe, expect, it } from 'vitest';
import {
  HUB_KEEP_ALIVE_MS,
  HUB_SERVER_TIMEOUT_MS,
  nextHubRetryDelayMs,
} from './chat-hub-reconnect';

describe('chat-hub-reconnect (BUG-006)', () => {
  it('keeps client timeouts aligned with API SignalR options', () => {
    expect(HUB_KEEP_ALIVE_MS).toBe(15_000);
    expect(HUB_SERVER_TIMEOUT_MS).toBe(90_000);
    expect(HUB_SERVER_TIMEOUT_MS).toBeGreaterThanOrEqual(HUB_KEEP_ALIVE_MS * 2);
  });

  it('uses linear backoff capped at 10s', () => {
    const noJitter = () => 0;
    expect(nextHubRetryDelayMs(0, noJitter)).toBe(1000);
    expect(nextHubRetryDelayMs(1, noJitter)).toBe(2000);
    expect(nextHubRetryDelayMs(4, noJitter)).toBe(5000);
    expect(nextHubRetryDelayMs(9, noJitter)).toBe(10_000);
    expect(nextHubRetryDelayMs(20, noJitter)).toBe(10_000);
  });

  it('adds up to 20% jitter without exceeding the cap by much', () => {
    const fullJitter = () => 1;
    expect(nextHubRetryDelayMs(0, fullJitter)).toBe(1000 + Math.floor(1000 * 0.2));
    expect(nextHubRetryDelayMs(9, fullJitter)).toBe(10_000 + Math.floor(10_000 * 0.2));
  });

  it('never returns null so SignalR keeps retrying', () => {
    for (let i = 0; i < 30; i += 1) {
      const delay = nextHubRetryDelayMs(i, () => 0.5);
      expect(delay).toBeTypeOf('number');
      expect(delay).toBeGreaterThan(0);
    }
  });
});
