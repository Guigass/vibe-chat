import { describe, expect, it } from 'vitest';
import {
  deriveAvailability,
  isStatusActive,
  statusLine,
  withPresence,
  type MemberAvailability,
} from './user-status';

const now = Date.parse('2026-10-02T22:00:00Z');

function entry(partial: Partial<MemberAvailability> = {}): MemberAvailability {
  return {
    userId: 'u1',
    presence: 'online',
    availability: 'available',
    status: null,
    ...partial,
  };
}

describe('user status availability', () => {
  it('expires at the deadline and keeps the full text for readers', () => {
    expect(isStatusActive({ state: 'custom', emoji: '☕', text: 'x'.repeat(80), clearAtEndOfDay: false, expiresAt: '2026-10-02T22:00:00Z' }, now)).toBe(false);
    expect(isStatusActive({ state: 'custom', emoji: '', text: 'ainda', clearAtEndOfDay: false, expiresAt: '2026-10-02T22:00:01Z' }, now)).toBe(true);

    const line = statusLine(entry({
      status: {
        state: 'custom',
        emoji: '☕',
        text: 'um texto longo que a tela corta e o leitor de tela recebe inteiro',
        clearAtEndOfDay: false,
        expiresAt: null,
      },
    }), now);
    expect(line).toContain('um texto longo');
  });

  it('derives availability and keeps DND busy across presence changes', () => {
    expect(deriveAvailability('online', false, null, false)).toBe('available');
    expect(deriveAvailability('online', true, 'custom', true)).toBe('busy');
    expect(deriveAvailability('offline', false, 'vacation', true)).toBe('vacation');

    const busy = withPresence(entry({
      availability: 'busy',
      presence: 'online',
      status: { state: 'custom', emoji: '', text: 'livre?', clearAtEndOfDay: false, expiresAt: null },
    }), 'away', now);
    expect(busy.availability).toBe('busy');
    expect(busy.presence).toBe('away');
  });
});
