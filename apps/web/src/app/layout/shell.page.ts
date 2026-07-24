import { Component, HostListener, OnDestroy, OnInit, effect, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { AuthService } from '../core/auth/auth.service';
import { ApiService } from '../core/api/api.service';
import { ChannelStore } from '../core/services/channel.store';
import { ChatHubService } from '../core/services/chat-hub.service';
import { MessageStore } from '../core/services/message.store';
import { ChannelList } from '../features/chat/channel-list/channel-list';
import { Composer } from '../features/chat/composer/composer';
import { Timeline } from '../features/chat/timeline/timeline';
import { SummarizeButton } from '../features/ai/summarize-button';
import { SearchMessageHit } from '../shared/models/chat.models';
import {
  ConnectionBanner,
  DensityControl,
  IconButton,
  Input,
  ThemeToggle,
} from '../shared/ui';

@Component({
  selector: 'vc-shell-page',
  standalone: true,
  imports: [
    RouterLink,
    RouterLinkActive,
    ChannelList,
    Timeline,
    Composer,
    SummarizeButton,
    ConnectionBanner,
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
  readonly hub = inject(ChatHubService);
  private readonly api = inject(ApiService);

  readonly sidebarOpen = signal(true);
  readonly contextOpen = signal(false);
  readonly search = signal('');
  readonly searchFocused = signal(false);
  readonly searchResults = signal<SearchMessageHit[]>([]);
  readonly searchLoading = signal(false);
  readonly searchError = signal<string | null>(null);
  readonly searchOpen = signal(false);

  private searchTimer: ReturnType<typeof setTimeout> | null = null;
  private searchSeq = 0;

  constructor() {
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
      this.contextOpen.set(false);
      this.searchFocused.set(false);
      this.searchOpen.set(false);
      (document.activeElement as HTMLElement | null)?.blur?.();
    }
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

  async logout(): Promise<void> {
    await this.auth.logout();
  }

  async openSearchHit(hit: SearchMessageHit): Promise<void> {
    this.channels.selectChannel(hit.channelId);
    await this.messages.loadChannel(hit.channelId);
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
