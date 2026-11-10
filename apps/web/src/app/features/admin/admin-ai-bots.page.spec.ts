import { ComponentFixture, TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AiBotApiService, AiBotDetail } from '../../core/api/ai-bot-api.service';
import { DirectoryApiService } from '../../core/api/directory-api.service';
import { ui } from '../../core/i18n/strings';
import { AdminAiBotsPage } from './admin-ai-bots.page';
import { AdminContextService } from './admin-context.service';

const detail: AiBotDetail = {
  id: 'bot-1',
  name: 'Oráculo',
  description: 'Políticas',
  status: 'Published',
  revision: 2,
  activeVersion: 1,
  principalUserId: 'principal-1',
  draft: {
    name: 'Oráculo novo',
    description: 'Políticas',
    avatarKey: '',
    allowDms: true,
    systemPrompt: 'Responda com a fonte.',
    provider: 'mock',
    modelId: 'mock-small',
    parameters: { temperature: 0.2 },
    maxTokens: 1024,
    timeoutMs: 30000,
    fallbackModelId: null,
    dataClass: 'internal',
    channelIds: [],
    skillVersionIds: [],
    knowledgeGrantIds: [],
    mcpToolGrantIds: [],
    guardrailPolicyVersion: null,
    maxTurns: 4,
    maxInputTokens: 4096,
    maxOutputTokens: 1024,
    dailyBudgetUsd: 1,
  },
  versions: [{ versionNumber: 1, publishedAt: '2026-11-10T00:00:00Z', provider: 'mock', modelId: 'mock-small', name: 'Oráculo' }],
};

describe('Admin AI bots (B-155)', () => {
  let fixture: ComponentFixture<AdminAiBotsPage>;
  const list = vi.fn();
  const get = vi.fn();
  const diff = vi.fn();

  beforeEach(async () => {
    list.mockReset();
    get.mockReset();
    diff.mockReset();
    list.mockResolvedValue([{ id: 'bot-1', name: 'Oráculo', description: 'Políticas', status: 'Published', revision: 2, activeVersion: 1, principalUserId: 'principal-1' }]);
    get.mockResolvedValue(detail);
    diff.mockResolvedValue({
      fromVersion: 1,
      toVersion: 0,
      changes: [{ field: 'systemPrompt', before: 'Antigo', after: 'Responda com a fonte.' }],
    });

    await TestBed.configureTestingModule({
      imports: [AdminAiBotsPage],
      providers: [
        {
          provide: AdminContextService,
          useValue: {
            ensureReady: vi.fn().mockResolvedValue(undefined),
            workspace: () => ({ id: 'ws-1' }),
          },
        },
        {
          provide: DirectoryApiService,
          useValue: { getChannels: vi.fn().mockResolvedValue([]) },
        },
        {
          provide: AiBotApiService,
          useValue: { list, get, diff, create: vi.fn(), update: vi.fn(), publish: vi.fn(), disable: vi.fn(), rollback: vi.fn() },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AdminAiBotsPage);
    await fixture.componentInstance.ngOnInit();
    fixture.detectChanges();
  });

  it('walks the builder to review and shows the diff', async () => {
    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('Oráculo');
    await fixture.componentInstance.open('bot-1');
    fixture.componentInstance.go('identity');
    fixture.detectChanges();
    expect(host.querySelector('[data-testid="ai-bot-step-identity"]')).toBeTruthy();
    fixture.componentInstance.go('personality');
    fixture.detectChanges();
    expect(host.querySelector('[data-testid="ai-bot-step-personality"]')).toBeTruthy();
    fixture.componentInstance.go('review');
    fixture.detectChanges();
    await fixture.componentInstance.preview();
    fixture.detectChanges();
    const diff = host.querySelector('[data-testid="ai-bot-diff"]')?.textContent ?? '';
    expect(diff).toContain('systemPrompt');
    expect(diff).toContain('Responda com a fonte.');
    expect(host.querySelector('[data-testid="ai-bot-history"]')?.textContent).toContain(ui.aiBotVersion);
  });

  it('explains when the catalog is switched off', async () => {
    list.mockRejectedValue(Object.assign(new Error('{"error":"AiBotsDisabled"}'), { status: 404 }));
    await fixture.componentInstance.reload();
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain(ui.aiBotDisabled);
  });
});
