import { describe, expect, it } from 'vitest';
import { displaySpaceName } from './display';
import { ui } from './strings';

describe('displaySpaceName', () => {
  it('localizes the seeded Geral / Engenharia spaces', () => {
    expect(displaySpaceName('Geral')).toBe(ui.navSpaceGeral);
    expect(displaySpaceName('geral')).toBe(ui.navSpaceGeral);
    expect(displaySpaceName('Engenharia')).toBe(ui.navSpaceEngineering);
  });

  it('keeps custom space names and falls back when empty', () => {
    expect(displaySpaceName('Produto')).toBe('Produto');
    expect(displaySpaceName(null)).toBe(ui.navOthers);
    expect(displaySpaceName('')).toBe(ui.navOthers);
  });
});
