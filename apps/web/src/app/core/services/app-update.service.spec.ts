/** @vitest-environment jsdom */
import '@angular/compiler';
import { DestroyRef, Injector } from '@angular/core';
import { SwUpdate } from '@angular/service-worker';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { Subject } from 'rxjs';
import { AppUpdateService } from './app-update.service';
import { environment } from '../../../environments/environment';

describe('AppUpdateService (B-165)', () => {
  let injector: Injector;
  let versionUpdates$: Subject<{ type: string }>;
  let swUpdate: {
    isEnabled: boolean;
    versionUpdates: Subject<{ type: string }>;
    checkForUpdate: ReturnType<typeof vi.fn>;
    activateUpdate: ReturnType<typeof vi.fn>;
  };
  let reloadMock: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    versionUpdates$ = new Subject();
    swUpdate = {
      isEnabled: true,
      versionUpdates: versionUpdates$,
      checkForUpdate: vi.fn().mockResolvedValue(true),
      activateUpdate: vi.fn().mockResolvedValue(true),
    };
    reloadMock = vi.fn();
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({
        ok: true,
        json: async () => ({
          name: 'VibeChat.Web',
          version: '0.1.0',
          buildId: environment.buildId,
        }),
      }),
    );

    Object.defineProperty(window, 'location', {
      configurable: true,
      value: { reload: reloadMock },
    });

    injector = Injector.create({
      providers: [
        { provide: DestroyRef, useValue: { onDestroy: vi.fn() } },
        { provide: SwUpdate, useValue: swUpdate },
        AppUpdateService,
      ],
    });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  function createService(): AppUpdateService {
    return injector.get(AppUpdateService);
  }

  it('marks update available when SwUpdate emits VERSION_READY', () => {
    const service = createService();
    service.start();
    expect(service.updateAvailable()).toBe(false);

    versionUpdates$.next({ type: 'VERSION_READY' });
    expect(service.updateAvailable()).toBe(true);
  });

  it('marks update available on published buildId mismatch', async () => {
    const service = createService();
    const remoteId = `${environment.buildId}-next`;
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({
        name: 'VibeChat.Web',
        version: '0.1.0',
        buildId: remoteId,
      }),
    });

    await service.checkPublishedVersion(fetchMock as unknown as typeof fetch);
    expect(fetchMock).toHaveBeenCalledWith(
      '/version.json',
      expect.objectContaining({ cache: 'no-store' }),
    );
    expect(service.updateAvailable()).toBe(true);
    expect(service.remoteBuildId()).toBe(remoteId);
  });

  it('ignores matching buildId', async () => {
    const service = createService();
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({
        name: 'VibeChat.Web',
        version: '0.1.0',
        buildId: environment.buildId,
      }),
    });

    await service.checkPublishedVersion(fetchMock as unknown as typeof fetch);
    expect(service.updateAvailable()).toBe(false);
  });

  it('reloads only after applyUpdate (user CTA)', async () => {
    const service = createService();
    service.start();
    versionUpdates$.next({ type: 'VERSION_READY' });

    expect(reloadMock).not.toHaveBeenCalled();
    await service.applyUpdate();
    expect(swUpdate.activateUpdate).toHaveBeenCalled();
    expect(reloadMock).toHaveBeenCalledTimes(1);
  });
});
