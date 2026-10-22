import { describe, expect, it } from 'vitest';
import {
  computeSignature,
  isCompatible,
  sandboxSend,
  scrub,
  tryNormalizeIdempotencyKey,
  verifyWebhook,
} from '../../../../../../sdks/typescript/src/index';

const body = '{"synthetic":true}';
const golden = 'sha256=4eb5148d02412ccd84b203e568a9c296ac3e36c993333c9b918c2eb67f05ba9b';
const now = new Date('2026-10-22T12:00:00.000Z');

describe('plugin sdk (B-136)', () => {
  it('matches the .NET golden vector and rejects replay, stale and bad signatures', () => {
    expect(computeSignature('sandbox-not-a-secret', body)).toBe(golden);
    expect(verifyWebhook('sandbox-not-a-secret', body, golden, 'delivery-1', now, now, []).reason).toBe('Accepted');
    expect(verifyWebhook('sandbox-not-a-secret', body, golden.toUpperCase(), 'delivery-1', now, now, []).reason).toBe(
      'InvalidSignature',
    );
    expect(verifyWebhook('sandbox-not-a-secret', body, golden, 'delivery-1', now, now, ['delivery-1']).reason).toBe(
      'ReplayDetected',
    );
    expect(
      verifyWebhook('sandbox-not-a-secret', body, golden, 'delivery-1', new Date('2026-10-22T11:54:00.000Z'), now, [])
        .reason,
    ).toBe('StaleDelivery');
  });

  it('keeps the version window, scrubs tokens and omits them from the sandbox request', () => {
    expect(isCompatible('1.0.0', '1', 'messaging.message.created', 1)).toBe(true);
    expect(isCompatible('1.0.0', '1', 'messaging.message.created', 2)).toBe(true);
    expect(isCompatible('1.0.0', '1', 'messaging.message.created', 99)).toBe(false);
    expect(isCompatible('0.9.0', '1', 'messaging.message.created', 1)).toBe(false);
    expect(scrub('vc_int_abc123 Bearer raw-token')).toBe('[redacted] Bearer [redacted]');
    expect(tryNormalizeIdempotencyKey('short')).toBeNull();
    const request = sandboxSend('ws', 'ch', 'hello', 'quickstart-key');
    expect(request.headers['Authorization']).toBeUndefined();
    expect(request.headers['X-VibeChat-Integration-Token']).toBeUndefined();
    expect(JSON.stringify(request)).not.toContain('vc_int_');
  });
});