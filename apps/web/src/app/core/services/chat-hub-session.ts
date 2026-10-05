import { HubConnection, HubConnectionState } from '@microsoft/signalr';
import { nextHubRetryDelayMs } from './chat-hub-reconnect';
import { ConnectionStatus } from './chat-hub.types';

export interface HubRetryHooks {
  wantConnected: () => boolean;
  status: () => ConnectionStatus;
  setStatus: (status: ConnectionStatus) => void;
  connection: () => HubConnection | null;
  dropConnection: () => void;
  connect: () => Promise<void>;
}

/** Manual reconnect backoff and online/visibility recovery for the hub. */
export class HubRetrySession {
  private retryTimer: ReturnType<typeof setTimeout> | null = null;
  private manualRetryCount = 0;
  private onlineHandler: (() => void) | null = null;
  private visibilityRetryHandler: (() => void) | null = null;

  resetCount(): void {
    this.manualRetryCount = 0;
  }

  schedule(hooks: HubRetryHooks): void {
    if (!hooks.wantConnected() || this.retryTimer) return;
    if (typeof navigator !== 'undefined' && navigator.onLine === false) {
      // wait for window 'online' listener
      return;
    }
    const delay = nextHubRetryDelayMs(this.manualRetryCount);
    this.manualRetryCount += 1;
    hooks.setStatus('reconnecting');
    this.retryTimer = setTimeout(() => {
      this.retryTimer = null;
      // Drop dead connection so the next start rebuilds cleanly.
      if (hooks.connection()?.state === HubConnectionState.Disconnected) {
        hooks.dropConnection();
      }
      void hooks.connect();
    }, delay);
  }

  clear(): void {
    if (this.retryTimer) {
      clearTimeout(this.retryTimer);
      this.retryTimer = null;
    }
  }

  ensureNetworkListeners(hooks: HubRetryHooks): void {
    if (typeof window === 'undefined') return;
    if (!this.onlineHandler) {
      this.onlineHandler = () => {
        if (hooks.wantConnected() && hooks.status() !== 'connected') {
          this.manualRetryCount = 0;
          this.clear();
          void hooks.connect();
        }
      };
      window.addEventListener('online', this.onlineHandler);
    }
    if (!this.visibilityRetryHandler && typeof document !== 'undefined') {
      this.visibilityRetryHandler = () => {
        if (
          document.visibilityState === 'visible' &&
          hooks.wantConnected() &&
          hooks.status() !== 'connected'
        ) {
          this.manualRetryCount = 0;
          this.clear();
          void hooks.connect();
        }
      };
      document.addEventListener('visibilitychange', this.visibilityRetryHandler);
    }
  }

  removeNetworkListeners(): void {
    if (typeof window !== 'undefined' && this.onlineHandler) {
      window.removeEventListener('online', this.onlineHandler);
      this.onlineHandler = null;
    }
    if (typeof document !== 'undefined' && this.visibilityRetryHandler) {
      document.removeEventListener('visibilitychange', this.visibilityRetryHandler);
      this.visibilityRetryHandler = null;
    }
  }
}
