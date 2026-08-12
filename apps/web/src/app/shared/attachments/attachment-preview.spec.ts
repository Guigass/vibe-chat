import { describe, expect, it } from 'vitest';
import {
  attachmentFamilyIcon,
  classifyAttachmentPreview,
  formatAttachmentSize,
  isGifContentType,
  menuActionsForMessage,
} from './attachment-preview';

describe('classifyAttachmentPreview', () => {
  it('classifies images by content type', () => {
    expect(classifyAttachmentPreview('image/png')).toBe('image');
    expect(classifyAttachmentPreview('image/jpeg')).toBe('image');
    expect(classifyAttachmentPreview('image/webp')).toBe('image');
    expect(classifyAttachmentPreview('image/gif')).toBe('image');
  });

  it('classifies pdf, video, audio and file fallbacks', () => {
    expect(classifyAttachmentPreview('application/pdf')).toBe('pdf');
    expect(classifyAttachmentPreview('video/mp4')).toBe('video');
    expect(classifyAttachmentPreview('audio/webm', 'File')).toBe('audio');
    expect(classifyAttachmentPreview('text/plain', 'File')).toBe('file');
    expect(classifyAttachmentPreview('application/zip')).toBe('file');
  });

  it('prefers Audio kind over generic content type', () => {
    expect(classifyAttachmentPreview('application/octet-stream', 'Audio')).toBe('audio');
  });

  it('prefers Video kind over generic content type', () => {
    expect(classifyAttachmentPreview('application/octet-stream', 'Video')).toBe('video');
  });
});

describe('attachment preview helpers', () => {
  it('detects gif content type', () => {
    expect(isGifContentType('image/gif')).toBe(true);
    expect(isGifContentType('image/png')).toBe(false);
  });

  it('formats sizes and family icons', () => {
    expect(formatAttachmentSize(512)).toBe('512 B');
    expect(formatAttachmentSize(2048)).toBe('2.0 KB');
    expect(attachmentFamilyIcon('image/png')).toBe('🖼');
    expect(attachmentFamilyIcon('application/pdf')).toBe('📄');
    expect(attachmentFamilyIcon('video/webm')).toBe('🎬');
    expect(attachmentFamilyIcon('text/plain')).toBe('📝');
  });

  it('filters menu actions by mine / flags', () => {
    expect(
      menuActionsForMessage({ mine: false, showForward: false, showThread: false }),
    ).toEqual([]);

    expect(
      menuActionsForMessage({
        mine: true,
        showForward: true,
        showThread: true,
        replyCount: 2,
      }).map((item) => item.id),
    ).toEqual(['forward', 'thread', 'edit', 'delete']);

    expect(
      menuActionsForMessage({
        mine: true,
        showForward: false,
        showThread: false,
        hasLinkPreview: true,
      }).map((item) => item.id),
    ).toEqual(['edit', 'remove-link-preview', 'delete']);

    expect(
      menuActionsForMessage({
        mine: false,
        showForward: false,
        showThread: false,
        showSave: true,
        isSaved: false,
      }).map((item) => item.id),
    ).toEqual(['save']);

    expect(
      menuActionsForMessage({
        mine: false,
        showForward: false,
        showThread: false,
        showSave: true,
        isSaved: true,
      }).map((item) => item.id),
    ).toEqual(['unsave']);

    const deleteItem = menuActionsForMessage({
      mine: true,
      showForward: false,
      showThread: false,
    }).find((item) => item.id === 'delete');
    expect(deleteItem?.danger).toBe(true);
    expect(deleteItem?.label).toBe('Apagar');
  });
});
