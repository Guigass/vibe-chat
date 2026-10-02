import { ComponentFixture, TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiService } from '../../core/api/api.service';
import { ui } from '../../core/i18n/strings';
import { TemplatePlan, WorkspaceOnboardingState, WorkspaceTemplateCatalog } from '../../shared/models/chat.models';
import { AdminContextService } from './admin-context.service';
import { AdminOnboardingPage } from './admin-onboarding.page';

const catalog: WorkspaceTemplateCatalog = {
  builtins: [
    { id: 'team', version: 1, spaceCount: 1, channelCount: 2, checklist: ['invite-members'], builtin: true },
    { id: 'project', version: 1, spaceCount: 1, channelCount: 3, checklist: ['invite-members'], builtin: true },
    { id: 'community', version: 1, spaceCount: 1, channelCount: 3, checklist: ['invite-members'], builtin: true },
    { id: 'incidents', version: 1, spaceCount: 1, channelCount: 3, checklist: ['invite-members'], builtin: true },
  ],
  custom: [],
};

const pending: WorkspaceOnboardingState = {
  status: 'pending',
  items: [
    { key: 'invite-members', state: 'open' },
    { key: 'review-channels', state: 'open' },
    { key: 'confirm-policy', state: 'open' },
  ],
};

const previewPlan: TemplatePlan = {
  templateId: 'team',
  version: 1,
  hasConflicts: false,
  dryRun: true,
  items: [
    { kind: 'space', action: 'create', key: 'time', name: 'Time' },
    { kind: 'channel', action: 'create', key: 'geral', name: 'geral' },
  ],
};

const conflictPlan: TemplatePlan = {
  ...previewPlan,
  hasConflicts: true,
  items: [
    { kind: 'channel', action: 'conflict', key: 'geral', name: 'geral', detail: 'type' },
  ],
};

describe('AdminOnboardingPage (B-115)', () => {
  let fixture: ComponentFixture<AdminOnboardingPage>;
  const preview = vi.fn();
  const apply = vi.fn();
  const update = vi.fn();

  async function setup(onboarding: WorkspaceOnboardingState = pending): Promise<void> {
    preview.mockReset();
    apply.mockReset();
    update.mockReset();
    preview.mockResolvedValue(previewPlan);
    apply.mockResolvedValue({ ...previewPlan, dryRun: false });
    update.mockImplementation(async (_id: string, body: { status?: string; items?: { key: string; state: string }[] }) => ({
      ...onboarding,
      status: body.status ?? onboarding.status,
      items: body.items ?? onboarding.items,
    }));

    await TestBed.configureTestingModule({
      imports: [AdminOnboardingPage],
      providers: [
        {
          provide: ApiService,
          useValue: {
            listWorkspaceTemplates: vi.fn().mockResolvedValue(catalog),
            getOnboarding: vi.fn().mockResolvedValue(onboarding),
            previewWorkspaceTemplate: preview,
            applyWorkspaceTemplate: apply,
            updateOnboarding: update,
            exportWorkspaceTemplate: vi.fn(),
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

    fixture = TestBed.createComponent(AdminOnboardingPage);
    await fixture.componentInstance.ngOnInit();
    fixture.detectChanges();
  }

  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  it('previews a template without applying, then shows the summary', async () => {
    await setup();
    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain(ui.adminTemplateTeam);
    expect(host.textContent).toContain(ui.adminOnboardingSkip);

    (host.querySelector('#choose-template') as HTMLFormElement).requestSubmit();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(preview).toHaveBeenCalledWith('ws-1', { templateId: 'team' });
    expect(apply).not.toHaveBeenCalled();
    expect(host.textContent).toContain(ui.adminOnboardingPreviewHint);
    expect(host.textContent).toContain('Time');

    (host.querySelector('form') as HTMLFormElement).requestSubmit();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(apply).toHaveBeenCalled();
    expect(host.textContent).toContain(ui.adminOnboardingReady);
    expect(host.textContent).toContain(ui.adminChecklistInviteMembers);
  });

  it('shows conflicts and does not apply', async () => {
    await setup();
    preview.mockResolvedValue(conflictPlan);
    const host = fixture.nativeElement as HTMLElement;
    (host.querySelector('#choose-template') as HTMLFormElement).requestSubmit();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(host.textContent).toContain(ui.adminOnboardingConflictBody);
    expect(host.textContent).toContain(ui.adminOnboardingConflictType);
    expect(host.querySelector('button[type="submit"]')).toBeNull();
    expect(apply).not.toHaveBeenCalled();
  });

  it('skips and resumes without requiring a template', async () => {
    await setup();
    const host = fixture.nativeElement as HTMLElement;
    const skip = [...host.querySelectorAll('button')].find((button) => button.textContent?.includes(ui.adminOnboardingSkip));
    expect(skip).toBeTruthy();
    await fixture.componentInstance.skip();
    fixture.detectChanges();

    expect(update).toHaveBeenCalledWith('ws-1', { status: 'skipped' });
    expect(host.textContent).toContain(ui.adminOnboardingSkipped);
    expect(host.querySelector('#choose-template')).toBeNull();

    const resume = [...host.querySelectorAll('button')].find((button) => button.textContent?.includes(ui.adminOnboardingResume));
    expect(resume).toBeTruthy();
    await fixture.componentInstance.resume();
    fixture.detectChanges();

    expect(host.querySelector('#choose-template')).toBeTruthy();
  });
});
