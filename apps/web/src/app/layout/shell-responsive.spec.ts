/** @vitest-environment jsdom */
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiService } from '../core/api/api.service';
import { AuthService } from '../core/auth/auth.service';
import { ChannelStore } from '../core/services/channel.store';
import { ChatHubService } from '../core/services/chat-hub.service';
import { PushNotificationService } from '../core/services/push-notification.service';
import { MessageStore } from '../core/services/message.store';
import { PinStore } from '../core/services/pin.store';
import { SavedStore } from '../core/services/saved.store';
import { ThreadStore } from '../core/services/thread.store';
import { AttachmentQueueService } from '../features/chat/composer/attachment-queue.service';
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
  const workspaceRole = signal('Member');
  const workspaces = signal([{ id: 'ws-1', name: 'Acme', role: 'Member' }]);

  beforeEach(async () => {
    activeChannel.set({ id: 'ch-1', name: 'geral' });
    workspaceRole.set('Member');
    workspaces.set([{ id: 'ws-1', name: 'Acme', role: 'Member' }]);
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
            activeWorkspace: () => workspaces()[0] ? { ...workspaces()[0], role: workspaceRole() } : null,
            workspaces: () => workspaces().map((w) => ({ ...w, role: workspaceRole() })),
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
            publicChannels: () => [],
            channels: () => [],
          },
        },
        {
          provide: MessageStore,
          useValue: {
            loadChannel: vi.fn().mockResolvedValue(undefined),
            markActiveChannelRead: vi.fn().mockResolvedValue(undefined),
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
          provide: PinStore,
          useValue: {
            panelOpen: () => false,
            closePanel: vi.fn(),
            loadForChannel: vi.fn().mockResolvedValue(undefined),
          },
        },
        {
          provide: SavedStore,
          useValue: {
            panelOpen: () => false,
            closePanel: vi.fn(),
            loadForWorkspace: vi.fn().mockResolvedValue(undefined),
          },
        },
        {
          provide: AttachmentQueueService,
          useValue: {
            addFiles: vi.fn().mockResolvedValue(undefined),
          },
        },
        {
          provide: ChatHubService,
          useValue: {
            status: () => 'connected',
            connect: vi.fn().mockResolvedValue(undefined),
            onPresenceChanged: () => () => undefined,
            onMessage: () => () => undefined,
          },
        },
        {
          provide: PushNotificationService,
          useValue: {
            bannerOpen: () => false,
            permissionDenied: () => false,
            devicesOpen: () => false,
            devices: () => [],
            notice: () => null,
            busy: () => false,
            dismissBanner: vi.fn(),
            enablePush: vi.fn(),
            toggleDevices: vi.fn(),
            removeDevice: vi.fn(),
            dismissNotice: vi.fn(),
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
              @if (canAccessAdmin()) {
                <a href="/admin">Admin</a>
              }
              @if (!sidebarOpen()) {
                <button type="button" aria-label="Mostrar barra" (click)="toggleSidebar()"></button>
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
    expect(host.querySelector('[aria-label="Mostrar barra"]')).toBeTruthy();
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

  it('opens the command palette with Ctrl+K (B-099)', async () => {
    stubMatchMedia(false);
    const fixture = TestBed.createComponent(ShellPage);
    fixture.detectChanges();

    fixture.componentInstance.onGlobalKeydown(
      new KeyboardEvent('keydown', { key: 'k', ctrlKey: true, cancelable: true }),
    );
    fixture.detectChanges();
    expect(fixture.componentInstance.palette.paletteOpen()).toBe(true);

    fixture.componentInstance.onGlobalKeydown(
      new KeyboardEvent('keydown', { key: 'Escape', cancelable: true }),
    );
    fixture.detectChanges();
    expect(fixture.componentInstance.palette.paletteOpen()).toBe(false);
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

  it('hides the Admin link from a Member', () => {
    const fixture = TestBed.createComponent(ShellPage);
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('a[href="/admin"]')).toBeNull();
  });

  it('shows the Admin link to a WorkspaceOwner', () => {
    workspaceRole.set('WorkspaceOwner');
    const fixture = TestBed.createComponent(ShellPage);
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('a[href="/admin"]')).toBeTruthy();
  });

  it('hides workspace selector when there is only one workspace', () => {
    stubMatchMedia(false);
    const fixture = TestBed.createComponent(ShellPage);
    fixture.detectChanges();

    expect(fixture.componentInstance.showWorkspaceSelector()).toBe(false);
  });

  it('shows workspace selector with multiple workspaces on expanded desktop', () => {
    stubMatchMedia(false);
    workspaces.set([
      { id: 'ws-1', name: 'Acme', role: 'Member' },
      { id: 'ws-2', name: 'Beta', role: 'Member' },
    ]);
    const fixture = TestBed.createComponent(ShellPage);
    fixture.detectChanges();

    expect(fixture.componentInstance.showWorkspaceSelector()).toBe(true);
  });

  it('hides workspace selector in compact nav even with multiple workspaces', () => {
    stubMatchMedia(false);
    workspaces.set([
      { id: 'ws-1', name: 'Acme', role: 'Member' },
      { id: 'ws-2', name: 'Beta', role: 'Member' },
    ]);
    const fixture = TestBed.createComponent(ShellPage);
    fixture.componentInstance.navCompact.set(true);
    fixture.detectChanges();

    expect(fixture.componentInstance.showWorkspaceSelector()).toBe(false);
  });
});
