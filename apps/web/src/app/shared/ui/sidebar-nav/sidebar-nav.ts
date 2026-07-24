import { Component, input, output, signal } from '@angular/core';
import {
  Channel,
  PresenceStatus,
  Space,
  SpaceGroup,
  WorkspaceMember,
} from '../../models/chat.models';
import { ChannelItem } from '../channel-item/channel-item';
import { Button } from '../button/button';
import { Input } from '../input/input';

@Component({
  selector: 'vc-sidebar-nav',
  standalone: true,
  imports: [ChannelItem, Button, Input],
  template: `
    <nav class="vc-sidebar-nav vc-anim-sidebar" aria-label="Spaces e channels">
      @for (group of groups(); track group.space?.id ?? 'ungrouped') {
        <section class="vc-sidebar-nav__space">
          <p class="vc-sidebar-nav__label">{{ group.space?.name ?? 'Outros' }}</p>
          <ul>
            @for (channel of group.channels; track channel.id) {
              <li>
                <vc-channel-item
                  [channel]="channel"
                  [active]="channel.id === activeId()"
                  (select)="select.emit(channel.id)"
                />
              </li>
            }
          </ul>
        </section>
      }

      @if (canCreate()) {
        <div class="vc-sidebar-nav__create">
          @if (!createOpen()) {
            <button type="button" class="vc-sidebar-nav__create-toggle" (click)="createOpen.set(true)">
              Novo channel
            </button>
          } @else {
            <form class="vc-sidebar-nav__form" (submit)="submitCreate($event)">
              <vc-input
                controlId="vc-create-channel"
                label="Nome do channel"
                placeholder="ex.: roadmap"
                [(value)]="channelName"
              />
              <label class="vc-sidebar-nav__select">
                <span>Space</span>
                <select [value]="selectedSpaceId()" (change)="onSpaceChange($event)">
                  <option value="">Sem space</option>
                  @for (space of spaces(); track space.id) {
                    <option [value]="space.id">{{ space.name }}</option>
                  }
                  <option value="__new__">Novo space…</option>
                </select>
              </label>
              @if (selectedSpaceId() === '__new__') {
                <vc-input
                  controlId="vc-create-space"
                  label="Nome do space"
                  placeholder="ex.: Produto"
                  [(value)]="newSpaceName"
                />
              }
              <label class="vc-sidebar-nav__select">
                <span>Tipo</span>
                <select [value]="channelType()" (change)="onTypeChange($event)">
                  <option value="Public">Público</option>
                  <option value="Private">Privado</option>
                </select>
              </label>
              @if (createError()) {
                <p class="vc-sidebar-nav__error" role="alert">{{ createError() }}</p>
              }
              <div class="vc-sidebar-nav__form-actions">
                <vc-button type="submit" [loading]="creating()" [disabled]="creating()">Criar</vc-button>
                <vc-button type="button" variant="ghost" (click)="cancelCreate()">Cancelar</vc-button>
              </div>
            </form>
          }
        </div>
      }

      <p class="vc-sidebar-nav__label vc-sidebar-nav__label--spaced">Mensagens diretas</p>
      <ul>
        @for (channel of directs(); track channel.id) {
          <li>
            <vc-channel-item
              [channel]="channel"
              [active]="channel.id === activeId()"
              [presence]="presenceOf(channel.peerUserId)"
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
                <span
                  class="vc-presence"
                  [attr.data-status]="presenceOf(member.userId)"
                  aria-hidden="true"
                ></span>
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
    .vc-sidebar-nav__space {
      display: grid;
      gap: 0.15rem;
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
    .vc-sidebar-nav__create {
      margin-top: var(--vc-space-2);
      padding: 0 var(--vc-space-2);
    }
    .vc-sidebar-nav__create-toggle {
      border: 0;
      background: transparent;
      color: var(--vc-brand);
      font: inherit;
      font-weight: 600;
      cursor: pointer;
      padding: 0.35rem 0;
      text-align: left;
    }
    .vc-sidebar-nav__form {
      display: grid;
      gap: var(--vc-space-2);
      padding: var(--vc-space-3) 0;
      border-top: 1px solid var(--vc-border);
      border-bottom: 1px solid var(--vc-border);
    }
    .vc-sidebar-nav__select {
      display: grid;
      gap: 0.35rem;
      font-size: 0.85rem;
      color: var(--vc-ink-muted);
      font-weight: 500;
    }
    .vc-sidebar-nav__select select {
      min-height: 2.5rem;
      padding: 0.55rem 0.8rem;
      border-radius: var(--vc-radius-md);
      border: 1px solid var(--vc-border);
      background: var(--vc-surface-elevated);
      color: var(--vc-ink);
      font: inherit;
    }
    .vc-sidebar-nav__form-actions {
      display: flex;
      gap: var(--vc-space-2);
      flex-wrap: wrap;
    }
    .vc-sidebar-nav__error {
      margin: 0;
      color: var(--vc-danger);
      font-size: 0.85rem;
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
    .vc-presence {
      width: 0.55rem;
      height: 0.55rem;
      border-radius: 50%;
      background: var(--vc-presence-offline);
      flex-shrink: 0;
    }
    .vc-presence[data-status='online'] {
      background: var(--vc-presence-online);
    }
    .vc-presence[data-status='away'] {
      background: var(--vc-presence-away);
    }
  `,
})
export class SidebarNav {
  readonly groups = input.required<SpaceGroup[]>();
  readonly spaces = input<Space[]>([]);
  readonly directs = input<Channel[]>([]);
  readonly members = input<WorkspaceMember[]>([]);
  readonly presence = input<Record<string, PresenceStatus>>({});
  readonly activeId = input<string | null>(null);
  readonly canCreate = input(false);
  readonly select = output<string>();
  readonly openDm = output<string>();
  readonly createChannel = output<{
    name: string;
    type: string;
    spaceId?: string | null;
    newSpaceName?: string;
  }>();

