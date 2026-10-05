import { Component, computed, inject } from '@angular/core';
import { ui } from '../../../core/i18n/strings';
import { ChannelStore } from '../../../core/services/channel.store';
import { MessageStore } from '../../../core/services/message.store';
import { isAnnouncementReadOnly, isCanPublishAnnouncement } from './composer-announcement';
import { ComposerState } from './composer-state';

@Component({
  selector: 'vc-composer-announcement-ack',
  standalone: true,
  templateUrl: './composer-announcement-ack.html',
  styleUrl: './composer-announcement-ack.scss',
})
export class ComposerAnnouncementAck {
  private readonly state = inject(ComposerState);
  private readonly channels = inject(ChannelStore);
  private readonly messages = inject(MessageStore);
  readonly ui = ui;
  readonly announcementReadOnly = computed(() => isAnnouncementReadOnly(this.channels));
  readonly canPublishAnnouncement = computed(() => isCanPublishAnnouncement(this.channels, this.messages));
  readonly requireAck = this.state.requireAck;
  readonly ackByLocal = this.state.ackByLocal;

  onRequireAckChange(event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    this.requireAck.set(checked);
    if (!checked) this.ackByLocal.set('');
  }

  onAckByChange(event: Event): void {
    this.ackByLocal.set((event.target as HTMLInputElement).value);
  }
}
