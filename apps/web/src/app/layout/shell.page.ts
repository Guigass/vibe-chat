import {
  Component,
  DestroyRef,
  HostListener,
  OnDestroy,
  OnInit,
  effect,
  inject,
  signal,
} from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { AuthService } from '../core/auth/auth.service';
import { ApiService } from '../core/api/api.service';
import { ChannelStore } from '../core/services/channel.store';
import { ChatHubService } from '../core/services/chat-hub.service';
import { MessageStore } from '../core/services/message.store';
import { ThreadStore } from '../core/services/thread.store';
import { ChannelList } from '../features/chat/channel-list/channel-list';
import { Composer } from '../features/chat/composer/composer';
import { Timeline } from '../features/chat/timeline/timeline';
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
} from '../shared/ui';
import { AttachmentQueueService } from '../features/chat/composer/attachment-queue.service';
import { collectFilesFromDataTransfer } from '../features/chat/composer/attachment-upload';
import { defaultSidebarOpen, SHELL_NARROW_MEDIA_QUERY } from './shell-viewport';

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
    SummarizeButton,
    SuggestReplyButton,
    ConnectionBanner,
    UpdateBanner,
    ThemeToggle,
    DensityControl,
    IconButton,
    Input,
  ],
  templateUrl: './shell.page.html',
  styleUrl: './shell.page.scss',
})
export class ShellPage implements OnInit, OnDestroy {
  readonly auth = inject(AuthService);
  readonly channels = inject(ChannelStore);
  readonly messages = inject(MessageStore);
  readonly threads = inject(ThreadStore);
  readonly hub = inject(ChatHubService);
  private readonly api = inject(ApiService);
  private readonly attachments = inject(AttachmentQueueService);
  private readonly destroyRef = inject(DestroyRef);

  /** UX-003: tracks max-width 960px; sidebar starts collapsed on narrow. */
  readonly narrowViewport = signal(false);
  readonly sidebarOpen = signal(true);
  readonly contextOpen = signal(false);
  readonly fileDragActive = signal(false);
  readonly search = signal('');
  readonly searchFocused = signal(false);
  readonly searchResults = signal<SearchMessageHit[]>([]);
  readonly searchLoading = signal(false);
  readonly searchError = signal<string | null>(null);
  readonly searchOpen = signal(false);

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
        // Narrow overlay: free the timeline after a channel/DM pick.
        if (this.narrowViewport()) {
          this.sidebarOpen.set(false);
        }
      }
      this.lastChannelId = channelId;
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

  toggleContext(): void {
    this.contextOpen.update((v) => !v);
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