  readonly createOpen = signal(false);
  readonly creating = signal(false);
  readonly createError = signal<string | null>(null);
  readonly channelName = signal('');
  readonly newSpaceName = signal('');
  readonly selectedSpaceId = signal('');
  readonly channelType = signal('Public');

  presenceOf(userId: string | undefined): PresenceStatus {
    if (!userId) return 'offline';
    return this.presence()[userId] ?? 'offline';
  }

  onSpaceChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.selectedSpaceId.set(value);
  }

  onTypeChange(event: Event): void {
    this.channelType.set((event.target as HTMLSelectElement).value);
  }

  cancelCreate(): void {
    this.createOpen.set(false);
    this.createError.set(null);
    this.channelName.set('');
    this.newSpaceName.set('');
    this.selectedSpaceId.set('');
    this.channelType.set('Public');
  }

  async submitCreate(event: Event): Promise<void> {
    event.preventDefault();
    const name = this.channelName().trim();
    if (!name) {
      this.createError.set('Informe o nome do channel.');
      return;
    }
    if (this.selectedSpaceId() === '__new__' && !this.newSpaceName().trim()) {
      this.createError.set('Informe o nome do novo space.');
      return;
    }

    this.creating.set(true);
    this.createError.set(null);
    try {
      this.createChannel.emit({
        name,
        type: this.channelType(),
        spaceId: this.selectedSpaceId() === '__new__' || !this.selectedSpaceId()
          ? null
          : this.selectedSpaceId(),
        newSpaceName: this.selectedSpaceId() === '__new__' ? this.newSpaceName().trim() : undefined,
      });
      this.cancelCreate();
    } catch (err) {
      this.createError.set(err instanceof Error ? err.message : 'Falha ao criar channel');
    } finally {
      this.creating.set(false);
    }
  }
}
