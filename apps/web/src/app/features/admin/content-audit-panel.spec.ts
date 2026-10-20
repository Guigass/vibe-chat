import { ComponentFixture, TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiService } from '../../core/api/api.service';
import { ui } from '../../core/i18n/strings';
import { SensitiveSettings } from '../../shared/models/chat.models';
import { AdminContextService } from './admin-context.service';
import { ContentAuditPanel } from './content-audit-panel';

const settings = {
  workspaceId: 'ws-1',
  contentAudit: { enabled: true },
} as SensitiveSettings;

describe('ContentAuditPanel', () => {
  let fixture: ComponentFixture<ContentAuditPanel>;
  let api: {
    getAdminSensitiveSettings: ReturnType<typeof vi.fn>;
    updateAdminSensitiveSettings: ReturnType<typeof vi.fn>;
  };

  beforeEach(async () => {
    api = {
      getAdminSensitiveSettings: vi.fn().mockResolvedValue(settings),
      updateAdminSensitiveSettings: vi.fn().mockResolvedValue({
        ...settings,
        contentAudit: { enabled: false },
      }),
    };

    await TestBed.configureTestingModule({
      imports: [ContentAuditPanel],
      providers: [
        { provide: ApiService, useValue: api },
        {
          provide: AdminContextService,
          useValue: {
            ensureReady: vi.fn().mockResolvedValue(undefined),
            workspace: () => ({ id: 'ws-1', name: 'Acme', slug: 'acme', role: 'Admin' }),
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ContentAuditPanel);
    await fixture.componentInstance.ngOnInit();
    fixture.detectChanges();
  });

  it('asks for confirmation before turning audit off', async () => {
    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain(ui.adminContentAuditTitle);
    const confidential = host.querySelectorAll<HTMLInputElement>('input[type="radio"]')[1];
    confidential.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const form = host.querySelector('form') as HTMLFormElement;
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await fixture.whenStable();

    expect(api.updateAdminSensitiveSettings).not.toHaveBeenCalled();
    expect(host.textContent).toContain(ui.errorContentAuditConfirmationRequired);
  });

  it('sends the disable confirmation to the API', async () => {
    const host = fixture.nativeElement as HTMLElement;
    const confidential = host.querySelectorAll<HTMLInputElement>('input[type="radio"]')[1];
    confidential.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    const confirm = host.querySelector<HTMLInputElement>('input[type="checkbox"]');
    expect(confirm).toBeTruthy();
    confirm!.checked = true;
    confirm!.dispatchEvent(new Event('change'));

    const form = host.querySelector('form') as HTMLFormElement;
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await fixture.whenStable();

    expect(api.updateAdminSensitiveSettings).toHaveBeenCalledWith({
      workspaceId: 'ws-1',
      contentAudit: { enabled: false, confirmDisable: true },
    });
  });
});
