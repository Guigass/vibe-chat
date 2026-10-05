/** @vitest-environment jsdom */
import { describe, expect, it } from 'vitest';
import { decideComposerMenuKey, decideComposerSubmitKey } from './composer-keyboard';

const idle = {
  slashOpen: false,
  slashCount: 0,
  slashIndex: 0,
  mentionOpen: false,
  mentionQueryActive: false,
  mentionCount: 0,
  mentionIndex: 0,
  editing: false,
};

describe('composer keyboard', () => {
  it('inserts the active mention on Enter and does not fall through to send', () => {
    const event = new KeyboardEvent('keydown', { key: 'Enter', cancelable: true });
    const action = decideComposerMenuKey(event, {
      ...idle,
      mentionOpen: true,
      mentionQueryActive: true,
      mentionCount: 2,
    });
    expect(action).toEqual({ type: 'mention-apply' });
    expect(event.defaultPrevented).toBe(true);
  });

  it('cancels edit on Escape and sends on Enter when menus are closed', () => {
    const escape = new KeyboardEvent('keydown', { key: 'Escape', cancelable: true });
    expect(decideComposerMenuKey(escape, { ...idle, editing: true })).toEqual({ type: 'cancel-edit' });

    const enter = new KeyboardEvent('keydown', { key: 'Enter', cancelable: true });
    expect(decideComposerMenuKey(enter, idle)).toBeNull();
    expect(decideComposerSubmitKey(enter)).toEqual({ type: 'submit' });
    expect(enter.defaultPrevented).toBe(true);
  });
});
