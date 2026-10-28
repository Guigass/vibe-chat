import { AeadId, CipherSuite, KdfId, KemId } from 'hpke-js';

const INFO = new TextEncoder().encode('vibechat.e2ee.envelope.v1');
const STORAGE_KEY = 'vibechat.e2ee.device.v1';
export const SEALED_CONTENT_TYPE = 'application/vnd.vibechat.e2ee';

export interface E2eePublicDevice {
  deviceId: string;
  publicJwk: JsonWebKey;
}

export interface E2eeDeviceRecord {
  deviceId: string;
  publicJwk: JsonWebKey;
  privateJwk: JsonWebKey;
}

export interface OpenedBytes {
  name: string;
  type: string;
  bytes: Uint8Array;
}

export function isConfidentialChannel(channel: { type?: string | null } | null | undefined): boolean {
  return (channel?.type ?? '').replaceAll('_', '').toLowerCase() === 'confidentiale2ee';
}

export function isE2eeEnvelope(body: string | null | undefined): boolean {
  if (!body || !body.startsWith('{')) return false;
  try {
    const parsed = JSON.parse(body) as { v?: number; recipients?: unknown };
    return parsed.v === 1 && Array.isArray(parsed.recipients);
  } catch {
    return false;
  }
}

export function isSealedContentType(contentType: string | null | undefined): boolean {
  return (contentType ?? '').split(';')[0].trim().toLowerCase() === SEALED_CONTENT_TYPE;
}

export function loadDevice(): E2eeDeviceRecord | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as E2eeDeviceRecord;
    if (!parsed.deviceId || !parsed.publicJwk || !parsed.privateJwk) return null;
    return parsed;
  } catch {
    return null;
  }
}

export function saveDevice(record: E2eeDeviceRecord): void {
  localStorage.setItem(STORAGE_KEY, JSON.stringify(record));
}

export async function createDevice(): Promise<E2eeDeviceRecord> {
  const pair = await suite().generateKeyPair();
  const record: E2eeDeviceRecord = {
    deviceId: crypto.randomUUID(),
    publicJwk: await crypto.subtle.exportKey('jwk', pair.publicKey),
    privateJwk: await crypto.subtle.exportKey('jwk', pair.privateKey),
  };
  saveDevice(record);
  return record;
}

export async function fingerprintOf(jwk: JsonWebKey): Promise<string> {
  const material = `${jwk.kty}|${jwk.crv}|${jwk.x}|${jwk.y}`;
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(material));
  return [...new Uint8Array(digest)].map((byte) => byte.toString(16).padStart(2, '0')).join('');
}

export function formatFingerprint(hex: string): string {
  return hex.match(/.{1,4}/g)?.join(' ') ?? hex;
}

export async function sealText(
  plaintext: string,
  epoch: number,
  recipients: E2eePublicDevice[],
): Promise<string> {
  const bytes = new TextEncoder().encode(plaintext);
  const sealed = [];
  for (const recipient of recipients) {
    const hpke = suite();
    const sender = await hpke.createSenderContext({
      recipientPublicKey: await hpke.importKey('jwk', recipient.publicJwk, true),
      info: INFO,
    });
    const ct = new Uint8Array(await sender.seal(toArrayBuffer(bytes)));
    sealed.push({
      kid: recipient.deviceId,
      enc: bytesToBase64Url(new Uint8Array(sender.enc)),
      ct: bytesToBase64Url(ct),
    });
  }
  return JSON.stringify({ v: 1, epoch, recipients: sealed });
}

export async function openText(envelope: string): Promise<string | null> {
  const device = loadDevice();
  if (!device) return null;
  const parsed = JSON.parse(envelope) as {
    recipients?: { kid: string; enc: string; ct: string }[];
  };
  const mine = (parsed.recipients ?? []).find((row) => row.kid === device.deviceId);
  if (!mine) return null;
  const hpke = suite();
  const recipient = await hpke.createRecipientContext({
    recipientKey: await hpke.importKey('jwk', device.privateJwk, false),
    enc: toArrayBuffer(base64UrlToBytes(mine.enc)),
    info: INFO,
  });
  const opened = new Uint8Array(await recipient.open(toArrayBuffer(base64UrlToBytes(mine.ct))));
  return new TextDecoder().decode(opened);
}

export async function sealFile(file: File, epoch: number, recipients: E2eePublicDevice[]): Promise<File> {
  const payload = JSON.stringify({
    name: file.name,
    type: file.type || 'application/octet-stream',
    bytes: bytesToBase64Url(new Uint8Array(await file.arrayBuffer())),
  });
  const envelope = await sealText(payload, epoch, recipients);
  return new File([envelope], `${file.name}.e2ee`, { type: SEALED_CONTENT_TYPE });
}

export async function openSealedBlob(bytes: Uint8Array): Promise<OpenedBytes | null> {
  const text = new TextDecoder().decode(bytes);
  const inner = await openText(text);
  if (!inner) return null;
  const parsed = JSON.parse(inner) as { name?: string; type?: string; bytes?: string };
  if (!parsed.bytes) return null;
  return {
    name: parsed.name || 'file',
    type: parsed.type || 'application/octet-stream',
    bytes: base64UrlToBytes(parsed.bytes),
  };
}

export async function sealBackup(password: string, record: E2eeDeviceRecord): Promise<string> {
  const salt = crypto.getRandomValues(new Uint8Array(16));
  const iv = crypto.getRandomValues(new Uint8Array(12));
  const key = await deriveKey(password, salt);
  const ct = new Uint8Array(
    await crypto.subtle.encrypt(
      { name: 'AES-GCM', iv },
      key,
      new TextEncoder().encode(JSON.stringify(record.privateJwk)),
    ),
  );
  return JSON.stringify({
    v: '1',
    salt: bytesToBase64Url(salt),
    iv: bytesToBase64Url(iv),
    ct: bytesToBase64Url(ct),
  });
}

function suite(): CipherSuite {
  return new CipherSuite({
    kem: KemId.DhkemP256HkdfSha256,
    kdf: KdfId.HkdfSha256,
    aead: AeadId.Aes256Gcm,
  });
}

function toArrayBuffer(bytes: Uint8Array): ArrayBuffer {
  return bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength) as ArrayBuffer;
}

async function deriveKey(password: string, salt: Uint8Array): Promise<CryptoKey> {
  const base = await crypto.subtle.importKey('raw', new TextEncoder().encode(password), 'PBKDF2', false, [
    'deriveKey',
  ]);
  return crypto.subtle.deriveKey(
    { name: 'PBKDF2', salt: salt.buffer as ArrayBuffer, iterations: 210_000, hash: 'SHA-256' },
    base,
    { name: 'AES-GCM', length: 256 },
    false,
    ['encrypt', 'decrypt'],
  );
}

function bytesToBase64Url(bytes: Uint8Array): string {
  let binary = '';
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replaceAll('+', '-').replaceAll('/', '_').replaceAll('=', '');
}

function base64UrlToBytes(value: string): Uint8Array {
  const padded = value.replaceAll('-', '+').replaceAll('_', '/') + '='.repeat((4 - (value.length % 4)) % 4);
  const binary = atob(padded);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i += 1) bytes[i] = binary.charCodeAt(i);
  return bytes;
}
