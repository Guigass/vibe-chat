import { describe, expect, it } from 'vitest';
import {
  formatSystemEventLabel,
  isSystemEventBody,
  parseSystemEventBody,
} from './system-event';

describe('system-event', () => {
  it('parses pin and unpin tokens', () => {
    const id = 'a0eebc99-9c0b-4ef8-bb6d-6bb9bd380a11';
    expect(parseSystemEventBody(`<system:pin:${id}>`)).toEqual({
      kind: 'pin',
      targetMessageId: id,
    });
    expect(parseSystemEventBody(`<system:unpin:${id}>`)).toEqual({
      kind: 'unpin',
      targetMessageId: id,
    });
    expect(isSystemEventBody(`<system:pin:${id}>`)).toBe(true);
    expect(isSystemEventBody('hello')).toBe(false);
  });

  it('formats labels in pt-BR', () => {
    const id = 'a0eebc99-9c0b-4ef8-bb6d-6bb9bd380a11';
    expect(
      formatSystemEventLabel('Alice', { kind: 'pin', targetMessageId: id }),
    ).toBe('Alice fixou uma mensagem');
    expect(
      formatSystemEventLabel('Bob', { kind: 'unpin', targetMessageId: id }),
    ).toBe('Bob desafixou uma mensagem');
  });
});
