import { WritableSignal } from '@angular/core';
import { rememberRecentEmoji } from '../../../shared/emoji/emoji-data';
import { applyMarkdownWrap, updateTextareaSelection } from '../../../shared/markdown/markdown-format';

export type ComposerFormatKind = 'bold' | 'italic' | 'strike' | 'code';

export function applyComposerFormat(
  draft: WritableSignal<string>,
  textarea: HTMLTextAreaElement | null,
  kind: ComposerFormatKind,
): void {
  if (!textarea) return;

  const current = draft();
  const result = applyMarkdownWrap(current, textarea.selectionStart, textarea.selectionEnd, kind);
  draft.set(result.value);
  updateTextareaSelection(textarea, result.value, result.selectionStart, result.selectionEnd);
}

export function insertComposerEmoji(
  draft: WritableSignal<string>,
  textarea: HTMLTextAreaElement | null,
  emoji: string,
): void {
  rememberRecentEmoji(emoji);
  const current = draft();
  const start = textarea?.selectionStart ?? current.length;
  const end = textarea?.selectionEnd ?? start;
  const next = `${current.slice(0, start)}${emoji}${current.slice(end)}`;
  draft.set(next);

  if (textarea) {
    const cursor = start + emoji.length;
    updateTextareaSelection(textarea, next, cursor, cursor);
  }
}
