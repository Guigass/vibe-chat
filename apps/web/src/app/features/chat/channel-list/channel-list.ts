import { Component, input, inject } from '@angular/core';
import { ChannelStore } from '../../../core/services/channel.store';
import { DraftStoreService } from '../../../core/services/draft-store.service';
import { MessageStore } from '../../../core/services/message.store';
import { PinStore } from '../../../core/services/pin.store';
import { SavedStore } from '../../../core/services/saved.store';
import { Badge, SidebarNav, Skeleton, VcTooltip } from '../../../shared/ui';

@Component({
  selector: 'vc-channel-list',
  standalone: true,
  imports: [SidebarNav, Skeleton, Badge, VcTooltip],
  template: `
    <div class="channel-list">
      @if (channels.loading()) {
        <div class="channel-list__loading">
          <vc-skeleton height="2rem" />
          <vc-skeleton height="2rem" />
          <vc-skeleton height="2rem" />
        </div>
      } @else {
        <div class="channel-list__shortcuts" [class.channel-list__shortcuts--compact]="navCompact()">
          <button
            type="button"
            class="channel-list__saved"
            [class.channel-list__saved--compact]="navCompact()"
            [class.channel-list__saved--active]="saved.panelOpen()"
            data-testid="saved-nav"
            [attr.aria-label]="navCompact() ? 'Mensagens salvas' : null"
            [vcTooltip]="navCompact() ? 'Mensagens salvas' : null"
            [tooltipDisabled]="!navCompact()"
            position="right"
            (click)="openSaved()"
          >
            <span class="channel-list__saved-icon" aria-hidden="true">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
                <path d="M6 4.5A1.5 1.5 0 0 1 7.5 3h9A1.5 1.5 0 0 1 18 4.5v15.2a.8.8 0 0 1-1.25.66L12 16.6l-4.75 3.76A.8.8 0 0 1 6 19.7V4.5Z" />
              </svg>
            </span>
            @if (!navCompact()) {
              <span class="channel-list__saved-copy">
                <span class="channel-list__saved-label">Salvos</span>
                <span class="channel-list__saved-hint">Vista pessoal</span>
              </span>
            }
            @if (saved.pendingCount() > 0) {
              @if (navCompact()) {
                <span class="channel-list__saved-dot" aria-hidden="true"></span>
              } @else {
                <vc-badge tone="accent">{{ saved.pendingCount() }}</vc-badge>
              }
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
      padding: 0 var(--vc-space-3) var(--vc-space-2);
    }
    .channel-list__shortcuts--compact {
      padding-inline: var(--vc-space-2);
    }
    .channel-list__saved {
      width: 100%;
      display: grid;
      grid-template-columns: auto 1fr auto;
      align-items: center;
      gap: var(--vc-space-2);
      min-height: calc(var(--vc-density-row) + 0.25rem);
      padding: 0.4rem 0.65rem;
      border: 1px solid var(--vc-border);
      border-radius: var(--vc-radius-md);
      background: transparent;
      color: var(--vc-ink);
      text-align: left;
      cursor: pointer;
      position: relative;
      font: inherit;
      transition:
        background var(--vc-dur-fast) var(--vc-ease-out),
        border-color var(--vc-dur-fast) var(--vc-ease-out),
        color var(--vc-dur-fast) var(--vc-ease-out);
    }
    .channel-list__saved:hover {
      border-color: color-mix(in srgb, var(--vc-brand) 45%, var(--vc-border));
      background: color-mix(in srgb, var(--vc-brand) 8%, transparent);
    }
    .channel-list__saved--active {
      border-color: color-mix(in srgb, var(--vc-brand) 55%, var(--vc-border));
      background: color-mix(in srgb, var(--vc-brand) 12%, transparent);
    }
    .channel-list__saved-icon {
      display: inline-grid;
      place-items: center;
      width: 1.35rem;
      height: 1.35rem;
      color: var(--vc-brand);
    }
    .channel-list__saved--active .channel-list__saved-icon svg path {
      fill: var(--vc-brand);
      stroke: var(--vc-brand);
    }
    .channel-list__saved-copy {
      display: grid;
      min-width: 0;
      gap: 0.05rem;
    }
    .channel-list__saved-label {
      font-weight: 600;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .channel-list__saved-hint {
      font-size: 0.7rem;
      color: var(--vc-ink-subtle);
      font-weight: 400;
    }
    .channel-list__saved--compact {
      grid-template-columns: 1fr;
      width: 2.25rem;
      min-height: 2.25rem;
      margin-inline: auto;
      padding: 0;
      justify-items: center;
      border-color: transparent;
      background: transparent;
    }
    .channel-list__saved--compact:hover,
    .channel-list__saved--compact.channel-list__saved--active {
      border-color: transparent;
      background: color-mix(in srgb, var(--vc-brand) 12%, transparent);
    }
    .channel-list__saved-dot {
      position: absolute;
      top: 0.2rem;
      right: 0.2rem;
      width: 0.45rem;
      height: 0.45rem;
      border-radius: 50%;
      background: var(--vc-brand);
      box-shadow: 0 0 0 2px var(--vc-surface-elevated);
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
