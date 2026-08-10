/** B-086 — pure helpers and types for client-only conversation drafts. */

export const DRAFT_DEBOUNCE_MS = 400;
export const DRAFT_EXPIRY_MS = 30 * 24 * 60 * 60 * 1000;
export const DRAFT_MAX_BODY_CHARS = 16_000;
export const DRAFT_MAX_PER_USER = 100;
export const DRAFT_LS_PREFIX = 'vc.draft.';
export const DRAFT_IDB_NAME = 'vc-drafts';
export const DRAFT_IDB_STORE = 'drafts';
export const DRAFT_IDB_VERSION = 1;

export interface DraftAttachmentMeta {
  attachmentId: string;
  fileName: string;
  sizeBytes: number;
  contentType: string;
}

export interface ConversationDraft {
  body: string;
  attachments: DraftAttachmentMeta[];
  updatedAt: number;
  selectionStart?: number;
  selectionEnd?: number;
}

export interface DraftRecord extends ConversationDraft {
  tenantId: string;
  userId: string;
  conversationId: string;
}

export function threadConversationId(threadId: string): string {
  return `thread:${threadId}`;
}

export function draftRecordKey(tenantId: string, userId: string, conversationId: string): string {
  return `${tenantId}|${userId}|${conversationId}`;
}

export function isDraftEmpty(draft: Pick<ConversationDraft, 'body' | 'attachments'>): boolean {
  return !draft.body.trim() && draft.attachments.length === 0;
}

export function normalizeDraftInput(input: {
  body: string;
  attachments?: DraftAttachmentMeta[];
  selectionStart?: number;
  selectionEnd?: number;
  now?: number;
}): ConversationDraft | null {
  const body = input.body.slice(0, DRAFT_MAX_BODY_CHARS);
  const attachments = (input.attachments ?? []).slice(0, 10);
  if (isDraftEmpty({ body, attachments })) return null;
  return {
    body,
    attachments,
    updatedAt: input.now ?? Date.now(),
    selectionStart: input.selectionStart,
    selectionEnd: input.selectionEnd,
  };
}

export function isDraftExpired(draft: ConversationDraft, now = Date.now()): boolean {
  return now - draft.updatedAt > DRAFT_EXPIRY_MS;
}

/** Keep newest drafts; drop expired and excess. */
export function pruneDraftRecords(records: DraftRecord[], now = Date.now()): DraftRecord[] {
  const fresh = records.filter((r) => !isDraftExpired(r, now));
  fresh.sort((a, b) => b.updatedAt - a.updatedAt);
  return fresh.slice(0, DRAFT_MAX_PER_USER);
}

export function parseLocalStorageDraft(raw: string | null): DraftRecord | null {
  if (!raw) return null;
  try {
    const parsed = JSON.parse(raw) as DraftRecord;
    if (
      typeof parsed?.tenantId !== 'string' ||
      typeof parsed?.userId !== 'string' ||
      typeof parsed?.conversationId !== 'string' ||
      typeof parsed?.body !== 'string' ||
      typeof parsed?.updatedAt !== 'number' ||
      !Array.isArray(parsed.attachments)
    ) {
      return null;
    }
    return parsed;
  } catch {
    return null;
  }
}
