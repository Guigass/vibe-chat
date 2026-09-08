import { Component, HostListener, computed, effect, input, output, signal } from '@angular/core';
import {
  Channel,
  ChannelMuteAction,
  PresenceStatus,
  Space,
  SpaceGroup,
  WorkspaceMember,
} from '../../models/chat.models';
import { ChannelItem } from '../channel-item/channel-item';
import { Button } from '../button/button';
import { Input } from '../input/input';
import { VcTooltip } from '../tooltip/tooltip';

function matchesFilter(text: string, query: string): boolean {
  if (!query) return true;
  return text.toLowerCase().includes(query);
}

@Component({
  selector: 'vc-sidebar-nav',
  standalone: true,
  imports: [ChannelItem, Button, Input, VcTooltip],
  template: `
    <nav
      class="vc-sidebar-nav vc-anim-sidebar"
      [class.vc-sidebar-nav--compact]="compact()"
      aria-label="Spaces e channels"
    >
      @if (!compact()) {
        <div class="vc-sidebar-nav__filter">
          <vc-input
            controlId="vc-nav-filter"
            ariaLabel="Filtrar canais, recentes e membros"
            placeholder="Filtrar canais, recentes e membros…"
            [(value)]="filterQuery"
          />
        </div>
      }

      @if (hasFilter() && isEmpty()) {
        <p class="vc-sidebar-nav__empty" role="status">Nenhum resultado para “{{ filterQuery().trim() }}”.</p>
      }

      @if (filteredGroups().length) {
        <section class="vc-sidebar-nav__block" aria-label="Canais">
          @for (group of filteredGroups(); track group.space?.id ?? 'ungrouped') {
            <div class="vc-sidebar-nav__space">
              @if (!compact()) {
                <p class="vc-sidebar-nav__label">{{ group.space?.name ?? 'Outros' }}</p>
              }
              <ul>
                @for (channel of group.channels; track channel.id) {
                  <li>
                    <vc-channel-item
                      [channel]="channel"
                      [active]="channel.id === activeId()"
                      [hasDraft]="draftIds().has(channel.id)"
                      [compact]="compact()"
                      [muted]="mutedIds().has(channel.id)"
                      [channelHasOverride]="overrideIds().has(channel.id)"
                      (select)="select.emit(channel.id)"
                      (muteAction)="channelMuteAction.emit({ channelId: channel.id, action: $event })"
                    />
                  </li>
                }
              </ul>
            </div>
          }

          @if (canCreate() && !hasFilter() && !compact()) {
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
        </section>
      }

      @if (filteredDirects().length) {
        <section class="vc-sidebar-nav__block" aria-label="Recentes">
          @if (!compact()) {
            <p class="vc-sidebar-nav__label">Recentes</p>
          }
          <ul>
            @for (channel of filteredDirects(); track channel.id) {
              <li>
                <vc-channel-item
                  [channel]="channel"
                  [active]="channel.id === activeId()"
                  [hasDraft]="draftIds().has(channel.id)"
                  [compact]="compact()"
                  [presence]="presenceOf(channel.peerUserId)"
                  [muted]="mutedIds().has(channel.id)"
                  [channelHasOverride]="overrideIds().has(channel.id)"
                  (select)="select.emit(channel.id)"
                  (muteAction)="channelMuteAction.emit({ channelId: channel.id, action: $event })"
                />
              </li>
            }
          </ul>
        </section>
      }

      @if (filteredMembers().length) {
        <section class="vc-sidebar-nav__block" aria-label="Membros">
          @if (!compact()) {
            <p class="vc-sidebar-nav__label">Membros</p>
            <button
              type="button"
              class="vc-sidebar-nav__create-toggle"
              data-testid="group-dm-picker-toggle"
              (click)="pickerOpen.set(!pickerOpen())"
            >
              {{ pickerOpen() ? 'Cancelar conversa' : 'Nova conversa' }}
            </button>
            @if (pickerOpen()) {
              <div class="vc-sidebar-nav__picker" data-testid="group-dm-picker">
                @if (selectedIds().length) {
                  <div class="vc-sidebar-nav__chips">
                    @for (member of selectedMembers(); track member.userId) {
                      <button type="button" class="vc-sidebar-nav__chip" (click)="togglePick(member.userId)">
                        {{ member.displayName }}
                        <span aria-hidden="true">×</span>
                      </button>
                    }
                  </div>
                }
                <p class="vc-sidebar-nav__picker-hint">
                  {{ selectedIds().length >= 2 ? 'DM em grupo' : 'Selecione 2 ou mais para um grupo' }}
                </p>
                <vc-button
                  type="button"
                  data-testid="group-dm-open"
                  [disabled]="selectedIds().length === 0"
                  (click)="confirmPicker()"
                >
                  Abrir conversa
                </vc-button>
              </div>
            }
          }
          <ul class="vc-sidebar-nav__members">
            @for (member of filteredMembers(); track member.userId) {
              <li>
                <button
                  type="button"
                  class="vc-sidebar-nav__member"
                  [class.vc-sidebar-nav__member--compact]="compact()"
                  [attr.aria-label]="memberLabel(member)"
                  [vcTooltip]="compact() ? memberLabel(member) : null"
                  [tooltipDisabled]="!compact()"
                  position="right"
                  (click)="onMemberClick(member.userId)"
                >
                  @if (compact()) {
                    <span class="vc-sidebar-nav__member-initial" aria-hidden="true">
                      {{ memberInitial(member) }}
                    </span>
                  } @else {
                    <span
                      class="vc-presence"
                      [attr.data-status]="presenceOf(member.userId)"
                      aria-hidden="true"
                    ></span>
                    <span aria-hidden="true">@</span>
                    {{ member.displayName }}
                  }
                </button>
              </li>
            }
          </ul>
        </section>
      }
    </nav>
  `,
  styles: `
    .vc-sidebar-nav {
      display: grid;
      gap: var(--vc-space-2);
      padding: 0 var(--vc-space-3);
    }
    .vc-sidebar-nav--compact {
      padding-inline: var(--vc-space-2);
      gap: var(--vc-space-2);
    }
    .vc-sidebar-nav__filter {
      position: sticky;
      top: 0;
      z-index: 1;
      background: inherit;
    }
    .vc-sidebar-nav__empty {
      margin: 0;
      padding: var(--vc-space-2);
      font-size: 0.82rem;
      color: var(--vc-ink-muted);
      text-align: center;
    }
    .vc-sidebar-nav__block {
      display: grid;
      gap: 0.15rem;
      padding-bottom: var(--vc-space-2);
      border-bottom: 1px solid color-mix(in srgb, var(--vc-border) 70%, transparent);
    }
    .vc-sidebar-nav__block:last-child {
      border-bottom: 0;
      padding-bottom: 0;
    }
    .vc-sidebar-nav__space {
      display: grid;
      gap: 0.15rem;
    }
    .vc-sidebar-nav__label {
      margin: 0;
      padding: 0.15rem var(--vc-space-2) 0;
      font-size: 0.72rem;
      text-transform: uppercase;
      letter-spacing: 0.08em;
      color: var(--vc-ink-muted);
      font-weight: 700;
    }
    ul {
      list-style: none;
      margin: 0;
      padding: 0;
      display: grid;
      gap: 0.15rem;
    }
    .vc-sidebar-nav__create {
      margin-top: var(--vc-space-1);
    }
    .vc-sidebar-nav__create-toggle {
      width: 100%;
      border: 0;
      background: transparent;
      color: var(--vc-ink-muted);
      text-align: left;
      padding: 0.35rem 0.7rem;
      border-radius: var(--vc-radius-md);
      cursor: pointer;
      font: inherit;
      font-size: 0.86rem;
    }
    .vc-sidebar-nav__create-toggle:hover {
      background: color-mix(in srgb, var(--vc-brand) 10%, transparent);
      color: var(--vc-ink);
    }
    .vc-sidebar-nav__form {
      display: grid;
      gap: var(--vc-space-2);
      padding: var(--vc-space-2);
      border: 1px solid var(--vc-border);
      border-radius: var(--vc-radius-md);
      background: var(--vc-surface);
    }
    .vc-sidebar-nav__select {
      display: grid;
      gap: 0.25rem;
      font-size: 0.82rem;
      color: var(--vc-ink-muted);
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
    .vc-sidebar-nav__member {
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
    .vc-sidebar-nav__member:hover {
      background: color-mix(in srgb, var(--vc-brand) 10%, transparent);
      color: var(--vc-ink);
    }
    .vc-sidebar-nav__member--compact {
      width: 2.25rem;
      padding: 0;
      justify-content: center;
      margin-inline: auto;
    }
    .vc-sidebar-nav__member-initial {
      width: 1.75rem;
      height: 1.75rem;
      display: grid;
      place-items: center;
      border-radius: var(--vc-radius-md);
      background: color-mix(in srgb, var(--vc-brand) 14%, transparent);
      color: var(--vc-brand-ink);
      font-size: 0.78rem;
      font-weight: 700;
      text-transform: uppercase;
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
    .vc-sidebar-nav__picker {
      display: grid;
      gap: var(--vc-space-2);
      padding: var(--vc-space-2);
      border: 1px solid var(--vc-border);
      border-radius: var(--vc-radius-md);
    }
    .vc-sidebar-nav__chips {
      display: flex;
      flex-wrap: wrap;
      gap: 0.35rem;
    }
    .vc-sidebar-nav__chip {
      border: 1px solid var(--vc-border);
      border-radius: 999px;
      background: color-mix(in srgb, var(--vc-brand) 10%, transparent);
      color: var(--vc-ink);
      font: inherit;
      font-size: 0.78rem;
      padding: 0.15rem 0.55rem;
      cursor: pointer;
    }
    .vc-sidebar-nav__picker-hint {
      margin: 0;
      font-size: 0.72rem;
      color: var(--vc-ink-subtle);
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
  readonly draftIds = input<ReadonlySet<string>>(new Set());
  readonly mutedIds = input<ReadonlySet<string>>(new Set());
  readonly overrideIds = input<ReadonlySet<string>>(new Set());
  readonly canCreate = input(false);
  readonly compact = input(false);
  readonly select = output<string>();
  readonly openDm = output<string>();
  readonly openGroup = output<string[]>();
  readonly channelMuteAction = output<{ channelId: string; action: ChannelMuteAction }>();
  readonly createChannel = output<{
    name: string;
    type: string;
    spaceId?: string | null;
    newSpaceName?: string;
  }>();

  readonly filterQuery = signal('');
  readonly filterOpen = signal(false);
  readonly createOpen = signal(false);
  readonly creating = signal(false);
  readonly createError = signal<string | null>(null);
  readonly channelName = signal('');
  readonly newSpaceName = signal('');
  readonly selectedSpaceId = signal('');
  readonly channelType = signal('Public');
  readonly pickerOpen = signal(false);
  readonly selectedIds = signal<string[]>([]);
  readonly selectedMembers = computed(() =>
    this.members().filter((m) => this.selectedIds().includes(m.userId)),
  );

  readonly hasFilter = computed(() => this.filterQuery().trim().length > 0);

  readonly filteredGroups = computed(() => {
    const q = this.filterQuery().trim().toLowerCase();
    return this.groups()
      .map((group) => ({
        ...group,
        channels: group.channels.filter((ch) => matchesFilter(ch.name, q)),
      }))
      .filter((group) => group.channels.length > 0);
  });

  readonly filteredDirects = computed(() => {
    const q = this.filterQuery().trim().toLowerCase();
    return this.directs().filter((ch) => matchesFilter(ch.name, q));
  });

  readonly filteredMembers = computed(() => {
    const q = this.filterQuery().trim().toLowerCase();
    return this.members().filter((m) => matchesFilter(m.displayName, q));
  });

  readonly isEmpty = computed(
    () =>
      this.filteredGroups().length === 0
      && this.filteredDirects().length === 0
      && this.filteredMembers().length === 0,
  );

  constructor() {
    effect(() => {
      if (this.compact()) {
        this.filterQuery.set('');
        this.filterOpen.set(false);
        this.cancelCreate();
      }
    });
  }

  @HostListener('window:keydown', ['$event'])
  onGlobalKeydown(event: KeyboardEvent): void {
    if (event.key !== 'Escape' || !this.hasFilter()) {
      return;
    }
    const target = event.target as HTMLElement | null;
    if (target?.id === 'vc-nav-filter') {
      this.clearFilter(event);
    }
  }

  presenceOf(userId: string | undefined): PresenceStatus {
    if (!userId) return 'offline';
    return this.presence()[userId] ?? 'offline';
  }

  memberInitial(member: WorkspaceMember): string {
    const trimmed = member.displayName.trim();
    return trimmed ? trimmed.charAt(0) : '?';
  }

  memberLabel(member: WorkspaceMember): string {
    return `Mensagem para ${member.displayName}`;
  }

  onMemberClick(userId: string): void {
    if (this.pickerOpen()) {
      this.togglePick(userId);
      return;
    }
    this.openDm.emit(userId);
  }

  togglePick(userId: string): void {
    this.selectedIds.update((ids) =>
      ids.includes(userId) ? ids.filter((id) => id !== userId) : [...ids, userId],
    );
  }

  confirmPicker(): void {
    const ids = this.selectedIds();
    if (ids.length >= 2) {
      this.openGroup.emit(ids);
    } else if (ids.length === 1) {
      this.openDm.emit(ids[0]);
    }
    this.pickerOpen.set(false);
    this.selectedIds.set([]);
  }

  clearFilter(event?: Event): void {
    event?.preventDefault();
    event?.stopPropagation();
    this.filterQuery.set('');
    this.filterOpen.set(false);
    (document.getElementById('vc-nav-filter') as HTMLInputElement | null)?.blur();
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
