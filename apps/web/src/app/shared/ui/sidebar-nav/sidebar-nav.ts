import { Component, input, output } from '@angular/core';
import { Channel, WorkspaceMember } from '../../models/chat.models';
import { ChannelItem } from '../channel-item/channel-item';

@Component({
  selector: 'vc-sidebar-nav',
  standalone: true,
  imports: [ChannelItem],
  template: `
    <nav class="vc-sidebar-nav vc-anim-sidebar" aria-label="Channels">
      <p class="vc-sidebar-nav__label">Channels</p>
      <ul>
        @for (channel of channels(); track channel.id) {
          <li>
            <vc-channel-item
              [channel]="channel"
              [active]="channel.id === activeId()"
              (select)="select.emit(channel.id)"
            />
          </li>
        }
      </ul>

      <p class="vc-sidebar-nav__label vc-sidebar-nav__label--spaced">Mensagens diretas</p>
      <ul>
        @for (channel of directs(); track channel.id) {
          <li>
            <vc-channel-item
              [channel]="channel"
              [active]="channel.id === activeId()"
              (select)="select.emit(channel.id)"
            />
          </li>
        }
      </ul>

      @if (members().length) {
        <p class="vc-sidebar-nav__label vc-sidebar-nav__label--spaced">Membros</p>
        <ul class="vc-sidebar-nav__members">
          @for (member of members(); track member.userId) {
            <li>
              <button type="button" (click)="openDm.emit(member.userId)">
                <span aria-hidden="true">@</span>
                {{ member.displayName }}
              </button>
            </li>
          }
        </ul>
      }
    </nav>
  `,
  styles: `
    .vc-sidebar-nav {
      display: grid;
      gap: 0.35rem;
      padding: 0 var(--vc-space-3);
    }
    .vc-sidebar-nav__label {
      margin: 0 0 var(--vc-space-2);
      padding: 0 var(--vc-space-2);
      font-size: 0.72rem;
      text-transform: uppercase;
      letter-spacing: 0.08em;
      color: var(--vc-ink-subtle);
      font-weight: 600;
    }
    .vc-sidebar-nav__label--spaced {
      margin-top: var(--vc-space-4);
    }
    ul {
      list-style: none;
      margin: 0;
      padding: 0;
      display: grid;
      gap: 0.15rem;
    }
    .vc-sidebar-nav__members button {
      width: 100%;
      display: flex;
      gap: 0.55rem;
      align-items: center;
      min-height: var(--vc-density-row);
      padding: 0 0.7rem;
      border: 0;
      border-radius: var(--vc-radius-md);
      background: transparent;
      color: var(--vc-ink-muted);
      text-align: left;
      cursor: pointer;
      font: inherit;
    }
    .vc-sidebar-nav__members button:hover {
      background: color-mix(in srgb, var(--vc-brand) 10%, transparent);
      color: var(--vc-ink);
    }
  `,
})
export class SidebarNav {
  readonly channels = input.required<Channel[]>();
  readonly directs = input<Channel[]>([]);
  readonly members = input<WorkspaceMember[]>([]);
  readonly activeId = input<string | null>(null);
  readonly select = output<string>();
  readonly openDm = output<string>();
}
