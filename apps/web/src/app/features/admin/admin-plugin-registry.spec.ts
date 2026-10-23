import { ComponentFixture, TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiService } from '../../core/api/api.service';
import { ui } from '../../core/i18n/strings';
import { AdminPluginRegistry } from './admin-plugin-registry';

describe('AdminPluginRegistry (B-137)', () => {
  let fixture: ComponentFixture<AdminPluginRegistry>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AdminPluginRegistry],
      providers: [
        {
          provide: ApiService,
          useValue: {
            listPluginRegistry: vi.fn().mockResolvedValue({
              installs: [
                {
                  id: 'row-1',
                  pluginId: 'registry-note',
                  version: '1.0.0',
                  previousVersion: null,
                  publisher: 'Lab Publisher',
                  source: 'mirror',
                  signatureKeyId: 'lab',
                  signatureFingerprint: 'abc123def456abcd',
                  capabilities: ['messages.send'],
                  risk: 'low',
                  status: 'active',
                },
              ],
            }),
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AdminPluginRegistry);
    fixture.componentRef.setInput('workspaceId', 'ws-1');
    await fixture.componentInstance.ngOnInit();
    fixture.detectChanges();
  });

  it('shows publisher, source, signature, capabilities, version and risk before further action', () => {
    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain(ui.adminPluginRegistryLead);
    expect(host.textContent).toContain('Lab Publisher');
    expect(host.textContent).toContain('mirror');
    expect(host.textContent).toContain('lab');
    expect(host.textContent).toContain('abc123def456abcd');
    expect(host.textContent).toContain('messages.send');
    expect(host.textContent).toContain('1.0.0');
    expect(host.textContent).toContain('low');
    expect(host.textContent).toContain(ui.adminPluginRegistryRollback);
    expect(host.textContent).toContain(ui.adminPluginRegistryRevoke);
    expect(host.textContent).not.toContain('vc_int_');
  });
});