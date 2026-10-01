import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiService } from '../../core/api/api.service';
import { ui } from '../../core/i18n/strings';
import { InstalledPlugin } from '../../shared/models/chat.models';
import { AdminContextService } from './admin-context.service';
import { AdminPluginsPage } from './admin-plugins.page';

const plugin: InstalledPlugin = {
  id: 'p1',
  pluginId: 'incoming-messages',
  name: 'Incoming Messages API',
  version: '1.0.0',
  capabilities: ['messages.send'],
  enabled: true,
  botId: 'b1',
  allowDms: false,
  channelIds: [],
  tokenConfigured: true,
  tokenLast4: 'ab12',
  installedAt: '2026-10-01T00:00:00Z',
  updatedAt: '2026-10-01T00:00:00Z',
};

describe('AdminPluginsPage (B-110)', () => {
  let fixture: ComponentFixture<AdminPluginsPage>;

  async function setup(plugins: InstalledPlugin[]): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [AdminPluginsPage],
      providers: [
        provideRouter([]),
        {
          provide: ApiService,
          useValue: {
            listInstalledPlugins: vi.fn().mockResolvedValue(plugins),
            getChannels: vi.fn().mockResolvedValue([]),
          },
        },
        {
          provide: AdminContextService,
          useValue: {
            ensureReady: vi.fn().mockResolvedValue(undefined),
            workspace: () => ({ id: 'ws-1', name: 'Acme', slug: 'acme', role: 'Admin' }),
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AdminPluginsPage);
    await fixture.componentInstance.ngOnInit();
    fixture.detectChanges();
  }

  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  it('shows the empty state when nothing is installed', async () => {
    await setup([]);
    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain(ui.adminPluginsEmpty);
    expect(host.textContent).toContain(ui.adminPluginInstallBuiltin);
    expect(host.textContent).not.toContain('vc_int_');
  });

  it('lists plugins and filters them down to the no-match state', async () => {
    await setup([plugin]);
    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('Incoming Messages API');
    expect(host.textContent).toContain('1.0.0');
    expect(host.textContent).toContain('messages.send');
    expect(host.textContent).toContain('ab12');

    fixture.componentInstance.filterQuery.set('loja');
    fixture.detectChanges();
    expect(host.textContent).toContain(ui.adminPluginNoMatch);
    expect(host.textContent).not.toContain('Incoming Messages API');
  });
});
