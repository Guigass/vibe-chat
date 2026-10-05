import { handleMarkdownShortcut } from '../../../shared/markdown/markdown-format';
import { ComposerFormatKind } from './composer-format';

export interface ComposerMenuKeyState {
  slashOpen: boolean;
  slashCount: number;
  slashIndex: number;
  mentionOpen: boolean;
  mentionQueryActive: boolean;
  mentionCount: number;
  mentionIndex: number;
  editing: boolean;
}

export type ComposerKeyAction =
  | { type: 'consumed' }
  | { type: 'slash-index'; index: number }
  | { type: 'slash-close' }
  | { type: 'slash-apply' }
  | { type: 'mention-index'; index: number }
  | { type: 'mention-close' }
  | { type: 'mention-apply' }
  | { type: 'cancel-edit' }
  | { type: 'format'; kind: ComposerFormatKind }
  | { type: 'submit' }
  | { type: 'sync' };

/** Slash, mention and Esc-to-cancel while editing. Null lets ArrowUp / submit run. */
export function decideComposerMenuKey(
  event: KeyboardEvent,
  state: ComposerMenuKeyState,
): ComposerKeyAction | null {
  if (state.slashOpen) {
    if (event.key === 'ArrowDown') {
      event.preventDefault();
      const max = state.slashCount;
      if (!max) return { type: 'consumed' };
      return { type: 'slash-index', index: (state.slashIndex + 1) % max };
    }
    if (event.key === 'ArrowUp') {
      event.preventDefault();
      const max = state.slashCount;
      if (!max) return { type: 'consumed' };
      return { type: 'slash-index', index: (state.slashIndex - 1 + max) % max };
    }
    if (event.key === 'Escape') {
      event.preventDefault();
      return { type: 'slash-close' };
    }
    if ((event.key === 'Enter' || event.key === 'Tab') && state.slashCount) {
      event.preventDefault();
      return { type: 'slash-apply' };
    }
  }

  if (state.mentionOpen || state.mentionQueryActive) {
    if (event.key === 'ArrowDown') {
      event.preventDefault();
      const max = state.mentionCount;
      if (!max) return { type: 'consumed' };
      return { type: 'mention-index', index: (state.mentionIndex + 1) % max };
    }
    if (event.key === 'ArrowUp') {
      event.preventDefault();
      const max = state.mentionCount;
      if (!max) return { type: 'consumed' };
      return { type: 'mention-index', index: (state.mentionIndex - 1 + max) % max };
    }
    if (event.key === 'Escape') {
      event.preventDefault();
      return { type: 'mention-close' };
    }
    if (event.key === 'Enter' || event.key === 'Tab') {
      event.preventDefault();
      event.stopPropagation();
      return { type: 'mention-apply' };
    }
  }

  if (event.key === 'Escape' && state.editing) {
    event.preventDefault();
    return { type: 'cancel-edit' };
  }

  return null;
}

/** Markdown shortcuts, Enter to send, otherwise sync menus and typing. */
export function decideComposerSubmitKey(event: KeyboardEvent): ComposerKeyAction {
  const shortcut = handleMarkdownShortcut(event);
  if (shortcut) {
    event.preventDefault();
    return { type: 'format', kind: shortcut };
  }

  if (event.key === 'Enter' && !event.shiftKey) {
    event.preventDefault();
    return { type: 'submit' };
  }

  return { type: 'sync' };
}
