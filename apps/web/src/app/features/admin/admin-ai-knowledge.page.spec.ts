import { ComponentFixture, TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { BotKnowledgeApiService, BotKnowledgeDetail } from '../../core/api/bot-knowledge-api.service';
import { ui } from '../../core/i18n/strings';
import { AdminAiKnowledgePage } from './admin-ai-knowledge.page';
import { AdminContextService } from './admin-context.service';

const detail: BotKnowledgeDetail = {
  id: 'source-1',
  name: 'Férias',
  kind: 'file',
  classification: 'internal',
  status: 'Ready',
  version: 1,
  chunkCount: 1,
  checksum: 'abc',
  lastError: null,
  updatedAt: '2026-11-23T00:00:00Z',
  contentSha256: 'def',
  locator: 'inline',
  pageId: null,
  fileId: null,
  deletedAt: null,
  indexLagSeconds: 0,
  purgeEvidence: null,
  jobs: [],
  grants: [],
};

describe('Admin bot knowledge (B-157)', () => {
  let fixture: ComponentFixture<AdminAiKnowledgePage>;
  const list = vi.fn();
  const get = vi.fn();
  const ask = vi.fn();

  beforeEach(async () => {
    list.mockReset();
    get.mockReset();
    ask.mockReset();
    list.mockResolvedValue([{
      id: 'source-1',
      name: 'Férias',
      kind: 'file',
      classification: 'internal',
      status: 'Ready',
      version: 1,
      chunkCount: 1,
      checksum: 'abc',
      lastError: null,
      updatedAt: '2026-11-23T00:00:00Z',
    }]);
    get.mockResolvedValue(detail);
    ask.mockResolvedValue({ evidence: 'insufficient', citations: [] });

    await TestBed.configureTestingModule({
      imports: [AdminAiKnowledgePage],
      providers: [
        {
          provide: AdminContextService,
          useValue: {
            ensureReady: vi.fn().mockResolvedValue(undefined),
            workspace: () => ({ id: 'ws-1' }),
          },
        },
        {
          provide: BotKnowledgeApiService,
          useValue: {
            list,
            get,
            ask,
            create: vi.fn(),
            pause: vi.fn(),
            resume: vi.fn(),
            reindex: vi.fn(),
            remove: vi.fn(),
            grant: vi.fn(),
            revoke: vi.fn(),
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AdminAiKnowledgePage);
    await fixture.componentInstance.ngOnInit();
    fixture.detectChanges();
  });

  it('lists a ready source and explains missing evidence', async () => {
    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('Férias');
    expect(host.textContent).toContain(ui.aiKnowledgeReady);
    await fixture.componentInstance.open('source-1');
    fixture.componentInstance.botId.set('bot-1');
    fixture.componentInstance.question.set('ferias');
    await fixture.componentInstance.ask();
    fixture.detectChanges();
    expect(host.querySelector('[data-testid="ai-knowledge-answer"]')?.textContent).toContain(ui.aiKnowledgeNoEvidence);
  });

  it('explains when knowledge is switched off', async () => {
    list.mockRejectedValue(Object.assign(new Error('{"error":"BotKnowledgeDisabled"}'), { status: 404 }));
    await fixture.componentInstance.reload();
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="ai-knowledge-disabled"]')?.textContent)
      .toContain(ui.aiKnowledgeDisabled);
  });

  it('shows the empty state', async () => {
    list.mockResolvedValue([]);
    await fixture.componentInstance.reload();
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="ai-knowledge-empty"]')?.textContent)
      .toContain(ui.aiKnowledgeEmpty);
  });
});
