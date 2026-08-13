import { Component, input, inject } from '@angular/core';
import { ChannelStore } from '../../../core/services/channel.store';
import { DraftStoreService } from '../../../core/services/draft-store.service';
import { MessageStore } from '../../../core/services/message.store';
import { PinStore } from '../../../core/services/pin.store';
import { SavedStore } from '../../../core/services/saved.store';
import { Badge, SidebarNav, Skeleton } from '../../../shared/ui';

@Component({
  selector: 'vc-channel-list',
  standalone: true,
  imports: [SidebarNav, Skeleton, Badge],
  template: `
    <div class="channel-list">
      @if (channels.loading()) {
        <div class="channel-list__loading">
          <vc-skeleton height="2rem" />
          <vc-skeleton height="2rem" />
          <vc-skeleton height="2rem" />
        </div>
      } @else {
        <div class="channel-list__shortcuts">
          <button
            type="button"
            class="channel-list__saved"
            [class.channel-list__saved--compact]="navCompact()"
            data-testid="saved-nav"
            [class.channel-list__saved--active]="saved.panelOpen()"
            [attr.aria-label]="navCompact() ? 'Mensagens salvas' : null"
            [attr.title]="navCompact() ? 'Mensagens salvas' : null"
            (click)="openSaved()"
          >
            <span class="channel-list__saved-mark" aria-hidden="true">✦</span>
            @if (!navCompact()) {
              <span class="channel-list__saved-label">Salvos</span>
            }
            @if (saved.pendingCount() > 0) {
              <vc-badge tone="accent">{{ saved.pendingCount() }}</vc-badge>
            }
          </button>
        </div>

        <vc-sidebar-nav
          [groups]="channels.spaceGroups()"
          [spaces]="channels.spaces()"
          [directs]="channels.directChannels()"
          [members]="channels.peerCandidates()"
          [presence]="channels.presence()"
          [activeId]="channels.activeChannelId() ?? null"
          [draftIds]="drafts.draftConversationIds()"
          [canCreate]="channels.canCreateChannel()"
          [compact]="navCompact()"
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
    .channel-list__shortcuts {
      padding: 0 var(--vc-space-3);
      margin-bottom: 0.15rem;
    }
    .channel-list__saved {
      width: 100%;
      display: grid;
      grid-template-columns: auto 1fr auto;
      align-items: center;
      gap: var(--vc-space-row);
      min-height: var(--vc-density-row);
      padding: 0 0.7rem;
      border: 0;
      border-radius: var(--vc-radius-md);
      background: transparent;
      color: var(--vc-ink-muted);
      text-align: left;
      cursor: pointer;
      position: relative;
      font: inherit;
      transition:
        background var(--vc-dur-fast) var(--vc-ease-out),
        color var(--vc-dur-fast) var(--vc-ease-out);
    }
    .channel-list__saved:hover {
      background: color-mix(in srgb, var(--vc-brand) 10%, transparent);
      color: var(--vc-ink);
    }
    .channel-list__saved--active {
      background: color-mix(in srgb, var(--vc-brand) 16%, transparent);
      color: var(--vc-ink);
    }
    .channel-list__saved--active::before {
      content: '';
      position: absolute;
      left: 0;
      top: 20%;
      bottom: 20%;
      width: 3px;
      border-radius: 999px;
      background: var(--vc-brand);
    }
    .channel-list__saved-mark {
      font-size: 0.75rem;
      opacity: 0.75;
      width: 1rem;
      text-align: center;
    }
    .channel-list__saved-label {
      font-weight: 500;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .channel-list__saved--compact {
      grid-template-columns: 1fr auto;
      width: 2.25rem;
      margin-inline: auto;
      padding: 0;
      justify-items: center;
    }
    .channel-list__saved--compact .channel-list__saved-label {
      display: none;
    }
  `,
})
export class ChannelList {
  readonly navCompact = input(false);
  readonly channels = inject(ChannelStore);
  readonly drafts = inject(DraftStoreService);
  readonly saved = inject(SavedStore);
  private readonly messages = inject(MessageStore);
  private readonly pins = inject(PinStore);

  openSaved(): void {
    this.pins.closePanel();
    this.saved.openPanel();
  }

  async onSelect(channelId: string): Promise<void> {
    this.saved.closePanel();
    this.channels.selectChannel(channelId);
    await this.messages.loadChannel(channelId);
  }

  async onOpenDm(userId: string): Promise<void> {
    this.saved.closePanel();
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
        this.saved.closePanel();
        await this.messages.loadChannel(channel.id);
      }
    } catch (err) {
      // surface via store hint
      console.error(err);
    }
  }
}
