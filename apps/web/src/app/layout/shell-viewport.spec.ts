import { describe, expect, it, vi } from 'vitest';
import {
  defaultSidebarOpen,
  NAV_COMPACT_STORAGE_KEY,
  readNavCompact,
  SHELL_NARROW_BREAKPOINT_PX,
  SHELL_NARROW_MEDIA_QUERY,
  writeNavCompact,
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

describe('nav compact preference (B-184)', () => {
  it('persists compact mode in localStorage', () => {
    const store = new Map<string, string>();
    vi.stubGlobal('localStorage', {
      getItem: (key: string) => store.get(key) ?? null,
      setItem: (key: string, value: string) => {
        store.set(key, value);
      },
    });

    expect(readNavCompact()).toBe(false);
    writeNavCompact(true);
    expect(store.get(NAV_COMPACT_STORAGE_KEY)).toBe('true');
    expect(readNavCompact()).toBe(true);
    writeNavCompact(false);
    expect(readNavCompact()).toBe(false);
  });
});
