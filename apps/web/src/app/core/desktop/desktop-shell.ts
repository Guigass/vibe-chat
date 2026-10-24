export interface DesktopNativeBridge {
  secureStore(key: string, value: string): Promise<void>;
  wipe(): Promise<void>;
}

export interface DesktopShellMode {
  mode: 'desktop' | 'browser';
  browserFallback: boolean;
}

const tokenKeys = ['access_token', 'refresh_token', 'id_token', 'authorization'];

export function resolveDesktopShell(bridge: DesktopNativeBridge | null | undefined): DesktopShellMode {
  if (!bridge) {
    return { mode: 'browser', browserFallback: true };
  }

  return { mode: 'desktop', browserFallback: true };
}

export function rejectWebTokenWrite(key: string): void {
  const normalized = key.trim().toLowerCase();
  if (tokenKeys.some((name) => normalized.includes(name))) {
    throw new Error('DesktopTokenRejected');
  }
}
