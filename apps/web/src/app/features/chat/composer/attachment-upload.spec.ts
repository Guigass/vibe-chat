import { describe, expect, it } from 'vitest';
import {
  MAX_ATTACHMENTS_PER_MESSAGE,
  MAX_ATTACHMENT_SIZE_BYTES,
  collectFilesFromDataTransfer,
  resolveContentType,
  validateAttachmentFile,
} from './attachment-upload';

describe('attachment-upload validation', () => {
  it('resolves content type from extension when file.type is empty', () => {
    const file = new File(['x'], 'photo.PNG', { type: '' });
    expect(resolveContentType(file)).toBe('image/png');
    expect(validateAttachmentFile(file)).toBeNull();
  });

  it('accepts allowed types within size limit', () => {
    const file = new File(['hello'], 'notes.txt', { type: 'text/plain' });
    expect(validateAttachmentFile(file)).toBeNull();
  });

  it('rejects disallowed content type with file name', () => {
    const file = new File(['x'], 'evil.exe', { type: 'application/x-msdownload' });
    const error = validateAttachmentFile(file);
    expect(error?.fileName).toBe('evil.exe');
    expect(error?.reason).toContain('tipo não permitido');
  });

  it('rejects files above max size with file name', () => {
    const file = new File([new Uint8Array(MAX_ATTACHMENT_SIZE_BYTES + 1)], 'big.png', {
      type: 'image/png',
    });
    const error = validateAttachmentFile(file);
    expect(error?.fileName).toBe('big.png');
    expect(error?.reason).toContain('excede');
  });

  it('ignores dataTransfer without Files type', () => {
    const dt = {
      types: ['text/plain'],
      files: [],
    } as unknown as DataTransfer;
    expect(collectFilesFromDataTransfer(dt)).toEqual([]);
  });

  it('exports max attachment count of 10', () => {
    expect(MAX_ATTACHMENTS_PER_MESSAGE).toBe(10);
  });
});
