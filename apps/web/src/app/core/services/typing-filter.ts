import { TypingState } from '../../shared/models/chat.models';

/** Drops the local user's typing entry (B-071 / W6-2 defense in depth). */
export function withoutSelfTyping(
  entries: readonly TypingState[],
  selfUserId: string | null | undefined,
): TypingState[] {
  if (!selfUserId) return [...entries];
  return entries.filter((t) => t.userId !== selfUserId);
}
