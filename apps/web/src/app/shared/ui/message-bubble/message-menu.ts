import type { ConnectedPosition } from '@angular/cdk/overlay';
import { ChannelStore } from '../../../core/services/channel.store';
import { fillTemplate, ui } from '../../../core/i18n/strings';
import { ChatMessage } from '../../models/chat.models';
import { menuActionsForMessage, type MessageMenuActionId } from '../../attachments/attachment-preview';
import { deleteLifecycle, editLifecycle, messagingPolicyOf } from '../../messaging/messaging-policy';

export const MINE_ACTION_MENU_POSITIONS: ConnectedPosition[] = [
  { originX: 'start', originY: 'bottom', overlayX: 'end', overlayY: 'top', offsetX: 0, offsetY: 4 },
  { originX: 'start', originY: 'top', overlayX: 'end', overlayY: 'bottom', offsetX: 0, offsetY: -4 },
  { originX: 'end', originY: 'bottom', overlayX: 'start', overlayY: 'top', offsetX: 0, offsetY: 4 },
  { originX: 'end', originY: 'top', overlayX: 'start', overlayY: 'bottom', offsetX: 0, offsetY: -4 },
];

export const THEIRS_ACTION_MENU_POSITIONS: ConnectedPosition[] = [
  { originX: 'end', originY: 'bottom', overlayX: 'start', overlayY: 'top', offsetX: 0, offsetY: 4 },
  { originX: 'end', originY: 'top', overlayX: 'start', overlayY: 'bottom', offsetX: 0, offsetY: -4 },
  { originX: 'start', originY: 'bottom', overlayX: 'end', overlayY: 'top', offsetX: 0, offsetY: 4 },
  { originX: 'start', originY: 'top', overlayX: 'end', overlayY: 'bottom', offsetX: 0, offsetY: -4 },
];

export interface MessageMenuFlags {
  showForward: boolean;
  showShareToChannel: boolean;
  showThread: boolean;
  showPin: boolean;
  showSave: boolean;
  showRemind: boolean;
  showMarkUnread: boolean;
  hasLinkPreview: boolean;
}

export function buildMessageMenuItems(
  channels: ChannelStore,
  message: ChatMessage,
  own: boolean,
  flags: MessageMenuFlags,
) {
  const policy = messagingPolicyOf(channels);
  const role = channels.activeWorkspace?.()?.role;
  const createdAt = message.createdAt;
  const nowMs = Date.now();
  const edit = editLifecycle({ policy, role, mine: own, createdAt, nowMs });
  const remove = deleteLifecycle({ policy, role, mine: own, createdAt, nowMs });
  return menuActionsForMessage({
    mine: own,
    showForward: flags.showForward,
    showShareToChannel: flags.showShareToChannel,
    showThread: flags.showThread,
    showPin: flags.showPin,
    isPinned: !!message.isPinned,
    showSave: flags.showSave,
    isSaved: !!message.isSaved,
    showRemind: flags.showRemind,
    showMarkUnread: flags.showMarkUnread,
    replyCount: message.replyCount,
    hasLinkPreview: flags.hasLinkPreview,
    allowEdit: edit === 'allow',
    allowDelete: remove === 'allow',
    editExpiredTitle:
      edit === 'expired' && policy.editWindowMinutes != null
        ? fillTemplate(ui.menuEditExpired, { n: policy.editWindowMinutes })
        : undefined,
    deleteExpiredTitle:
      remove === 'expired' && policy.deleteWindowMinutes != null
        ? fillTemplate(ui.menuDeleteExpired, { n: policy.deleteWindowMinutes })
        : undefined,
  });
}

export interface MessageMenuTarget {
  forward: { emit(): void };
  shareToChannel: { emit(): void };
  openThread: { emit(): void };
  startEdit: { emit(): void };
  removeLinkPreview: { emit(): void };
  delete: { emit(): void };
  pin: { emit(): void };
  unpin: { emit(): void };
  save: { emit(): void };
  unsave: { emit(): void };
  remind: { emit(): void };
  markUnread: { emit(): void };
}

export function emitMessageMenuAction(id: MessageMenuActionId, target: MessageMenuTarget): void {
  switch (id) {
    case 'forward':
      target.forward.emit();
      break;
    case 'share-to-channel':
      target.shareToChannel.emit();
      break;
    case 'thread':
      target.openThread.emit();
      break;
    case 'edit':
      target.startEdit.emit();
      break;
    case 'remove-link-preview':
      target.removeLinkPreview.emit();
      break;
    case 'delete':
      target.delete.emit();
      break;
    case 'pin':
      target.pin.emit();
      break;
    case 'unpin':
      target.unpin.emit();
      break;
    case 'save':
      target.save.emit();
      break;
    case 'unsave':
      target.unsave.emit();
      break;
    case 'remind':
      target.remind.emit();
      break;
    case 'mark-unread':
      target.markUnread.emit();
      break;
  }
}
