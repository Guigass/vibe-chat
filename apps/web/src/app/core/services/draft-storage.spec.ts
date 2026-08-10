import { describe, expect, it } from 'vitest';
import {
  draftRecordKey,
  isDraftEmpty,
  isDraftExpired,
  normalizeDraftInput,
  pruneDraftRecords,
  threadConversationId,
  DRAFT_EXPIRY_MS,
  DRAFT_MAX_PER_USER,
  DraftRecord,
} from './draft-storage';

describe('draft-storage helpers (B-086)', () => {
  it('builds composite and thread conversation keys', () => {
    expect(draftRecordKey('t1', 'u1', 'c1')).toBe('t1|u1|c1');
    expect(threadConversationId('th-9')).toBe('thread:th-9');
  });

  it('treats whitespace-only body without attachments as empty', () => {
    expect(isDraftEmpty({ body: '   ', attachments: [] })).toBe(true);
    expect(isDraftEmpty({ body: '', attachments: [{ attachmentId: 'a', fileName: 'f', sizeBytes: 1, contentType: 'text/plain' }] })).toBe(false);
  });

  it('normalizeDraftInput returns null for empty and keeps selection', () => {
    expect(normalizeDraftInput({ body: '  ', attachments: [] })).toBeNull();
    const draft = normalizeDraftInput({
      body: 'hello',
      selectionStart: 2,
      selectionEnd: 4,
      now: 1000,
    });
    expect(draft).toEqual({
      body: 'hello',
      attachments: [],
      updatedAt: 1000,
      selectionStart: 2,
      selectionEnd: 4,
    });
  });

  it('expires drafts after 30 days', () => {
    const now = 1_000_000;
    expect(isDraftExpired({ body: 'x', attachments: [], updatedAt: now - DRAFT_EXPIRY_MS - 1 }, now)).toBe(true);
    expect(isDraftExpired({ body: 'x', attachments: [], updatedAt: now - 1000 }, now)).toBe(false);
  });

  it('prunes expired and keeps newest within cap', () => {
    const now = Date.now();
    const records: DraftRecord[] = [];
    for (let i = 0; i < DRAFT_MAX_PER_USER + 5; i++) {
      records.push({
        tenantId: 't',
        userId: 'u',
        conversationId: `c${i}`,
        body: `b${i}`,
        attachments: [],
        updatedAt: now - i * 1000,
      });
    }
    records.push({
      tenantId: 't',
      userId: 'u',
      conversationId: 'old',
      body: 'gone',
      attachments: [],
      updatedAt: now - DRAFT_EXPIRY_MS - 10,
    });

    const kept = pruneDraftRecords(records, now);
    expect(kept).toHaveLength(DRAFT_MAX_PER_USER);
    expect(kept.some((r) => r.conversationId === 'old')).toBe(false);
    expect(kept[0].conversationId).toBe('c0');
  });
});
