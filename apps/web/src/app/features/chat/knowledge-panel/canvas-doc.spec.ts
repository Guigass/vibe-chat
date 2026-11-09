import { describe, expect, it } from 'vitest';
import * as Y from 'yjs';
import { applyString, canvasTextName } from './canvas-doc';

describe('canvas doc', () => {
  it('converges after a partition', () => {
    const origin = new Y.Doc();
    origin.getText(canvasTextName).insert(0, 'Olá');
    const state = Y.encodeStateAsUpdate(origin);
    const vector = Y.encodeStateVector(origin);

    const left = new Y.Doc();
    Y.applyUpdate(left, state);
    left.getText(canvasTextName).insert(3, ' Alice');
    const right = new Y.Doc();
    Y.applyUpdate(right, state);
    right.getText(canvasTextName).insert(0, 'Bob ');

    const leftUpdate = Y.encodeStateAsUpdate(left, vector);
    const rightUpdate = Y.encodeStateAsUpdate(right, vector);
    const first = new Y.Doc();
    Y.applyUpdate(first, state);
    Y.applyUpdate(first, leftUpdate);
    Y.applyUpdate(first, rightUpdate);
    const second = new Y.Doc();
    Y.applyUpdate(second, state);
    Y.applyUpdate(second, rightUpdate);
    Y.applyUpdate(second, leftUpdate);

    expect(first.getText(canvasTextName).toString()).toBe(second.getText(canvasTextName).toString());
    expect(first.getText(canvasTextName).toString()).toContain('Alice');
    expect(first.getText(canvasTextName).toString()).toContain('Bob');
  });

  it('applies a textarea diff without dropping the other side', () => {
    const doc = new Y.Doc();
    const text = doc.getText(canvasTextName);
    text.insert(0, 'Olá');
    doc.transact(() => applyString(text, 'Olá Alice'));
    expect(text.toString()).toBe('Olá Alice');
    doc.transact(() => applyString(text, 'Bob Olá Alice'));
    expect(text.toString()).toBe('Bob Olá Alice');
  });
});
