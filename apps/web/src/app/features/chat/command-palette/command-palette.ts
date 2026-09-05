import { CdkTrapFocus } from '@angular/cdk/a11y';
import { Component, computed, effect, ElementRef, inject, signal, viewChild } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../../../core/auth/auth.service';
import { ChannelStore } from '../../../core/services/channel.store';
import { CommandPaletteService } from '../../../core/services/command-palette.service';
import { MessageStore } from '../../../core/services/message.store';
import { SavedStore } from '../../../core/services/saved.store';
import { ThemeService } from '../../../core/services/theme.service';
import { hasAdminDashboard } from '../../admin/admin-permissions';
import {
  flattenPaletteItems,
  rankPaletteItems,
  SHORTCUT_SHEET,
  SLASH_COMMANDS_NEEDING_ARGS,
  type PaletteItem,
} from '../../../shared/command-palette/palette';
import { SlashCommandsService } from '../composer/slash-commands.service';
import { ui } from '../../../core/i18n/strings';

@Component({
  selector: 'vc-command-palette',
  standalone: true,
  imports: [CdkTrapFocus],
  template: `
    @if (palette.paletteOpen()) {
      <div class="cp-backdrop" (click)="closePalette()"></div>
      <div
        class="cp"
        role="dialog"
        aria-modal="true"
        aria-labelledby="cp-title"
        data-testid="command-palette"
        cdkTrapFocus
        [cdkTrapFocusAutoCapture]="true"
      >
        <h2 id="cp-title" class="cp__title">{{ ui.paletteTitle }}</h2>
        <label class="cp__search">
          <span class="vc-sr-only">{{ ui.paletteFilter }}</span>
          <input
            #queryInput
            type="search"
            [value]="query()"
            (input)="onQuery($event)"
            (keydown)="onQueryKeydown($event)"
            [placeholder]="ui.palettePlaceholder"
            autocomplete="off"
            autofocus
            data-testid="command-palette-query"
          />
        </label>
        <p class="cp__live vc-sr-only" aria-live="polite">{{ liveMessage() }}</p>
        <div class="cp__results" role="listbox" [attr.aria-label]="ui.paletteResults">
          @for (group of groups(); track group.kind) {
            <p class="cp__group">{{ group.title }}</p>
            @for (item of group.items; track item.id) {
              <button
                type="button"
                role="option"
                class="cp__item"
                [class.is-active]="item.id === activeId()"
                [attr.aria-selected]="item.id === activeId()"
                (mousemove)="setActive(item.id)"
                (click)="void run(item.id)"
              >
                <span class="cp__item-main">
                  <span class="cp__item-label">{{ item.label }}</span>
                  @if (item.hint) {
                    <span class="cp__item-hint">{{ item.hint }}</span>
                  }
                </span>
                @if (item.shortcut) {
                  <kbd class="cp__kbd">{{ item.shortcut }}</kbd>
                }
              </button>
            }
          } @empty {
            <p class="cp__empty">{{ ui.paletteEmpty }}</p>
          }
        </div>
        <p class="cp__footer">
          <kbd>↑</kbd><kbd>↓</kbd> navegam · <kbd>Enter</kbd> abre · <kbd>Esc</kbd> fecha ·
          <kbd>?</kbd> atalhos
        </p>
      </div>
    }

    @if (palette.sheetOpen()) {
      <div class="cp-backdrop" (click)="closeSheet()"></div>
      <div
        class="cp cp--sheet"
        role="dialog"
        aria-modal="true"
        aria-labelledby="cp-sheet-title"
        data-testid="shortcut-sheet"
        cdkTrapFocus
        [cdkTrapFocusAutoCapture]="true"
      >
        <header class="cp__sheet-head">
          <h2 id="cp-sheet-title">Atalhos de teclado</h2>
          <button type="button" class="cp__ghost" (click)="closeSheet()" aria-label="Fechar">×</button>
        </header>
        <ul class="cp__sheet-list">
          @for (row of shortcuts; track row.combo) {
            <li>
              <kbd class="cp__kbd">{{ row.combo }}</kbd>
              <span>{{ row.action }}</span>
            </li>
          }
        </ul>
      </div>
    }
  `,
  styles: `
    .cp-backdrop {
      position: fixed;
      inset: 0;
      background: color-mix(in srgb, var(--vc-ink) 45%, transparent);
      z-index: 60;
    }
    .cp {
      position: fixed;
      z-index: 61;
      top: 14vh;
      left: 50%;
      transform: translateX(-50%);
      width: min(36rem, calc(100vw - 2rem));
      max-height: min(32rem, 72vh);
      display: flex;
      flex-direction: column;
      gap: 0.65rem;
      padding: 0.9rem 1rem 0.75rem;
      border-radius: var(--vc-radius-md);
      background: var(--vc-surface-elevated);
      border: 1px solid var(--vc-border);
      box-shadow: 0 16px 48px color-mix(in srgb, var(--vc-ink) 22%, transparent);
      animation: cp-in var(--vc-dur-fast) var(--vc-ease-out);
    }
    .cp--sheet {
      top: 50%;
      transform: translate(-50%, -50%);
      max-height: min(28rem, 80vh);
      animation: none;
    }
    @keyframes cp-in {
      from {
        opacity: 0;
        transform: translateX(-50%) translateY(-0.4rem);
      }
      to {
        opacity: 1;
        transform: translateX(-50%) translateY(0);
      }
    }
    @media (prefers-reduced-motion: reduce) {
      .cp {
        animation: none;
      }
    }
    .cp__title,
    .cp__sheet-head h2 {
      margin: 0;
      font-family: var(--vc-font-display);
      font-size: 1.05rem;
    }
    .cp__sheet-head {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 0.5rem;
    }
    .cp__search input {
      width: 100%;
      box-sizing: border-box;
      min-height: 2.5rem;
      padding: 0.55rem 0.8rem;
      border-radius: var(--vc-radius-md);
      border: 1px solid var(--vc-border);
      background: var(--vc-surface);
      color: var(--vc-ink);
      font: inherit;
    }
    .cp__results {
      overflow: auto;
      min-height: 8rem;
      max-height: 20rem;
    }
    .cp__group {
      margin: 0.55rem 0 0.25rem;
      font-size: 0.75rem;
      letter-spacing: 0.04em;
      text-transform: uppercase;
      color: var(--vc-ink-muted);
    }
    .cp__item {
      width: 100%;
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 0.75rem;
      padding: 0.5rem 0.6rem;
      border: 0;
      border-radius: var(--vc-radius-sm);
      background: transparent;
      color: inherit;
      font: inherit;
      text-align: left;
      cursor: pointer;
    }
    .cp__item.is-active,
    .cp__item:hover {
      background: color-mix(in srgb, var(--vc-brand) 14%, transparent);
    }
    .cp__item-main {
      display: grid;
      gap: 0.1rem;
    }
    .cp__item-hint {
      font-size: 0.8rem;
      color: var(--vc-ink-muted);
    }
    .cp__kbd {
      font-family: var(--vc-font-mono);
      font-size: 0.72rem;
      padding: 0.1rem 0.35rem;
      border: 1px solid var(--vc-border);
      border-radius: 4px;
      color: var(--vc-ink-muted);
      white-space: nowrap;
    }
    .cp__empty,
    .cp__footer {
      margin: 0;
      color: var(--vc-ink-muted);
      font-size: 0.82rem;
    }
    .cp__sheet-list {
      list-style: none;
      margin: 0;
      padding: 0;
      display: grid;
      gap: 0.55rem;
      overflow: auto;
    }
    .cp__sheet-list li {
      display: flex;
      justify-content: space-between;
      gap: 1rem;
      align-items: baseline;
    }
    .cp__ghost {
      border: 0;
      background: transparent;
      color: inherit;
      font-size: 1.25rem;
      cursor: pointer;
    }
  `,
})
export class CommandPalette {
  readonly ui = ui;
  readonly palette = inject(CommandPaletteService);
  private readonly channels = inject(ChannelStore);
  private readonly messages = inject(MessageStore);
  private readonly saved = inject(SavedStore);
  private readonly slash = inject(SlashCommandsService);
  private readonly theme = inject(ThemeService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly query = signal('');
  readonly activeId = signal<string | null>(null);
  readonly shortcuts = SHORTCUT_SHEET;
  private readonly queryInput = viewChild<ElementRef<HTMLInputElement>>('queryInput');
  private slashItems = signal<PaletteItem[]>([]);

  readonly sourceItems = computed(() => this.buildItems());
  readonly groups = computed(() =>
    rankPaletteItems(this.sourceItems(), this.query(), this.palette.recentIds()),
  );
  readonly flatItems = computed(() => flattenPaletteItems(this.groups()));
  readonly liveMessage = computed(() => {
    const n = this.flatItems().length;
    if (!n) return 'Nenhum resultado';
    return n === 1 ? '1 resultado' : `${n} resultados`;
  });

  constructor() {
    effect(() => {
      if (!this.palette.paletteOpen()) return;
      this.query.set('');
      this.palette.hydrateRecents(this.auth.profile()?.id);
      void this.refreshSlashItems();
    });

    effect(() => {
      if (!this.palette.paletteOpen()) return;
      const input = this.queryInput()?.nativeElement;
      if (!input) return;
      queueMicrotask(() => input.focus());
    });

    effect(() => {
      const first = this.flatItems()[0];
      const current = this.activeId();
      if (!current || !this.flatItems().some((item) => item.id === current)) {
        this.activeId.set(first?.id ?? null);
      }
    });
  }

  onQuery(event: Event): void {
    this.query.set((event.target as HTMLInputElement).value);
  }

  setActive(id: string): void {
    this.activeId.set(id);
  }

  onQueryKeydown(event: KeyboardEvent): void {
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault();
      this.move(event.key === 'ArrowDown' ? 1 : -1);
      return;
    }
    if (event.key === 'Enter') {
      event.preventDefault();
      const id = this.activeId();
      if (id) void this.run(id);
      return;
    }
    if (event.key === 'Escape') {
      event.preventDefault();
      this.closePalette();
    }
  }

