export interface MobileNativeBridge {
  secureStore(key: string, value: string): Promise<void>;
  wipe(): Promise<void>;
}

export interface MobileShellMode {
  mode: 'mobile' | 'browser';
  browserFallback: boolean;
}

const tokenKeys = ['access_token', 'refresh_token', 'id_token', 'authorization', 'fcm', 'apns'];

export function resolveMobileShell(bridge: MobileNativeBridge | null | undefined): MobileShellMode {
  if (!bridge) {
    return { mode: 'browser', browserFallback: true };
  }

  return { mode: 'mobile', browserFallback: true };
}

export function rejectWebTokenWrite(key: string): void {
  const normalized = key.trim().toLowerCase();
  if (tokenKeys.some((name) => normalized.includes(name))) {
    throw new Error('MobileTokenRejected');
  }
}

export function acceptShareTarget(input: { confirm?: boolean; content?: string }): void {
  if (input.confirm !== true || (input.content !== undefined && input.content.length > 0)) {
    throw new Error('MobileShareRejected');
  }
}

export function pushEnvelope(kind: string, conversationId: string): { kind: string; conversationId: string; containsContent: false } {
  if (kind !== 'message' && kind !== 'mention') {
    throw new Error('MobilePushRejected');
  }

  return { kind, conversationId, containsContent: false };
}

export async function applyRemoteLogout(
  device: { wipeSecureStorage: boolean },
  bridge: MobileNativeBridge | null,
): Promise<'wiped' | 'browser'> {
  if (!device.wipeSecureStorage || !bridge) {
    return 'browser';
  }

  await bridge.wipe();
  return 'wiped';
}
