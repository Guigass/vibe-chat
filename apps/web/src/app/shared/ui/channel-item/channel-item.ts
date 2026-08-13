import { Component, computed, input, output } from '@angular/core';
import { Channel, PresenceStatus } from '../../models/chat.models';
import { Badge } from '../badge/badge';
import { VcTooltip } from '../tooltip/tooltip';

@Component({
  selector: 'vc-channel-item',
  standalone: true,
  imports: [Badge, VcTooltip],
  template: `
    <button
      type="button"
      class="vc-channel"
      [class.vc-channel--active]="active()"
      [class.vc-channel--compact]="compact()"
      [attr.aria-label]="compact() ? ariaLabel() : null"
      [vcTooltip]="compact() ? ariaLabel() : null"
      [tooltipDisabled]="!compact()"
      position="right"
      (click)="select.emit()"
    >
      @if (presence()) {
        <span class="vc-channel__presence" [attr.data-status]="presence()" aria-hidden="true"></span>
      } @else {
        <span class="vc-channel__hash" aria-hidden="true">{{ prefix() }}</span>
      }
      <span class="vc-channel__main">
        <span class="vc-channel__name">{{ channel().name }}</span>
        @if (hasDraft()) {
          <span class="vc-channel__draft">Rascunho</span>
        }
      </span>
      @if (channel().mentionCount && channel().mentionCount! > 0) {
        <vc-badge tone="warn">{{ channel().mentionCount }}</vc-badge>
      } @else if (channel().unreadCount > 0) {
        <vc-badge tone="accent">{{ channel().unreadCount }}</vc-badge>
      }
    </button>
  `,
  styles: `
    .vc-channel {
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
      transition:
        background var(--vc-dur-fast) var(--vc-ease-out),
        color var(--vc-dur-fast) var(--vc-ease-out);
    }
    .vc-channel:hover {
      background: color-mix(in srgb, var(--vc-brand) 10%, transparent);
      color: var(--vc-ink);
    }
    .vc-channel--active {
      background: color-mix(in srgb, var(--vc-brand) 16%, transparent);
      color: var(--vc-ink);
    }
    .vc-channel--active::before {
      content: '';
      position: absolute;
      left: 0;
      top: 20%;
      bottom: 20%;
      width: 3px;
      border-radius: 999px;
      background: var(--vc-brand);
    }
    .vc-channel__hash {
      font-family: var(--vc-font-mono);
      opacity: 0.7;
    }
    .vc-channel__presence {
      width: 0.55rem;
      height: 0.55rem;
      border-radius: 50%;
      background: var(--vc-presence-offline);
      justify-self: center;
    }
    .vc-channel__presence[data-status='online'] {
      background: var(--vc-presence-online);
    }
    .vc-channel__presence[data-status='away'] {
      background: var(--vc-presence-away);
    }
    .vc-channel__main {
      display: grid;
      gap: 0.05rem;
      min-width: 0;
    }
    .vc-channel__name {
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
      font-weight: 500;
    }
    .vc-channel__draft {
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
      font-size: 0.72rem;
      font-style: italic;
      color: var(--vc-ink-subtle);
      font-weight: 400;
    }
    .vc-channel--compact {
      grid-template-columns: 1fr;
      justify-items: center;
      padding: 0;
      width: 2.25rem;
      margin-inline: auto;
    }
    .vc-channel--compact .vc-channel__main,
    .vc-channel--compact vc-badge {
      display: none;
    }
    .vc-channel--compact.vc-channel--active::before {
      left: -0.35rem;
    }
    .vc-channel--compact .vc-channel__hash,
    .vc-channel--compact .vc-channel__presence {
      font-size: 0.95rem;
    }
  `,
})
export class ChannelItem {
  readonly channel = input.required<Channel>();
  readonly active = input(false);
  readonly hasDraft = input(false);
  readonly compact = input(false);
  readonly presence = input<PresenceStatus | null>(null);
  readonly select = output<void>();

  readonly ariaLabel = computed(() => {
    const ch = this.channel();
    const parts = [ch.isDirect ? `@${ch.name}` : `#${ch.name}`];
    if (this.hasDraft()) {
      parts.push('rascunho');
    }
    if (ch.mentionCount && ch.mentionCount > 0) {
      parts.push(`${ch.mentionCount} menções`);
    } else if (ch.unreadCount > 0) {
      parts.push(`${ch.unreadCount} não lidas`);
    }
    return parts.join(', ');
  });

  prefix(): string {
    if (this.channel().isDirect) return '@';
    return this.channel().isPrivate ? '◦' : '#';
  }
}
