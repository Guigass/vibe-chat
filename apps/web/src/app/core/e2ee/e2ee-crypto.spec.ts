import { describe, expect, it } from 'vitest';
import { createDevice, fingerprintOf, openText, sealText } from './e2ee-crypto';

const vector = {
  kty: 'EC',
  crv: 'P-256',
  x: 'f83OJ3D2xF1Bg8vub9tLe1gHMzV76e8Tus9uPHvRVEU',
  y: 'x_FEzRu9m36HLN_tue659LNpXW6pCyStikYjKIWI5a0',
};

describe('e2ee crypto', () => {
  it('matches the canonical fingerprint vector', async () => {
    await expect(fingerprintOf(vector)).resolves.toBe(
      'a8f3cb6cf809a56384e408f1476f354390efbd154ed0009ec9b5dfc6fe55512d',
    );
  });

  it('seals for the recipient and fails closed for another device', async () => {
    const alice = await createDevice();
    const bob = await createDevice();
    const secret = 'secret-phrase-not-on-the-server';
    const envelope = await sealText(secret, 3, [
      { deviceId: bob.deviceId, publicJwk: bob.publicJwk },
    ]);
    expect(envelope).not.toContain(secret);
    localStorage.setItem(
      'vibechat.e2ee.device.v1',
      JSON.stringify(bob),
    );
    await expect(openText(envelope)).resolves.toBe(secret);
    localStorage.setItem(
      'vibechat.e2ee.device.v1',
      JSON.stringify(alice),
    );
    await expect(openText(envelope)).resolves.toBeNull();
  });
});
