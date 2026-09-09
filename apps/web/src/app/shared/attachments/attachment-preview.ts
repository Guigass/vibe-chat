import { ui } from '../../core/i18n/strings';

export type AttachmentPreviewKind = 'image' | 'pdf' | 'audio' | 'video' | 'file';

export function classifyAttachmentPreview(
  contentType: string,
  kind?: 'File' | 'Audio' | 'Video',
): AttachmentPreviewKind {
  const type = (contentType ?? '').trim().toLowerCase();
  if (kind === 'Audio' || type.startsWith('audio/')) return 'audio';
  if (kind === 'Video' || type.startsWith('video/')) return 'video';
  if (type.startsWith('image/')) return 'image';
  if (type === 'application/pdf') return 'pdf';
  return 'file';
}

export function isGifContentType(contentType: string): boolean {
  return (contentType ?? '').trim().toLowerCase() === 'image/gif';
}

export function attachmentFamilyIcon(
  contentType: string,
  kind?: 'File' | 'Audio' | 'Video',
): string {
  switch (classifyAttachmentPreview(contentType, kind)) {
    case 'image':
      return '🖼';
    case 'pdf':
      return '📄';
    case 'audio':
      return '🎵';
    case 'video':
      return '🎬';
    default:
      if ((contentType ?? '').toLowerCase().startsWith('text/')) return '📝';
      return '📎';
  }
}

export function formatAttachmentSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

export type MessageMenuActionId =
  | 'forward'
  | 'thread'
  | 'edit'
  | 'remove-link-preview'
  | 'delete'
  | 'pin'
  | 'unpin'
  | 'save'
  | 'unsave'
  | 'mark-unread';

export function menuActionsForMessage(options: {
  mine: boolean;
  showForward: boolean;
  showThread: boolean;
  showPin?: boolean;
  isPinned?: boolean;
  showSave?: boolean;
  isSaved?: boolean;
  showMarkUnread?: boolean;
  replyCount?: number;
  hasLinkPreview?: boolean;
}): Array<{ id: MessageMenuActionId; label: string; danger?: boolean }> {
  const items: Array<{
    id: MessageMenuActionId;
    label: string;
    danger?: boolean;
  }> = [];
  if (options.showForward) {
    items.push({ id: 'forward', label: ui.menuForward });
  }
  if (options.showThread) {
    const count = options.replyCount ?? 0;
    items.push({
      id: 'thread',
      label:
        count > 0
          ? `${count} ${count === 1 ? ui.menuReply : ui.menuReplies}`
          : ui.menuOpenThread,
    });
  }
  if (options.showPin) {
    items.push({
      id: options.isPinned ? 'unpin' : 'pin',
      label: options.isPinned ? ui.menuUnpin : ui.menuPin,
    });
  }
  if (options.showSave) {
    items.push({
      id: options.isSaved ? 'unsave' : 'save',
      label: options.isSaved ? ui.menuUnsave : ui.menuSave,
    });
  }
  if (options.showMarkUnread) {
    items.push({ id: 'mark-unread', label: ui.menuMarkUnread });
  }
  if (options.mine) {
    items.push({ id: 'edit', label: ui.menuEdit });
    if (options.hasLinkPreview) {
      items.push({ id: 'remove-link-preview', label: ui.menuRemovePreview });
    }
    items.push({ id: 'delete', label: ui.menuDelete, danger: true });
  }
  return items;
}
