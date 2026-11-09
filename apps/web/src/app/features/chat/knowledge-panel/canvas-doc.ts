import * as Y from 'yjs';

export const canvasTextName = 'body';

export function bytesToBase64(bytes: Uint8Array): string {
  let binary = '';
  for (const byte of bytes) {
    binary += String.fromCharCode(byte);
  }
  return btoa(binary);
}

export function base64ToBytes(value: string): Uint8Array {
  const binary = atob(value);
  const bytes = new Uint8Array(binary.length);
  for (let index = 0; index < binary.length; index++) {
    bytes[index] = binary.charCodeAt(index);
  }
  return bytes;
}

export function applyString(text: Y.Text, next: string): void {
  const prev = text.toString();
  if (prev === next) {
    return;
  }
  let start = 0;
  const max = Math.min(prev.length, next.length);
  while (start < max && prev[start] === next[start]) {
    start++;
  }
  let endPrev = prev.length;
  let endNext = next.length;
  while (endPrev > start && endNext > start && prev[endPrev - 1] === endNextChar(next, endNext)) {
    endPrev--;
    endNext--;
  }
  if (endPrev > start) {
    text.delete(start, endPrev - start);
  }
  if (endNext > start) {
    text.insert(start, next.slice(start, endNext));
  }
}

function endNextChar(value: string, index: number): string {
  return value[index - 1];
}
