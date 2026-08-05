import { describe, expect, it } from 'vitest';
import {
  defaultSidebarOpen,
  SHELL_NARROW_BREAKPOINT_PX,
  SHELL_NARROW_MEDIA_QUERY,
} from './shell-viewport';

describe('shell viewport (UX-003)', () => {
  it('uses the 960px overlay breakpoint', () => {
    expect(SHELL_NARROW_BREAKPOINT_PX).toBe(960);
    expect(SHELL_NARROW_MEDIA_QUERY).toBe('(max-width: 960px)');
  });

  it('starts collapsed on narrow and open on desktop', () => {
    expect(defaultSidebarOpen(true)).toBe(false);
    expect(defaultSidebarOpen(false)).toBe(true);
  });
});
