import { Component, HostListener, OnInit, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { AuthService } from '../core/auth/auth.service';
import { ChannelStore } from '../core/services/channel.store';
import { ChatHubService } from '../core/services/chat-hub.service';
import { MessageStore } from '../core/services/message.store';
import { ChannelList } from '../features/chat/channel-list/channel-list';
import { Composer } from '../features/chat/composer/composer';
import { Timeline } from '../features/chat/timeline/timeline';
import { SummarizeButton } from '../features/ai/summarize-button';
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
export class ShellPage implements OnInit {
  readonly auth = inject(AuthService);
  readonly channels = inject(ChannelStore);
  readonly messages = inject(MessageStore);
  readonly hub = inject(ChatHubService);

  readonly sidebarOpen = signal(true);
  readonly contextOpen = signal(false);
  readonly search = signal('');
  readonly searchFocused = signal(false);

  async ngOnInit(): Promise<void> {
    await Promise.all([this.channels.load(), this.hub.connect()]);
    const active = this.channels.activeChannel();
    if (active) {
      await this.messages.loadChannel(active.id);
    }
  }

  @HostListener('window:keydown', ['$event'])
  onGlobalKeydown(event: KeyboardEvent): void {
    const isModK = (event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k';
    if (isModK) {
      event.preventDefault();
      this.searchFocused.set(true);
      const el = document.getElementById('vc-search') as HTMLInputElement | null;
      el?.focus();
      return;
    }

    if (event.key === 'Escape') {
      this.contextOpen.set(false);
      this.searchFocused.set(false);
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
}
