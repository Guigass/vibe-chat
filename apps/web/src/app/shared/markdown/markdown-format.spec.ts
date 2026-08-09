import { describe, expect, it } from 'vitest';
import { applyMarkdownWrap, handleMarkdownShortcut } from './markdown-format';

describe('markdown-format', () => {
  it('wraps selection with bold markers', () => {
    const result = applyMarkdownWrap('hello world', 0, 5, 'bold');
    expect(result.value).toBe('**hello** world');
    expect(result.selectionStart).toBe(2);
    expect(result.selectionEnd).toBe(7);
  });

  it('wraps empty selection for code', () => {
    const result = applyMarkdownWrap('text', 2, 2, 'code');
    expect(result.value).toBe('te``xt');
  });

  it('detects Ctrl+B shortcut', () => {
    const event = { ctrlKey: true, metaKey: false, key: 'b', shiftKey: false } as KeyboardEvent;
    expect(handleMarkdownShortcut(event)).toBe('bold');
  });

  it('detects Shift+X strikethrough shortcut', () => {
    const event = { ctrlKey: false, metaKey: false, key: 'x', shiftKey: true } as KeyboardEvent;
    expect(handleMarkdownShortcut(event)).toBe('strike');
  });
});
