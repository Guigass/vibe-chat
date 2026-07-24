import { Component, input, output } from '@angular/core';
import { Channel } from '../../models/chat.models';
import { Badge } from '../badge/badge';

@Component({
  selector: 'vc-channel-item',
  standalone: true,
  imports: [Badge],
  template: `
    <button
      type="button"
      class="vc-channel"
      [class.vc-channel--active]="active()"
      (click)="select.emit()"
    >
      <span class="vc-channel__hash" aria-hidden="true">{{ prefix() }}</span>
      <span class="vc-channel__name">{{ channel().name }}</span>
      @if (channel().unreadCount > 0) {
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
      gap: 0.55rem;
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
    .vc-channel__name {
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
      font-weight: 500;
    }
  `,
})
export class ChannelItem {
  readonly channel = input.required<Channel>();
  readonly active = input(false);
  readonly select = output<void>();

  prefix(): string {
    if (this.channel().isDirect) return '@';
    return this.channel().isPrivate ? '◦' : '#';
  }
}
