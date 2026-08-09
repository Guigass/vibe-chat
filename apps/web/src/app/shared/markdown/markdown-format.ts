export type MarkdownWrapKind = 'bold' | 'italic' | 'strike' | 'code';

const WRAP: Record<MarkdownWrapKind, { before: string; after: string }> = {
  bold: { before: '**', after: '**' },
  italic: { before: '*', after: '*' },
  strike: { before: '~~', after: '~~' },
  code: { before: '`', after: '`' },
};

export function applyMarkdownWrap(
  value: string,
  selectionStart: number,
  selectionEnd: number,
  kind: MarkdownWrapKind,
): { value: string; selectionStart: number; selectionEnd: number } {
  const { before, after } = WRAP[kind];
  const selected = value.slice(selectionStart, selectionEnd);
  const wrapped = `${before}${selected}${after}`;
  const nextValue = value.slice(0, selectionStart) + wrapped + value.slice(selectionEnd);
  const nextStart = selectionStart + before.length;
  const nextEnd = nextStart + selected.length;
  return {
    value: nextValue,
    selectionStart: selected.length === 0 ? nextEnd : nextStart,
    selectionEnd: selected.length === 0 ? nextEnd : nextEnd,
  };
}

export function handleMarkdownShortcut(event: KeyboardEvent): MarkdownWrapKind | null {
  const mod = event.ctrlKey || event.metaKey;
  if (mod && event.key.toLowerCase() === 'b') {
    return 'bold';
  }
  if (mod && event.key.toLowerCase() === 'i') {
    return 'italic';
  }
  if (event.shiftKey && event.key.toLowerCase() === 'e') {
    return 'code';
  }
  if (event.shiftKey && event.key.toLowerCase() === 'x') {
    return 'strike';
  }
  return null;
}

export function updateTextareaSelection(
  textarea: HTMLTextAreaElement,
  value: string,
  selectionStart: number,
  selectionEnd: number,
): void {
  textarea.value = value;
  textarea.selectionStart = selectionStart;
  textarea.selectionEnd = selectionEnd;
  textarea.dispatchEvent(new Event('input', { bubbles: true }));
}
