import { createHmac, timingSafeEqual } from 'node:crypto';

export const sdkVersion = '1.0.0';
export const apiContract = '1';
export const license = 'Apache-2.0';
export const sandboxSecret = 'sandbox-not-a-secret';

const maxAgeMs = 5 * 60 * 1000;
const clockSkewMs = 60 * 1000;

const supported = new Set([
  'messaging.message.created:1',
  'messaging.message.created:2',
  'messaging.message.edited:1',
  'messaging.message.deleted:1',
  'messaging.message.reaction_changed:1',
  'integrations.webhook.test:1',
  'audit.event.recorded:1',
]);

export interface SdkVerification {
  accepted: boolean;
  reason: string;
}

export function computeSignature(secret: string, body: string): string {
  const hash = createHmac('sha256', secret).update(body, 'utf8').digest('hex');
  return `sha256=${hash}`;
}

export function verifyWebhook(
  secret: string,
  body: string,
  signature: string | null | undefined,
  deliveryId: string | null | undefined,
  deliveredAt: Date,
  now: Date,
  seenDeliveryIds: readonly string[],
): SdkVerification {
  const id = (deliveryId ?? '').trim();
  if (id.length < 1 || id.length > 80) {
    return { accepted: false, reason: 'DeliveryIdRequired' };
  }

  const expected = computeSignature(secret, body);
  if (!fixedEquals(expected, (signature ?? '').trim())) {
    return { accepted: false, reason: 'InvalidSignature' };
  }

  const skew = deliveredAt.getTime() - now.getTime();
  if (skew > clockSkewMs || now.getTime() - deliveredAt.getTime() > maxAgeMs) {
    return { accepted: false, reason: 'StaleDelivery' };
  }

  if (seenDeliveryIds.includes(id)) {
    return { accepted: false, reason: 'ReplayDetected' };
  }

  return { accepted: true, reason: 'Accepted' };
}

export function tryNormalizeIdempotencyKey(key: string | null | undefined): string | null {
  const normalized = (key ?? '').trim();
  if (normalized.length < 8 || normalized.length > 120 || /\s/.test(normalized)) {
    return null;
  }

  return normalized;
}

export function scrub(value: string | null | undefined): string {
  if (!value) return '';
  return value
    .replace(/vc_int_[A-Za-z0-9_-]+/g, '[redacted]')
    .replace(/vc_cb_[A-Za-z0-9_-]+/g, '[redacted]')
    .replace(/Bearer \S+/g, 'Bearer [redacted]');
}

export function isCompatible(version: string, contract: string, eventType: string, schemaVersion: number): boolean {
  if (version !== sdkVersion || contract !== apiContract) return false;
  return supported.has(`${eventType}:${schemaVersion}`);
}

export function sandboxSend(workspaceId: string, channelId: string, body: string, idempotencyKey: string): {
  method: 'POST';
  path: string;
  headers: Record<string, string>;
  body: { operation: string; channelId: string; body: string; idempotencyKey: string };
} {
  return {
    method: 'POST',
    path: `/api/v1/workspaces/${workspaceId}/developer/sdk/sandbox`,
    headers: { 'Idempotency-Key': idempotencyKey },
    body: { operation: 'messages.send', channelId, body, idempotencyKey },
  };
}

function fixedEquals(expected: string, provided: string): boolean {
  const left = Buffer.from(expected, 'utf8');
  const right = Buffer.from(provided, 'utf8');
  if (left.length !== right.length) return false;
  return timingSafeEqual(left, right);
}
