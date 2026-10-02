import { AWAY_GRACE_MS } from './chat-hub.types';

export interface HubPresenceHooks {
  heartbeat: () => Promise<void>;
  setAway: () => Promise<void>;
}

/** Visibility heartbeat and away grace. Same timers as the previous hub-owned loop. */
export class HubPresenceLoop {
  private heartbeatTimer: ReturnType<typeof setInterval> | null = null;
  private awayTimer: ReturnType<typeof setTimeout> | null = null;
  private visibilityHandler: (() => void) | null = null;

  start(hooks: HubPresenceHooks): void {
    this.stop();
    this.heartbeatTimer = setInterval(() => {
      if (document.visibilityState === 'visible') {
        void hooks.heartbeat();
      }
    }, 20000);

    this.visibilityHandler = () => {
      if (document.visibilityState === 'hidden') {
        this.clearAwayTimer();
        this.awayTimer = setTimeout(() => {
          this.awayTimer = null;
          void hooks.setAway();
        }, AWAY_GRACE_MS);
      } else {
        this.clearAwayTimer();
        void hooks.heartbeat();
      }
    };
    document.addEventListener('visibilitychange', this.visibilityHandler);
  }

  stop(): void {
    this.clearAwayTimer();
    if (this.heartbeatTimer) {
      clearInterval(this.heartbeatTimer);
      this.heartbeatTimer = null;
    }
    if (this.visibilityHandler) {
      document.removeEventListener('visibilitychange', this.visibilityHandler);
      this.visibilityHandler = null;
    }
  }

  private clearAwayTimer(): void {
    if (this.awayTimer) {
      clearTimeout(this.awayTimer);
      this.awayTimer = null;
    }
  }
}
