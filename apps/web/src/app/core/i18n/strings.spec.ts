import { describe, expect, it } from 'vitest';
import { translateErrorCode, ui } from './strings';

describe('translateErrorCode', () => {
  it('maps known API codes and leaves unknown codes intact', () => {
    expect(translateErrorCode('InvalidLocale')).toBe(ui.errorInvalidLocale);
    expect(translateErrorCode('MentionAllForbidden')).toBe(ui.errorMentionAllForbidden);
    expect(translateErrorCode('WrongChannel')).toBe(ui.errorWrongChannel);
    expect(translateErrorCode('PollClosed')).toBe('PollClosed');
    expect(translateErrorCode(undefined)).toBe('');
  });
});
