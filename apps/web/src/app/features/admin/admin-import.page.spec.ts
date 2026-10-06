import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiService } from '../../core/api/api.service';
import { AdminContextService } from './admin-context.service';
import { AdminImportPage } from './admin-import.page';

describe('AdminImportPage', () => {
  const createImport = vi.fn();
  const planImport = vi.fn();

  beforeEach(() => {
    sessionStorage.clear();
    createImport.mockReset();
    planImport.mockReset();
    TestBed.configureTestingModule({
      imports: [AdminImportPage],
      providers: [
        {
          provide: AdminContextService,
          useValue: {
            ensureReady: vi.fn(async () => undefined),
            workspace: signal({ id: 'ws-1', name: 'Demo' }),
          },
        },
        {
          provide: ApiService,
          useValue: {
            getImport: vi.fn(),
            createImport,
            planImport,
            executeImport: vi.fn(),
            pauseImport: vi.fn(),
            resumeImport: vi.fn(),
            publishImport: vi.fn(),
            rollbackImport: vi.fn(),
          },
        },
      ],
    });
  });

  it('validates a file and keeps the job id for a later visit', async () => {
    createImport.mockResolvedValue({
      id: 'job-1',
      status: 'validated',
      adapter: 'vibechat',
      createdAt: '',
      updatedAt: '',
      idempotent: false,
      report: {
        principals: 1,
        spaces: 1,
        channels: 1,
        threads: 0,
        messages: 1,
        attachments: 0,
        quarantined: 0,
        ignored: 0,
        redacted: 0,
        failed: 0,
        blocking: false,
        warnings: [],
        conflicts: [],
      },
    });
    const fixture = TestBed.createComponent(AdminImportPage);
    await fixture.componentInstance.ngOnInit();
    const input = document.createElement('input');
    const file = new File([JSON.stringify({ format: 'vibechat.import.v1' })], 'export.json', { type: 'application/json' });
    Object.defineProperty(input, 'files', { value: [file] });
    await fixture.componentInstance.onFile({ target: input } as unknown as Event);
    await fixture.componentInstance.validate();
    expect(createImport).toHaveBeenCalledOnce();
    expect(sessionStorage.getItem('vibechat.import.job')).toBe('job-1');
    expect(fixture.componentInstance.counts(fixture.componentInstance.job()!)).toContain('1');
  });
});
