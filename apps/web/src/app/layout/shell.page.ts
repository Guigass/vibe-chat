import {
  Component,
  DestroyRef,
  HostListener,
  OnDestroy,
  OnInit,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { RouterLink, RouterLinkActive, ActivatedRoute } from '@angular/router';
import { AuthService } from '../core/auth/auth.service';
import { ApiService } from '../core/api/api.service';
import { ChannelStore } from '../core/services/channel.store';
import { ChatHubService } from '../core/services/chat-hub.service';
import { MessageStore } from '../core/services/message.store';
import { ThreadStore } from '../core/services/thread.store';
import { PinStore } from '../core/services/pin.store';
import { SavedStore } from '../core/services/saved.store';
import { PushNotificationService } from '../core/services/push-notification.service';
import { NotificationPreferencesStore } from '../core/services/notification-preferences.store';
import { ChannelList } from '../features/chat/channel-list/channel-list';
import { Composer } from '../features/chat/composer/composer';
import { Timeline } from '../features/chat/timeline/timeline';
import { PinsPanel } from '../features/chat/pins-panel/pins-panel';
import { SavedPanel } from '../features/chat/saved-panel/saved-panel';
import { ThreadPanel } from '../features/chat/thread-panel/thread-panel';
import { NotificationPreferencesPanel } from '../features/chat/notification-preferences-panel/notification-preferences-panel';
import { SuggestReplyButton } from '../features/ai/suggest-reply-button';
import { SummarizeButton } from '../features/ai/summarize-button';
import { SearchMessageHit, WorkspaceMember } from '../shared/models/chat.models';
import {
  applySearchOperator,
  hasSearchFilter,
  highlightSearchParts,
  parseSearchQuery,
  removeSearchChip,
  type SearchChip,
  type SearchSort,
} from '../shared/search/search-query';
import { readRecentSearches, writeRecentSearch } from '../shared/search/search-recent';
import { CommandPalette } from '../features/chat/command-palette/command-palette';
import { CommandPaletteService } from '../core/services/command-palette.service';
import {
  cycleChannel,
  isEditableTarget,
  matchGlobalShortcut,
} from '../shared/command-palette/palette';
import {
  ConnectionBanner,
  DensityControl,
  IconButton,
  Input,
  ThemeToggle,
  UpdateBanner,
  PushOptInBanner,
  PushDevicesControl,
  InAppNoticeBanner,
  VcTooltip,
  provideVcTooltipDefaults,
  provideVcTooltipGroup,
} from '../shared/ui';
import { AttachmentQueueService } from '../features/chat/composer/attachment-queue.service';
import { collectFilesFromDataTransfer } from '../features/chat/composer/attachment-upload';
import { hasAdminDashboard } from '../features/admin/admin-permissions';
import { defaultSidebarOpen, readNavCompact, SHELL_NARROW_MEDIA_QUERY, writeNavCompact } from './shell-viewport';

@Component({
  selector: 'vc-shell-page',
  standalone: true,
  imports: [
    RouterLink,
    RouterLinkActive,
    ChannelList,
    Timeline,
    Composer,
    ThreadPanel,
    PinsPanel,
    SavedPanel,
    NotificationPreferencesPanel,
    SummarizeButton,
    SuggestReplyButton,
    ConnectionBanner,
    UpdateBanner,
    PushOptInBanner,
    PushDevicesControl,
    InAppNoticeBanner,
    ThemeToggle,
    DensityControl,
    IconButton,
    Input,
    VcTooltip,
    CommandPalette,
  ],
  providers: [provideVcTooltipDefaults(), ...provideVcTooltipGroup()],
  templateUrl: './shell.page.html',
  styleUrl: './shell.page.scss',
})
export class ShellPage implements OnInit, OnDestroy {
  readonly auth = inject(AuthService);
  readonly channels = inject(ChannelStore);
  readonly messages = inject(MessageStore);
  readonly threads = inject(ThreadStore);
  readonly pins = inject(PinStore);
  readonly saved = inject(SavedStore);
  readonly hub = inject(ChatHubService);
  readonly push = inject(PushNotificationService);
  readonly notificationPrefs = inject(NotificationPreferencesStore);
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly attachments = inject(AttachmentQueueService);
  private readonly destroyRef = inject(DestroyRef);
  readonly palette = inject(CommandPaletteService);

  /** UX-003: tracks max-width 960px; sidebar starts collapsed on narrow. */
  readonly narrowViewport = signal(false);
  readonly sidebarOpen = signal(true);
  /** B-184: icon-only desktop rail; persisted in localStorage. */
  readonly navCompact = signal(readNavCompact());
  readonly contextOpen = signal(false);
  readonly fileDragActive = signal(false);
  readonly search = signal('');
  readonly searchFocused = signal(false);
  readonly searchResults = signal<SearchMessageHit[]>([]);
  readonly searchLoading = signal(false);
  readonly searchError = signal<string | null>(null);
  readonly searchOpen = signal(false);
  readonly searchScope = signal<'workspace' | 'channel'>('workspace');
  readonly searchSort = signal<SearchSort>('relevance');
  readonly searchTotal = signal(0);
  readonly searchCursor = signal<string | null>(null);
  readonly searchRecent = signal<string[]>([]);
  readonly parsedSearch = computed(() => parseSearchQuery(this.search()));
  readonly searchChips = computed(() => this.parsedSearch().chips);
  readonly searchSuggestions = computed(() => this.buildSearchSuggestions());
  readonly searchGroups = computed(() => this.groupSearchHits(this.searchResults()));
  readonly searchCanRun = computed(() => {
    const parsed = this.parsedSearch();
    return parsed.term.length >= 2 || hasSearchFilter(parsed);
  });
  readonly canAccessAdmin = computed(() =>
    this.channels.workspaces().some((workspace) => hasAdminDashboard(workspace.role)),
  );
  /** Workspace select: expanded rail only, and only when there is a choice. */
  readonly showWorkspaceSelector = computed(
    () => this.channels.workspaces().length > 1 && !(this.navCompact() && !this.narrowViewport()),
  );

  private searchTimer: ReturnType<typeof setTimeout> | null = null;
  private searchSeq = 0;
  private lastChannelId: string | null = null;
  private unsubPresence: (() => void) | null = null;
  private fileDragDepth = 0;

  constructor() {
    this.bindNarrowViewport();

    effect(() => {
      const channelId = this.channels.activeChannelId() ?? null;
      if (this.lastChannelId !== null && this.lastChannelId !== channelId) {
        this.threads.close();
        this.pins.closePanel();
        this.saved.closePanel();
        // Narrow overlay: free the timeline after a channel/DM pick.
        if (this.narrowViewport()) {
          this.sidebarOpen.set(false);
        }
      }
      this.lastChannelId = channelId;
      if (channelId) {
        void this.pins.loadForChannel(channelId);
      }
    });

    effect(() => {
      const workspaceId = this.channels.activeWorkspace()?.id ?? null;
      void this.saved.loadForWorkspace(workspaceId);
    });

    effect(() => {
      const raw = this.search();
      const parsed = parseSearchQuery(raw);
      const workspaceId = this.channels.activeWorkspace()?.id;
      const canRun = parsed.term.length >= 2 || hasSearchFilter(parsed);
      this.searchSort();
      this.searchScope();
      if (this.searchTimer) {
        clearTimeout(this.searchTimer);
        this.searchTimer = null;
      }

      if (!workspaceId || !canRun || this.auth.isOfflineDemo() || this.channels.isDemo()) {
        this.searchResults.set([]);
        this.searchError.set(null);
        this.searchLoading.set(false);
        this.searchTotal.set(0);
        this.searchCursor.set(null);
        this.searchOpen.set(this.searchFocused());
        return;
      }

      this.searchLoading.set(true);
      this.searchOpen.set(true);
      const seq = ++this.searchSeq;
      this.searchTimer = setTimeout(() => {
        void this.runSearch(workspaceId, parsed, seq);
      }, 280);
    });
  }

  async ngOnInit(): Promise<void> {
    this.unsubPresence = this.hub.onPresenceChanged((event) => {
      this.channels.setPresence(event.userId, event.status);
    });
    await Promise.all([this.channels.load(), this.hub.connect(), this.notificationPrefs.load()]);
    await this.applyPushDeepLink();
    const active = this.channels.activeChannel();
    if (active) {
      await this.messages.loadChannel(active.id);
    }
  }

  ngOnDestroy(): void {
    if (this.searchTimer) {
      clearTimeout(this.searchTimer);
    }
    this.unsubPresence?.();
  }

  @HostListener('window:keydown', ['$event'])
  onGlobalKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape' && !event.shiftKey) {
      if (this.palette.sheetOpen()) {
        event.preventDefault();
        this.palette.closeShortcutSheet();
        return;
      }
      if (this.palette.paletteOpen()) {
        event.preventDefault();
        this.palette.closePalette();
        return;
      }
      if (this.threads.open()) {
        this.threads.close();
        return;
      }
      if (this.saved.panelOpen()) {
        this.saved.closePanel();
        return;
      }
      if (this.pins.panelOpen()) {
        this.pins.closePanel();
        return;
      }
      if (this.notificationPrefs.panelOpen()) {
        this.notificationPrefs.closePanel();
        return;
      }
      if (this.push.devicesOpen()) {
        this.push.devicesOpen.set(false);
        return;
      }
      if (this.narrowViewport() && this.sidebarOpen()) {
        this.sidebarOpen.set(false);
        return;
      }
      this.contextOpen.set(false);
      this.searchFocused.set(false);
      this.searchOpen.set(false);
      (document.activeElement as HTMLElement | null)?.blur?.();
      return;
    }

    const shortcut = matchGlobalShortcut(event, isEditableTarget(event.target));
    if (!shortcut) return;
    if (this.palette.anyOverlayOpen() && shortcut !== 'palette') return;

    event.preventDefault();
    switch (shortcut) {
      case 'palette':
        this.palette.hydrateRecents(this.auth.profile()?.id);
        this.palette.openPalette();
        return;
      case 'search':
        this.focusMessageSearch();
        return;
      case 'channel-prev':
        void this.cycleActiveChannel(-1, 'all');
        return;
      case 'channel-next':
        void this.cycleActiveChannel(1, 'all');
        return;
      case 'unread-prev':
        void this.cycleActiveChannel(-1, 'unread');
        return;
      case 'unread-next':
        void this.cycleActiveChannel(1, 'unread');
        return;
      case 'mentions':
        void this.cycleActiveChannel(1, 'mention');
        return;
      case 'mark-read':
        void this.messages.markActiveChannelRead();
        return;
      case 'shortcuts':
        this.palette.openShortcutSheet();
        return;
    }
  }

  focusMessageSearch(): void {
    this.searchFocused.set(true);
    this.searchOpen.set(true);
    this.refreshRecentSearches();
    const el = document.getElementById('vc-search') as HTMLInputElement | null;
    el?.focus();
  }

  private async cycleActiveChannel(
    direction: 1 | -1,
    filter: 'all' | 'unread' | 'mention',
  ): Promise<void> {
    const next = cycleChannel(
      [...this.channels.publicChannels(), ...this.channels.directChannels()],
      this.channels.activeChannelId(),
      direction,
      filter,
    );
    if (!next) return;
    this.channels.selectChannel(next.id);
    await this.messages.loadChannel(next.id);
    this.palette.requestComposerFocus();
  }

  private bindNarrowViewport(): void {
    if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') {
      return;
    }

    const media = window.matchMedia(SHELL_NARROW_MEDIA_QUERY);
    const apply = (narrow: boolean) => {
      const wasNarrow = this.narrowViewport();
      this.narrowViewport.set(narrow);
      if (narrow) {
        // Entering (or starting in) narrow: collapse so timeline has room.
        this.sidebarOpen.set(defaultSidebarOpen(true));
      } else if (wasNarrow) {
        // Leaving narrow: restore desktop rail.
        this.sidebarOpen.set(defaultSidebarOpen(false));
      }
    };

    apply(media.matches);
    const onChange = (event: MediaQueryListEvent) => apply(event.matches);
    media.addEventListener('change', onChange);
    this.destroyRef.onDestroy(() => media.removeEventListener('change', onChange));
  }

  async onWorkspaceChange(event: Event): Promise<void> {
    const value = (event.target as HTMLSelectElement).value;
    await this.channels.selectWorkspace(value);
    const active = this.channels.activeChannel();
    if (active) {
      await this.messages.loadChannel(active.id);
    }
  }

  toggleSidebar(): void {
    this.sidebarOpen.update((v) => !v);
  }

  toggleNavCompact(): void {
    if (this.narrowViewport()) {
      return;
    }
    this.navCompact.update((v) => {
      const next = !v;
      writeNavCompact(next);
      return next;
    });
  }

  toggleContext(): void {
    this.pins.closePanel();
    this.saved.closePanel();
    this.contextOpen.update((v) => !v);
  }

  openPinsPanel(): void {
    this.contextOpen.set(false);
    this.saved.closePanel();
    this.pins.openPanel();
  }

  openSavedPanel(): void {
    this.contextOpen.set(false);
    this.pins.closePanel();
    this.saved.openPanel();
  }

  async onUnpinFromPanel(messageId: string): Promise<void> {
    const channelId = this.channels.activeChannelId();
    if (!channelId) return;
    await this.pins.unpinMessage(channelId, messageId);
  }

  onFileDragEnter(event: DragEvent): void {
    if (!this.hasFileDrag(event)) return;
    event.preventDefault();
    this.fileDragDepth += 1;
    this.fileDragActive.set(true);
  }

  onFileDragOver(event: DragEvent): void {
    if (!this.hasFileDrag(event)) return;
    event.preventDefault();
    if (event.dataTransfer) {
      event.dataTransfer.dropEffect = 'copy';
    }
  }

  onFileDragLeave(event: DragEvent): void {
    if (!this.hasFileDrag(event)) return;
    this.fileDragDepth = Math.max(0, this.fileDragDepth - 1);
    if (this.fileDragDepth === 0) {
      this.fileDragActive.set(false);
    }
  }

  onFileDrop(event: DragEvent): void {
    if (!this.hasFileDrag(event)) return;
    event.preventDefault();
    this.fileDragDepth = 0;
    this.fileDragActive.set(false);
    const files = collectFilesFromDataTransfer(event.dataTransfer);
    if (files.length) {
      void this.attachments.addFiles(files);
    }
  }

  private hasFileDrag(event: DragEvent): boolean {
    return !!event.dataTransfer?.types.includes('Files');
  }

  async logout(): Promise<void> {
    await this.auth.logout();
  }

  readonly searchJumpNotice = signal<string | null>(null);

  async openPushNotice(): Promise<void> {
    const notice = this.push.notice();
    if (!notice) return;
    await this.jumpToPushTarget(notice.channelId, notice.messageId, notice.seq);
    this.push.dismissNotice();
  }

  async openSearchHit(hit: SearchMessageHit): Promise<void> {
    const userId = this.auth.profile()?.id;
    if (userId && this.search().trim()) {
      this.searchRecent.set(writeRecentSearch(userId, this.search().trim()));
    }
    await this.channels.selectChannel(hit.channelId);
    const result = await this.messages.jumpToSequence(hit.channelId, hit.sequence, hit.messageId);
    if (result === 'deleted') {
      this.searchJumpNotice.set('Esta mensagem foi removida.');
    } else if (result === 'missing') {
      this.searchJumpNotice.set('Não foi possível localizar a mensagem.');
    } else {
      this.searchJumpNotice.set(null);
    }
    this.searchOpen.set(false);
    this.searchFocused.set(false);
  }

  onSearchFocus(): void {
    this.searchFocused.set(true);
    this.searchOpen.set(true);
    this.refreshRecentSearches();
  }

  removeSearchChip(chip: SearchChip): void {
    this.search.set(removeSearchChip(this.search(), chip));
  }

  applySuggestion(value: string): void {
    const active = this.parsedSearch().activeOperator;
    if (!active) {
      this.search.set(value);
      return;
    }
    this.search.set(applySearchOperator(this.search(), active.op, value));
  }

  applyRecent(query: string): void {
    this.search.set(query);
  }

  setSearchScope(scope: 'workspace' | 'channel'): void {
    this.searchScope.set(scope);
  }

  setSearchSort(sort: SearchSort): void {
    this.searchSort.set(sort);
  }

  highlightParts(text: string): Array<{ text: string; hit: boolean }> {
    return highlightSearchParts(text, this.parsedSearch().term);
  }

  async loadMoreSearch(): Promise<void> {
    const workspaceId = this.channels.activeWorkspace()?.id;
    const cursor = this.searchCursor();
    if (!workspaceId || !cursor) {
      return;
    }
    const seq = ++this.searchSeq;
    await this.runSearch(workspaceId, this.parsedSearch(), seq, cursor);
  }

  private refreshRecentSearches(): void {
    const userId = this.auth.profile()?.id;
    this.searchRecent.set(userId ? readRecentSearches(userId) : []);
  }

  private resolveAuthorId(token: string | undefined, members: WorkspaceMember[]): string | undefined {
    if (!token) return undefined;
    const needle = token.replace(/^@/, '').toLowerCase();
    if (/^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(needle)) {
      return needle;
    }
    const exact = members.find((m) => m.displayName.toLowerCase() === needle);
    if (exact) return exact.userId;
    return members.find((m) => m.displayName.toLowerCase().startsWith(needle))?.userId;
  }

  private resolveChannelId(token: string | undefined): string | undefined {
    if (!token) return undefined;
    const needle = token.replace(/^#/, '').toLowerCase();
    if (/^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(needle)) {
      return needle;
    }
    const channels = this.channels.channels();
    const exact = channels.find((c) => c.name.toLowerCase() === needle);
    if (exact) return exact.id;
    return channels.find((c) => c.name.toLowerCase().startsWith(needle))?.id;
  }

  private buildSearchSuggestions(): Array<{ value: string; label: string }> {
    const active = this.parsedSearch().activeOperator;
    if (!active) {
      return [];
    }
    const q = active.query.replace(/^[@#]/, '').toLowerCase();
    if (active.op === 'de') {
      return this.channels
        .members()
        .filter((m) => !q || m.displayName.toLowerCase().includes(q))
        .slice(0, 8)
        .map((m) => ({ value: m.displayName, label: m.displayName }));
    }
    if (active.op === 'em') {
      return this.channels
        .channels()
        .filter((c) => !c.isDirect)
        .filter((c) => !q || c.name.toLowerCase().includes(q))
        .slice(0, 8)
        .map((c) => ({ value: c.name, label: `#${c.name}` }));
    }
    if (active.op === 'tem') {
      return [
        { value: 'anexo', label: 'tem:anexo' },
        { value: 'link', label: 'tem:link' },
        { value: 'imagem', label: 'tem:imagem' },
        { value: 'audio', label: 'tem:audio' },
        { value: 'documento', label: 'tem:documento' },
      ].filter((item) => !q || item.value.startsWith(q));
    }
    if (active.op === 'antes' || active.op === 'depois') {
      const today = new Date().toISOString().slice(0, 10);
      return [{ value: today, label: today }];
    }
    return [];
  }

  private groupSearchHits(items: SearchMessageHit[]): Array<{
    channelId: string;
    channelName: string;
    count: number;
    items: SearchMessageHit[];
  }> {
    const groups = new Map<string, { channelId: string; channelName: string; items: SearchMessageHit[] }>();
    for (const hit of items) {
      const current = groups.get(hit.channelId) ?? {
        channelId: hit.channelId,
        channelName: hit.channelName,
        items: [],
      };
      current.items.push(hit);
      groups.set(hit.channelId, current);
    }
    return [...groups.values()].map((group) => ({ ...group, count: group.items.length }));
  }

  private async applyPushDeepLink(): Promise<void> {
    const params = this.route.snapshot.queryParamMap;
    const channelId = params.get('channel');
    const messageId = params.get('message');
    const seqRaw = params.get('seq');
    if (!channelId) return;
    const seq = seqRaw ? Number(seqRaw) : NaN;
    await this.jumpToPushTarget(channelId, messageId, Number.isFinite(seq) ? seq : undefined);
  }

  private async jumpToPushTarget(channelId: string, messageId: string | null, seq?: number): Promise<void> {
    this.channels.selectChannel(channelId);
    if (messageId && seq && seq > 0) {
      await this.messages.jumpToSequence(channelId, seq, messageId);
    } else if (messageId) {
      this.messages.jumpToMessage(messageId);
    }
  }

  private async runSearch(
    workspaceId: string,
    parsed: ReturnType<typeof parseSearchQuery>,
    seq: number,
    cursor?: string,
  ): Promise<void> {
    try {
      const authorId = this.resolveAuthorId(parsed.authorToken, this.channels.members());
      const channelFromOp = this.resolveChannelId(parsed.channelToken);
      const channelId =
        channelFromOp ??
        (this.searchScope() === 'channel' ? this.channels.activeChannelId() ?? undefined : undefined);
      const result = await this.api.searchMessages({
        workspaceId,
        q: parsed.term,
        channelId,
        authorId,
        from: parsed.from,
        to: parsed.to,
        hasAttachment: parsed.hasAttachment,
        hasLink: parsed.hasLink,
        attachmentKind: parsed.attachmentKind,
        sort: this.searchSort(),
        cursor,
        limit: 20,
      });
      if (seq !== this.searchSeq) {
        return;
      }
      this.searchResults.set(cursor ? [...this.searchResults(), ...result.items] : result.items);
      this.searchTotal.set(result.total ?? result.items.length);
      this.searchCursor.set(result.cursor ?? null);
      this.searchError.set(null);
    } catch {
      if (seq !== this.searchSeq) {
        return;
      }
      if (!cursor) {
        this.searchResults.set([]);
        this.searchTotal.set(0);
        this.searchCursor.set(null);
      }
      this.searchError.set('Não foi possível buscar mensagens.');
    } finally {
      if (seq === this.searchSeq) {
        this.searchLoading.set(false);
      }
    }
  }
}