  closePalette(): void {
    this.palette.closePalette();
  }

  closeSheet(): void {
    this.palette.closeShortcutSheet();
  }

  async run(id: string): Promise<void> {
    const item = this.flatItems().find((row) => row.id === id);
    if (!item) return;
    const userId = this.auth.profile()?.id;
    this.palette.rememberRecent(userId, item.id);
    this.palette.closePalette({ restoreFocus: false });
    await this.execute(item);
  }

  private move(direction: 1 | -1): void {
    const items = this.flatItems();
    if (!items.length) return;
    const index = items.findIndex((item) => item.id === this.activeId());
    const next = index < 0 ? 0 : (index + direction + items.length) % items.length;
    this.activeId.set(items[next]?.id ?? null);
  }

  private async refreshSlashItems(): Promise<void> {
    const workspaceId = this.channels.activeWorkspace()?.id;
    if (!workspaceId) {
      this.slashItems.set([]);
      return;
    }
    const commands = await this.slash.listCommands(workspaceId);
    this.slashItems.set(
      commands.map((command) => ({
        id: `slash:${command.name}`,
        kind: 'action' as const,
        label: `/${command.name}`,
        hint: command.description,
        keywords: [command.name, command.usage, command.description],
        action: { type: 'slash', name: command.name },
      })),
    );
  }

