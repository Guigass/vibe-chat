const textEncoder = new TextEncoder();
const textDecoder = new TextDecoder();

export function offlineCryptoAvailable(): boolean {
  return typeof crypto !== 'undefined' && typeof crypto.subtle !== 'undefined';
}

export async function createOfflineKey(): Promise<CryptoKey> {
  return crypto.subtle.generateKey({ name: 'AES-GCM', length: 256 }, false, ['encrypt', 'decrypt']);
}

export async function sealText(key: CryptoKey, plaintext: string): Promise<string> {
  const iv = crypto.getRandomValues(new Uint8Array(12));
  const cipher = await crypto.subtle.encrypt({ name: 'AES-GCM', iv }, key, textEncoder.encode(plaintext));
  return JSON.stringify({
    iv: bytesToBase64(iv),
    data: bytesToBase64(new Uint8Array(cipher)),
  });
}

export async function openText(key: CryptoKey, packed: string): Promise<string | null> {
  try {
    const parsed = JSON.parse(packed) as { iv?: string; data?: string };
    if (!parsed.iv || !parsed.data) return null;
    const plain = await crypto.subtle.decrypt(
      { name: 'AES-GCM', iv: toBuffer(base64ToBytes(parsed.iv)) },
      key,
      toBuffer(base64ToBytes(parsed.data)),
    );
    return textDecoder.decode(plain);
  } catch {
    return null;
  }
}

function bytesToBase64(bytes: Uint8Array): string {
  let binary = '';
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary);
}

function base64ToBytes(value: string): Uint8Array {
  const binary = atob(value);
  const bytes = new Uint8Array(binary.length);
  for (let index = 0; index < binary.length; index += 1) bytes[index] = binary.charCodeAt(index);
  return bytes;
}

function toBuffer(bytes: Uint8Array): ArrayBuffer {
  return bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength) as ArrayBuffer;
}
