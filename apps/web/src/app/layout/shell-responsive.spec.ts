import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiService } from '../core/api/api.service';
import { AuthService } from '../core/auth/auth.service';
import { ChannelStore } from '../core/services/channel.store';
import { ChatHubService } from '../core/services/chat-hub.service';
import { MessageStore } from '../core/services/message.store';
import { ThreadStore } from '../core/services/thread.store';
import { ShellPage } from './shell.page';
import { SHELL_NARROW_MEDIA_QUERY } from './shell-viewport';

type MediaListener = (event: MediaQueryListEvent) => void;

function stubMatchMedia(initialMatches: boolean): {
  setMatches: (matches: boolean) => void;
} {
  let matches = initialMatches;
  const listeners = new Set<MediaListener>();

  Object.defineProperty(window, 'matchMedia', {
    writable: true,
    configurable: true,
    value: vi.fn().mockImplementation((query: string) => ({
      get matches() {
        return query === SHELL_NARROW_MEDIA_QUERY ? matches : false;
      },
      media: query,
      onchange: null,
      addListener: vi.fn(),
      removeListener: vi.fn(),
      addEventListener: vi.fn((_: string, cb: MediaListener) => {
        listeners.add(cb);
      }),
      removeEventListener: vi.fn((_: string, cb: MediaListener) => {
        listeners.delete(cb);
      }),
      dispatchEvent: vi.fn(),
    })),
  });

  return {
    setMatches(next: boolean) {
      matches = next;
      const event = { matches: next } as MediaQueryListEvent;
      for (const listener of listeners) {
        listener(event);
      }
    },
  };
}

describe('ShellPage responsive sidebar (UX-003)', () => {
  const activeChannel = signal<{ id: string; name: string; isDirect?: boolean } | null>({
    id: 'ch-1',
    name: 'geral',
  });

  beforeEach(async () => {
    activeChannel.set({ id: 'ch-1', name: 'geral' });
    await TestBed.configureTestingModule({
      imports: [ShellPage],
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            profile: () => ({ name: 'Alice' }),
            isOfflineDemo: () => true,
            logout: vi.fn(),
          },
        },
        {
          provide: ChannelStore,
          useValue: {
            activeChannel: activeChannel.asReadonly(),
            activeChannelId: () => activeChannel()?.id ?? null,
            activeWorkspace: () => ({ id: 'ws-1', name: 'Acme' }),
            workspaces: () => [{ id: 'ws-1', name: 'Acme' }],
            error: () => null,
            isDemo: () => true,
            load: vi.fn().mockResolvedValue(undefined),
            selectWorkspace: vi.fn(),
            setPresence: vi.fn(),
            spaceGroups: () => [],
            spaces: () => [],
            directChannels: () => [],
            peerCandidates: () => [],
            presence: () => ({}),
            canCreateChannel: () => false,
            loading: () => false,
            selectChannel: vi.fn(),
          },
        },
        {
          provide: MessageStore,
          useValue: {
            loadChannel: vi.fn().mockResolvedValue(undefined),
          },
        },
        {
          provide: ThreadStore,
          useValue: {
            open: () => false,
            close: vi.fn(),
          },
        },
        {
          provide: ChatHubService,
          useValue: {
            status: () => 'connected',
            connect: vi.fn().mockResolvedValue(undefined),
            onPresenceChanged: () => () => undefined,
          },
        },
        {
          provide: ApiService,
          useValue: {
            searchMessages: vi.fn(),
          },
        },
      ],
    })
      .overrideComponent(ShellPage, {
        set: {
          template: `
            <div
              class="shell"
              [class.shell--sidebar-collapsed]="!sidebarOpen()"
              [class.shell--narrow]="narrowViewport()"
            >
              @if (narrowViewport() && sidebarOpen()) {
                <button type="button" class="shell__backdrop" aria-label="Fechar barra lateral"></button>
              }
              <aside class="shell__sidebar" [attr.aria-hidden]="!sidebarOpen()"></aside>
              @if (!sidebarOpen()) {
                <button type="button" aria-label="Abrir barra lateral" (click)="toggleSidebar()"></button>
              }
            </div>
          `,
          imports: [],
        },
      })
      .compileComponents();
  });

  it('auto-collapses on narrow viewports and restores on desktop', async () => {
    const media = stubMatchMedia(true);
    const fixture = TestBed.createComponent(ShellPage);
    fixture.detectChanges();
    await fixture.whenStable();

    const host = fixture.nativeElement as HTMLElement;
    expect(fixture.componentInstance.narrowViewport()).toBe(true);
    expect(fixture.componentInstance.sidebarOpen()).toBe(false);
    expect(host.querySelector('.shell--sidebar-collapsed')).toBeTruthy();
    expect(host.querySelector('[aria-label="Abrir barra lateral"]')).toBeTruthy();
    expect(host.querySelector('.shell__backdrop')).toBeNull();

    fixture.componentInstance.toggleSidebar();
    fixture.detectChanges();
    expect(host.querySelector('.shell__backdrop')).toBeTruthy();
    expect(host.querySelector('.shell__sidebar')?.getAttribute('aria-hidden')).toBe('false');

    media.setMatches(false);
    fixture.detectChanges();
    expect(fixture.componentInstance.narrowViewport()).toBe(false);
    expect(fixture.componentInstance.sidebarOpen()).toBe(true);
    expect(host.querySelector('.shell--sidebar-collapsed')).toBeNull();
  });

  it('closes the narrow overlay with Escape', async () => {
    stubMatchMedia(true);
    const fixture = TestBed.createComponent(ShellPage);
    fixture.detectChanges();
    await fixture.whenStable();

    fixture.componentInstance.sidebarOpen.set(true);
    fixture.detectChanges();
    expect(fixture.componentInstance.sidebarOpen()).toBe(true);

    fixture.componentInstance.onGlobalKeydown(
      new KeyboardEvent('keydown', { key: 'Escape' }),
    );
    fixture.detectChanges();
    expect(fixture.componentInstance.sidebarOpen()).toBe(false);
  });

  it('closes the narrow overlay after channel change', async () => {
    stubMatchMedia(true);
    const fixture = TestBed.createComponent(ShellPage);
    fixture.detectChanges();
    await fixture.whenStable();

    fixture.componentInstance.sidebarOpen.set(true);
    fixture.detectChanges();

    activeChannel.set({ id: 'ch-2', name: 'engenharia' });
    fixture.detectChanges();
    await fixture.whenStable();

    expect(fixture.componentInstance.sidebarOpen()).toBe(false);
  });
});