  private buildItems(): PaletteItem[] {
    const items: PaletteItem[] = [];
    for (const channel of this.channels.publicChannels()) {
      items.push({
        id: `channel:${channel.id}`,
        kind: 'channel',
        label: `#${channel.name}`,
        hint: channel.description,
        keywords: [channel.name, channel.description ?? ''],
        action: { type: 'channel', channelId: channel.id },
      });
    }
    for (const member of this.channels.peerCandidates()) {
      items.push({
        id: `person:${member.userId}`,
        kind: 'person',
        label: member.displayName,
        hint: member.email,
        keywords: [member.displayName, member.email],
        action: { type: 'person', userId: member.userId },
      });
    }

    items.push(
      {
        id: 'action:search',
        kind: 'action',
        label: 'Buscar mensagens',
        shortcut: 'Ctrl/Cmd+Shift+F',
        keywords: ['busca', 'search', 'filtros'],
        action: { type: 'search' },
      },
      {
        id: 'action:saved',
        kind: 'action',
        label: 'Ir para Salvos',
        keywords: ['salvos', 'saved'],
        action: { type: 'saved' },
      },
      {
        id: 'action:theme',
        kind: 'action',
        label: this.theme.theme() === 'dark' ? 'Ativar tema claro' : 'Ativar tema escuro',
        keywords: ['tema', 'theme', 'dark', 'light'],
        action: { type: 'theme' },
      },
      {
        id: 'action:density',
        kind: 'action',
        label:
          this.theme.density() === 'compact' ? 'Densidade confortável' : 'Densidade compacta',
        keywords: ['densidade', 'compacto'],
        action: { type: 'density' },
      },
      {
        id: 'action:mentions',
        kind: 'action',
        label: 'Ir para menções',
        shortcut: 'Ctrl/Cmd+Shift+M',
        keywords: ['menções', 'mentions'],
        action: { type: 'mentions' },
      },
      {
        id: 'action:read',
        kind: 'action',
        label: 'Marcar canal como lido',
        shortcut: 'Shift+Esc',
        keywords: ['lido', 'read'],
        action: { type: 'mark-read' },
      },
      {
        id: 'action:shortcuts',
        kind: 'action',
        label: 'Folha de atalhos',
        shortcut: '?',
        keywords: ['atalhos', 'ajuda', 'shortcuts'],
        action: { type: 'shortcuts' },
      },
    );

    if (this.channels.workspaces().some((workspace) => hasAdminDashboard(workspace.role))) {
      items.push({
        id: 'action:admin',
        kind: 'action',
        label: 'Ir para Admin',
        keywords: ['admin', 'administração'],
        action: { type: 'admin' },
      });
    }

    items.push(...this.slashItems());
    return items;
  }

