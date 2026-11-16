import { ComponentFixture, TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AiSkillApiService, AiSkillDetail } from '../../core/api/ai-skill-api.service';
import { ui } from '../../core/i18n/strings';
import { AdminAiSkillsPage } from './admin-ai-skills.page';
import { AdminContextService } from './admin-context.service';

const detail: AiSkillDetail = {
  id: 'skill-1',
  slug: 'oracle',
  name: 'Oráculo',
  description: 'Políticas',
  status: 'Published',
  revision: 2,
  activeVersion: 1,
  activeVersionId: 'version-1',
  ownerUserId: 'owner-1',
  draft: {
    slug: 'oracle',
    name: 'Oráculo',
    description: 'Políticas',
    instructions: 'Responda com a fonte.',
    argumentsSchema: { type: 'object' },
    examples: [{ name: 'ping', expected: 'pong' }],
    compatibility: 'bots.v1',
    classification: 'internal',
    knowledgeGrantIds: [],
    mcpToolGrantIds: [],
  },
  versions: [{ id: 'version-1', versionNumber: 1, publishedAt: '2026-11-16T00:00:00Z', instructionsSha256: 'abc' }],
};

describe('Admin AI skills (B-156)', () => {
  let fixture: ComponentFixture<AdminAiSkillsPage>;
  const list = vi.fn();
  const get = vi.fn();
  const compose = vi.fn();

  beforeEach(async () => {
    list.mockReset();
    get.mockReset();
    compose.mockReset();
    list.mockResolvedValue([{
      id: 'skill-1',
      slug: 'oracle',
      name: 'Oráculo',
      description: 'Políticas',
      status: 'Published',
      revision: 2,
      activeVersion: 1,
      activeVersionId: 'version-1',
    }]);
    get.mockResolvedValue(detail);
    compose.mockResolvedValue({
      layers: [
        { order: 1, layer: 'platform', skillVersionId: null, instructionsSha256: null },
        { order: 4, layer: 'skill', skillVersionId: 'version-1', instructionsSha256: 'abc' },
      ],
      warning: 'instruction-does-not-grant-access',
    });

    await TestBed.configureTestingModule({
      imports: [AdminAiSkillsPage],
      providers: [
        {
          provide: AdminContextService,
          useValue: {
            ensureReady: vi.fn().mockResolvedValue(undefined),
            workspace: () => ({ id: 'ws-1' }),
          },
        },
        {
          provide: AiSkillApiService,
          useValue: {
            list,
            get,
            compose,
            create: vi.fn(),
            update: vi.fn(),
            diff: vi.fn(),
            export: vi.fn(),
            publish: vi.fn(),
            deprecate: vi.fn(),
            clone: vi.fn(),
            rollback: vi.fn(),
            import: vi.fn(),
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AdminAiSkillsPage);
    await fixture.componentInstance.ngOnInit();
    fixture.detectChanges();
  });

  it('shows the warning, the version id and the effective order', async () => {
    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('[data-testid="ai-skill-warning"]')?.textContent).toContain(ui.aiSkillWarning);
    expect(host.textContent).toContain('version-1');
    await fixture.componentInstance.open('skill-1');
    fixture.componentInstance.togglePick('version-1', true);
    await fixture.componentInstance.compose();
    fixture.detectChanges();
    const layers = host.querySelector('[data-testid="ai-skill-layers"]')?.textContent ?? '';
    expect(layers.indexOf(ui.aiSkillLayerPlatform)).toBeGreaterThanOrEqual(0);
    expect(layers.indexOf(ui.aiSkillLayerPlatform)).toBeLessThan(layers.indexOf(ui.aiSkillLayerSkill));
    expect(host.querySelector('[data-testid="ai-skill-history"]')?.textContent).toContain(ui.aiSkillVersionId);
  });

  it('explains when bots are switched off', async () => {
    list.mockRejectedValue(Object.assign(new Error('{"error":"AiBotsDisabled"}'), { status: 404 }));
    await fixture.componentInstance.reload();
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="ai-skills-disabled"]')?.textContent).toContain(ui.aiSkillDisabled);
  });

  it('shows the empty state', async () => {
    list.mockResolvedValue([]);
    await fixture.componentInstance.reload();
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="ai-skills-empty"]')?.textContent).toContain(ui.aiSkillEmpty);
  });
});
