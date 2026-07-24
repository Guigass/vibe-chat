import { Component, input, output } from '@angular/core';
import { Channel } from '../../models/chat.models';
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
    ul {
      list-style: none;
      margin: 0;
      padding: 0;
      display: grid;
      gap: 0.15rem;
    }
  `,
})
export class SidebarNav {
  readonly channels = input.required<Channel[]>();
  readonly activeId = input<string | null>(null);
  readonly select = output<string>();
}
