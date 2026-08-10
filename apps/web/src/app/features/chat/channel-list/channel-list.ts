import { Component, inject } from '@angular/core';
import { ChannelStore } from '../../../core/services/channel.store';
import { DraftStoreService } from '../../../core/services/draft-store.service';
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
          [groups]="channels.spaceGroups()"
          [spaces]="channels.spaces()"
          [directs]="channels.directChannels()"
          [members]="channels.peerCandidates()"
          [presence]="channels.presence()"
          [activeId]="channels.activeChannelId() ?? null"
          [draftIds]="drafts.draftConversationIds()"
          [canCreate]="channels.canCreateChannel()"
          (select)="onSelect($event)"
          (openDm)="onOpenDm($event)"
          (createChannel)="onCreate($event)"
        />
      }
    </div>
  `,
  styles: `
    :host {
      display: block;
      min-height: 0;
      overflow: hidden;
    }
    .channel-list {
      height: 100%;
      min-height: 0;
      overflow: auto;
      overscroll-behavior: contain;
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
  readonly drafts = inject(DraftStoreService);
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

  async onCreate(input: {
    name: string;
    type: string;
    spaceId?: string | null;
    newSpaceName?: string;
  }): Promise<void> {
    try {
      const channel = await this.channels.createChannel(input);
      if (channel) {
        await this.messages.loadChannel(channel.id);
      }
    } catch (err) {
      // surface via store hint
      console.error(err);
    }
  }
}
