import { ChannelStore } from '../../../core/services/channel.store';
import { MessageStore } from '../../../core/services/message.store';

export function isAnnouncementReadOnly(channels: ChannelStore): boolean {
  const channel = channels.activeChannel();
  return channel?.type === 'announcement' && !channels.canPublishAnnouncement();
}

export function isCanPublishAnnouncement(channels: ChannelStore, messages: MessageStore): boolean {
  const channel = channels.activeChannel();
  return (
    channel?.type === 'announcement' &&
    channels.canPublishAnnouncement() &&
    !messages.editingMessage()
  );
}
