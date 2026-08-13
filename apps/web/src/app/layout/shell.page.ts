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
import { ChannelList } from '../features/chat/channel-list/channel-list';
import { Composer } from '../features/chat/composer/composer';
import { Timeline } from '../features/chat/timeline/timeline';
import { PinsPanel } from '../features/chat/pins-panel/pins-panel';
import { SavedPanel } from '../features/chat/saved-panel/saved-panel';
import { ThreadPanel } from '../features/chat/thread-panel/thread-panel';
import { SuggestReplyButton } from '../features/ai/suggest-reply-button';
import { SummarizeButton } from '../features/ai/summarize-button';
import { SearchMessageHit } from '../shared/models/chat.models';
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
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly attachments = inject(AttachmentQueueService);
  private readonly destroyRef = inject(DestroyRef);

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
      const term = this.search().trim();
      const workspaceId = this.channels.activeWorkspace()?.id;
      if (this.searchTimer) {
        clearTimeout(this.searchTimer);
        this.searchTimer = null;
      }

      if (!workspaceId || term.length < 2 || this.auth.isOfflineDemo() || this.channels.isDemo()) {
        this.searchResults.set([]);
        this.searchError.set(null);
        this.searchLoading.set(false);
        this.searchOpen.set(this.searchFocused() && term.length >= 2);
        return;
      }

      this.searchLoading.set(true);
      this.searchOpen.set(true);
      const seq = ++this.searchSeq;
      this.searchTimer = setTimeout(() => {
        void this.runSearch(workspaceId, term, seq);
      }, 280);
    });
  }

  async ngOnInit(): Promise<void> {
    this.unsubPresence = this.hub.onPresenceChanged((event) => {
      this.channels.setPresence(event.userId, event.status);
    });
    await Promise.all([this.channels.load(), this.hub.connect()]);
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
    const isModK = (event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k';
    if (isModK) {
      event.preventDefault();
      this.searchFocused.set(true);
      this.searchOpen.set(true);
      const el = document.getElementById('vc-search') as HTMLInputElement | null;
      el?.focus();
      return;
    }

    if (event.key === 'Escape') {
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
    }
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

  private async runSearch(workspaceId: string, term: string, seq: number): Promise<void> {
    try {
      const result = await this.api.searchMessages({ workspaceId, q: term, limit: 12 });
      if (seq !== this.searchSeq) {
        return;
      }
      this.searchResults.set(result.items);
      this.searchError.set(null);
    } catch {
      if (seq !== this.searchSeq) {
        return;
      }
      this.searchResults.set([]);
      this.searchError.set('Não foi possível buscar mensagens.');
    } finally {
      if (seq === this.searchSeq) {
        this.searchLoading.set(false);
      }
    }
  }
}
