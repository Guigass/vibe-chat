/** Aligned with API SignalR KeepAliveInterval (15s). */
export const HUB_KEEP_ALIVE_MS = 15_000;

/** Aligned with API SignalR ClientTimeoutInterval (90s). */
export const HUB_SERVER_TIMEOUT_MS = 90_000;

const MAX_RETRY_DELAY_MS = 10_000;

/**
 * Linear backoff 1s…10s with up to 20% jitter (BUG-006).
 * Never returns null — SignalR keeps retrying indefinitely.
 */
export function nextHubRetryDelayMs(
  previousRetryCount: number,
  random: () => number = Math.random,
): number {
  const attempt = Math.max(0, previousRetryCount);
  const base = Math.min(MAX_RETRY_DELAY_MS, (attempt + 1) * 1000);
  const jitter = Math.floor(base * 0.2 * random());
  return base + jitter;
}
