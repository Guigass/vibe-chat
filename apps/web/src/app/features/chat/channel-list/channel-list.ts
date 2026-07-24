import { Component, inject } from '@angular/core';
import { ChannelStore } from '../../../core/services/channel.store';
import { MessageStore } from '../../../core/services/message.store';
import { SidebarNav, Skeleton } from '../../../shared/ui';

@Component({
  selector: 'vc-channel-list',
  standalone: true,
  imports: [SidebarNav, Skeleton],
  template: `
    <div class="channel-list">
      @if (channels.loading()) {
        <div class="channel-list__loading">
          <vc-skeleton height="2rem" />
          <vc-skeleton height="2rem" />
          <vc-skeleton height="2rem" />
        </div>
      } @else {
        <vc-sidebar-nav
          [channels]="channels.publicChannels()"
          [directs]="channels.directChannels()"
          [members]="channels.peerCandidates()"
          [activeId]="channels.activeChannel()?.id ?? null"
          (select)="onSelect($event)"
          (openDm)="onOpenDm($event)"
        />
      }
    </div>
  `,
  styles: `
    .channel-list {
      min-height: 0;
      overflow: auto;
    }
    .channel-list__loading {
      display: grid;
      gap: 0.5rem;
      padding: 0 var(--vc-space-4);
    }
  `,
})
export class ChannelList {
  readonly channels = inject(ChannelStore);
  private readonly messages = inject(MessageStore);

  async onSelect(channelId: string): Promise<void> {
    this.channels.selectChannel(channelId);
    await this.messages.loadChannel(channelId);
  }

  async onOpenDm(userId: string): Promise<void> {
    const channel = await this.channels.openDirectMessage(userId);
    if (channel) {
      await this.messages.loadChannel(channel.id);
    }
  }
}
