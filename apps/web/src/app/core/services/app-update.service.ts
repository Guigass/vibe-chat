import { DestroyRef, Injectable, inject, isDevMode, signal } from '@angular/core';
import { SwUpdate, VersionReadyEvent } from '@angular/service-worker';
import { environment } from '../../../environments/environment';

export type WebVersionInfo = {
  name: string;
  version: string;
  buildId: string;
};

/** Interval between light /version.json checks (boot + focus also trigger). */
const VERSION_POLL_MS = 45 * 60 * 1000;

/** SW message channel can hang when there is no pending worker — never block the CTA. */
const SW_ACTIVATE_TIMEOUT_MS = 2500;

function isVersionReady(event: { type: string }): event is VersionReadyEvent {
  return event.type === 'VERSION_READY';
}

function withTimeout<T>(promise: Promise<T>, ms: number): Promise<T | 'timeout'> {
  return new Promise((resolve, reject) => {
    const timer = window.setTimeout(() => resolve('timeout'), ms);
    promise.then(
      (value) => {
        window.clearTimeout(timer);
        resolve(value);
      },
      (err: unknown) => {
        window.clearTimeout(timer);
        reject(err);
      },
    );
  });
}

@Injectable({ providedIn: 'root' })
export class AppUpdateService {
  private readonly destroyRef = inject(DestroyRef);
  private readonly swUpdate = inject(SwUpdate, { optional: true });

  private readonly updateAvailableSignal = signal(false);
  private readonly remoteBuildIdSignal = signal<string | null>(null);
  private readonly applyingSignal = signal(false);
  private started = false;
  private pollTimer: ReturnType<typeof setInterval> | null = null;

  readonly updateAvailable = this.updateAvailableSignal.asReadonly();
  readonly remoteBuildId = this.remoteBuildIdSignal.asReadonly();
  readonly applying = this.applyingSignal.asReadonly();
  readonly embeddedBuildId = environment.buildId;

  /** Call once from the app root; idempotent. */
  start(): void {
    if (this.started || typeof window === 'undefined') {
      return;
    }
    this.started = true;

    void this.checkPublishedVersion();

    const onFocus = () => {
      void this.checkPublishedVersion();
    };
    const onVisibility = () => {
      if (document.visibilityState === 'visible') {
        void this.checkPublishedVersion();
      }
    };

    window.addEventListener('focus', onFocus);
    document.addEventListener('visibilitychange', onVisibility);
    this.pollTimer = setInterval(() => {
      void this.checkPublishedVersion();
    }, VERSION_POLL_MS);

    this.destroyRef.onDestroy(() => {
      window.removeEventListener('focus', onFocus);
      document.removeEventListener('visibilitychange', onVisibility);
      if (this.pollTimer) {
        clearInterval(this.pollTimer);
        this.pollTimer = null;
      }
    });

    if (this.swUpdate?.isEnabled) {
      const sub = this.swUpdate.versionUpdates.subscribe((event) => {
        if (isVersionReady(event)) {
          this.updateAvailableSignal.set(true);
        }
      });
      this.destroyRef.onDestroy(() => sub.unsubscribe());

      // Prod SW: also ask the worker to check periodically.
      void this.swUpdate.checkForUpdate().catch(() => {
        /* ignore — offline / first install */
      });
    } else if (isDevMode()) {
      // Lab without SW still benefits from /version.json mismatch (e.g. Compose rebuild).
    }
  }

  async checkPublishedVersion(fetchImpl: typeof fetch = fetch): Promise<void> {
    try {
      const response = await fetchImpl('/version.json', {
        cache: 'no-store',
        headers: { Accept: 'application/json' },
      });
      if (!response.ok) {
        return;
      }
      const body = (await response.json()) as Partial<WebVersionInfo>;
      if (typeof body.buildId !== 'string' || body.buildId.length === 0) {
        return;
      }
      this.remoteBuildIdSignal.set(body.buildId);
      if (body.buildId !== environment.buildId) {
        this.updateAvailableSignal.set(true);
      }
    } catch {
      /* network / parse — keep quiet */
    }
  }

  /** Activate pending SW (if any) and reload. Only invoke from an explicit user CTA. */
  async applyUpdate(): Promise<void> {
    if (this.applyingSignal()) {
      return;
    }
    this.applyingSignal.set(true);
    try {
      let activated = false;
      if (this.swUpdate?.isEnabled) {
        try {
          const result = await withTimeout(
            this.swUpdate.activateUpdate(),
            SW_ACTIVATE_TIMEOUT_MS,
          );
          activated = result === true;
        } catch {
          /* still reload — mismatch path does not need SW activate */
        }
      }

      // version.json mismatch often has no waiting SW; drop stale SW/caches so reload
      // fetches the new shell instead of replaying the old ngsw assets.
      if (!activated) {
        await this.clearStaleClientCaches();
      }

      this.reloadWindow();
    } finally {
      this.applyingSignal.set(false);
    }
  }

  /** Test seam — override in unit tests instead of stubbing window.location. */
  reloadWindow(): void {
    window.location.reload();
  }

  private async clearStaleClientCaches(): Promise<void> {
    if (typeof navigator !== 'undefined' && 'serviceWorker' in navigator) {
      try {
        const registrations = await navigator.serviceWorker.getRegistrations();
        await Promise.all(registrations.map((reg) => reg.unregister()));
      } catch {
        /* ignore */
      }
    }
    if (typeof caches !== 'undefined') {
      try {
        const keys = await caches.keys();
        await Promise.all(keys.map((key) => caches.delete(key)));
      } catch {
        /* ignore */
      }
    }
  }
}