  private async execute(item: PaletteItem): Promise<void> {
    const action = item.action;
    switch (action.type) {
      case 'channel':
        await this.openChannel(action.channelId);
        return;
      case 'person': {
        const channel = await this.channels.openDirectMessage(action.userId);
        if (channel) await this.messages.loadChannel(channel.id);
        this.palette.requestComposerFocus();
        return;
      }
      case 'slash':
        await this.runSlash(action.name);
        return;
      case 'saved':
        this.saved.openPanel();
        return;
      case 'admin':
        void this.router.navigateByUrl('/admin');
        return;
      case 'theme':
        this.theme.toggleTheme();
        this.palette.requestComposerFocus();
        return;
      case 'density':
        this.theme.toggleDensity();
        this.palette.requestComposerFocus();
        return;
      case 'shortcuts':
        this.palette.openShortcutSheet();
        return;
      case 'search':
        this.focusSearch();
        return;
      case 'mentions':
        await this.goToMention();
        return;
      case 'mark-read':
        await this.messages.markActiveChannelRead();
        this.palette.requestComposerFocus();
        return;
    }
  }

  private async openChannel(channelId: string): Promise<void> {
    this.channels.selectChannel(channelId);
    await this.messages.loadChannel(channelId);
    this.palette.requestComposerFocus();
  }

  private async runSlash(name: string): Promise<void> {
    if (name === 'ajuda') {
      this.palette.openShortcutSheet();
      return;
    }
    if (SLASH_COMMANDS_NEEDING_ARGS.has(name)) {
      this.channels.prefillComposer(`/${name} `);
      this.palette.requestComposerFocus();
      return;
    }
    await this.slash.execute(`/${name}`);
    this.palette.requestComposerFocus();
  }

  private focusSearch(): void {
    const el = document.getElementById('vc-search') as HTMLInputElement | null;
    el?.focus();
  }

  async goToMention(): Promise<void> {
    const next = this.channels
      .channels()
      .find(
        (channel) =>
          (channel.mentionCount ?? 0) > 0 &&
          channel.id !== this.channels.activeChannelId(),
      ) ?? this.channels.channels().find((channel) => (channel.mentionCount ?? 0) > 0);
    if (!next) return;
    await this.openChannel(next.id);
  }
}
