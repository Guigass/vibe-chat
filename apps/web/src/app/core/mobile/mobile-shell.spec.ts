import { describe, expect, it, vi } from 'vitest';
import { acceptShareTarget, applyRemoteLogout, pushEnvelope, rejectWebTokenWrite, resolveMobileShell } from './mobile-shell';

describe('mobile shell', () => {
  it('falls back to the browser when the native bridge is absent', () => {
    expect(resolveMobileShell(null)).toEqual({ mode: 'browser', browserFallback: true });
  });

  it('keeps the browser fallback when a native bridge is present', () => {
    const bridge = {
      secureStore: async () => undefined,
      wipe: async () => undefined,
    };
    expect(resolveMobileShell(bridge).mode).toBe('mobile');
    expect(resolveMobileShell(bridge).browserFallback).toBe(true);
  });

  it('refuses to write a token into web storage', () => {
    expect(() => rejectWebTokenWrite('access_token')).toThrow(/MobileTokenRejected/);
    expect(() => rejectWebTokenWrite('fcmToken')).toThrow(/MobileTokenRejected/);
    expect(() => rejectWebTokenWrite('theme')).not.toThrow();
  });

  it('accepts a share only after confirmation and without content', () => {
    expect(() => acceptShareTarget({ confirm: true })).not.toThrow();
    expect(() => acceptShareTarget({ confirm: false })).toThrow(/MobileShareRejected/);
    expect(() => acceptShareTarget({ confirm: true, content: 'secret' })).toThrow(/MobileShareRejected/);
  });

  it('builds a push envelope without message content', () => {
    expect(pushEnvelope('mention', '22222222-2222-2222-2222-222222222222')).toEqual({
      kind: 'mention',
      conversationId: '22222222-2222-2222-2222-222222222222',
      containsContent: false,
    });
    expect(() => pushEnvelope('body', '22222222-2222-2222-2222-222222222222')).toThrow(/MobilePushRejected/);
  });

  it('wipes secure storage on remote logout', async () => {
    const wipe = vi.fn(async () => undefined);
    const bridge = { secureStore: async () => undefined, wipe };
    await expect(applyRemoteLogout({ wipeSecureStorage: true }, bridge)).resolves.toBe('wiped');
    expect(wipe).toHaveBeenCalledOnce();
    await expect(applyRemoteLogout({ wipeSecureStorage: false }, bridge)).resolves.toBe('browser');
    await expect(applyRemoteLogout({ wipeSecureStorage: true }, null)).resolves.toBe('browser');
  });
});
