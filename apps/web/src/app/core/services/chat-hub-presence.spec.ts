/** @vitest-environment jsdom */
import { TestBed } from '@angular/core/testing';
import { HubConnection, HubConnectionState } from '@microsoft/signalr';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthService } from '../auth/auth.service';
import { TenantContext } from '../tenant/tenant-context';
import { AWAY_GRACE_MS, ChatHubService } from './chat-hub.service';

interface ChatHubPresenceInternals {
  connection: HubConnection | null;
  startPresenceLoop(): void;
  stopPresenceLoop(): void;
}

describe('ChatHubService presence (BUG-008)', () => {
  let service: ChatHubPresenceInternals;
  let visibilityState: DocumentVisibilityState;
  let invoke: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    vi.useFakeTimers();
    visibilityState = 'visible';
    vi.spyOn(document, 'visibilityState', 'get').mockImplementation(() => visibilityState);

    TestBed.configureTestingModule({
      providers: [
        ChatHubService,
        { provide: AuthService, useValue: {} },
        {
          provide: TenantContext,
          useValue: { snapshot: () => ({ tenantId: 'tenant-1' }) },
        },
      ],
    });

    invoke = vi.fn().mockResolvedValue(undefined);
    service = TestBed.inject(ChatHubService) as unknown as ChatHubPresenceInternals;
    service.connection = {
      state: HubConnectionState.Connected,
      invoke,
    } as unknown as HubConnection;
    service.startPresenceLoop();
  });

  afterEach(() => {
    service.stopPresenceLoop();
    TestBed.resetTestingModule();
    vi.restoreAllMocks();
    vi.useRealTimers();
  });

  it('waits for the grace period before setting away', async () => {
    visibilityState = 'hidden';
    document.dispatchEvent(new Event('visibilitychange'));

    expect(invoke).not.toHaveBeenCalledWith('SetAway', 'tenant-1');

    await vi.advanceTimersByTimeAsync(AWAY_GRACE_MS - 1);
    expect(invoke).not.toHaveBeenCalledWith('SetAway', 'tenant-1');

    await vi.advanceTimersByTimeAsync(1);
    expect(invoke).toHaveBeenCalledWith('SetAway', 'tenant-1');
  });

  it('cancels the away timer and heartbeats when visible again', async () => {
    visibilityState = 'hidden';
    document.dispatchEvent(new Event('visibilitychange'));

    await vi.advanceTimersByTimeAsync(AWAY_GRACE_MS / 2);
    visibilityState = 'visible';
    document.dispatchEvent(new Event('visibilitychange'));
    await vi.advanceTimersByTimeAsync(AWAY_GRACE_MS);

    expect(invoke).not.toHaveBeenCalledWith('SetAway', 'tenant-1');
    expect(invoke).toHaveBeenCalledWith('Heartbeat', 'tenant-1');
  });
});
