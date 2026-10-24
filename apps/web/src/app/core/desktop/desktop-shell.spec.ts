import { describe, expect, it } from 'vitest';
import { rejectWebTokenWrite, resolveDesktopShell } from './desktop-shell';

describe('desktop shell', () => {
  it('falls back to the browser when the native bridge is absent', () => {
    expect(resolveDesktopShell(null)).toEqual({ mode: 'browser', browserFallback: true });
  });

  it('keeps the browser fallback when a native bridge is present', () => {
    const bridge = {
      secureStore: async () => undefined,
      wipe: async () => undefined,
    };
    expect(resolveDesktopShell(bridge).mode).toBe('desktop');
    expect(resolveDesktopShell(bridge).browserFallback).toBe(true);
  });

  it('refuses to write a token into web storage', () => {
    expect(() => rejectWebTokenWrite('access_token')).toThrow(/DesktopTokenRejected/);
    expect(() => rejectWebTokenWrite('refresh_token')).toThrow(/DesktopTokenRejected/);
    expect(() => rejectWebTokenWrite('theme')).not.toThrow();
  });
});
