import { describe, expect, it } from 'vitest';
import {
  MAX_ATTACHMENTS_PER_MESSAGE,
  MAX_ATTACHMENT_SIZE_BYTES,
  collectFilesFromDataTransfer,
  resolveContentType,
  validateAttachmentFile,
  validateVideoAttachmentFile,
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

  it('rejects video above max size', () => {
    const file = new File([new Uint8Array(26 * 1024 * 1024)], 'big.mp4', { type: 'video/mp4' });
    const error = validateVideoAttachmentFile(file);
    expect(error?.reason).toContain('excede');
  });

  it('rejects video above max duration when metadata known', () => {
    const file = new File(['x'], 'long.webm', { type: 'video/webm' });
    const error = validateVideoAttachmentFile(file, 'video/webm', 61_000);
    expect(error?.reason).toContain('duração');
  });

  it('accepts allowed video mime within limits', () => {
    const file = new File(['x'], 'clip.mp4', { type: 'video/mp4' });
    expect(validateVideoAttachmentFile(file, 'video/mp4', 30_000)).toBeNull();
  });

  it('exports max attachment count of 10', () => {
    expect(MAX_ATTACHMENTS_PER_MESSAGE).toBe(10);
  });
});
