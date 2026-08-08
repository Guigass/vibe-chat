import { DatePipe } from '@angular/common';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { HlmSelectImports } from '@spartan-ng/helm/select';
import { ApiService } from '../../core/api/api.service';
import {
  AdminConversationItem,
  AdminConversationMessageItem,
} from '../../shared/models/chat.models';
import { Badge, BadgeTone } from '../../shared/ui';
import { AdminContextService } from './admin-context.service';
import { AdminAreaId } from './admin-permissions';

interface ConversationOption {
  id: string;
  label: string;
  type: string;
}

@Component({
  selector: 'vc-admin-conversations',
  standalone: true,
  imports: [DatePipe, Badge, ...HlmSelectImports],
  templateUrl: './admin-conversations.page.html',
  styleUrl: './admin-shared.scss',
})
export class AdminConversationsPage implements OnInit {
  readonly areaId: AdminAreaId = 'conversations';

  private readonly api = inject(ApiService);
  readonly ctx = inject(AdminContextService);

  readonly loading = signal(true);
  readonly loadError = signal(false);
  readonly conversations = signal<AdminConversationItem[]>([]);
  readonly selectedConversationId = signal<string | null>(null);
  readonly conversationMessages = signal<AdminConversationMessageItem[]>([]);
  readonly conversationMessagesBusy = signal(false);
  readonly conversationMessagesError = signal(false);
  readonly activeThreadId = signal<string | null>(null);
  readonly threadMessages = signal<AdminConversationMessageItem[]>([]);
  readonly threadBusy = signal(false);

  readonly searchQuery = signal('');
  readonly typeFilter = signal('all');

  readonly filteredConversations = computed(() => {
    const q = this.searchQuery().trim().toLowerCase();
    const type = this.typeFilter();
    return this.conversations().filter((row) => {
      if (type !== 'all' && row.type !== type) {
        return false;
      }
      if (!q) {
        return true;
      }
      return row.name.toLowerCase().includes(q);
    });
  });

  readonly conversationOptions = computed<ConversationOption[]>(() =>
    this.filteredConversations().map((c) => ({
      id: c.id,
      label: this.conversationLabel(c),
      type: c.type,
    })),
  );

  readonly typeOptions = computed(() => {
    const types = new Set(this.conversations().map((c) => c.type));
    return ['all', ...Array.from(types).sort()];
  });

  async ngOnInit(): Promise<void> {
    await this.ctx.ensureReady();
    await this.loadConversations();
    this.loading.set(false);
  }

  conversationLabelForId = (id: string | null | undefined): string => {
    if (!id) {
      return '';
    }
    return this.conversationOptions().find((option) => option.id === id)?.label ?? id;
  };

  messageStatusTone(m: AdminConversationMessageItem): BadgeTone {
    if (m.deletedAt) {
      return 'danger';
    }
    if (m.editedAt) {
      return 'warn';
    }
    return 'success';
  }

  messageStatusLabel(m: AdminConversationMessageItem): string {
    if (m.deletedAt) {
      return m.deletedByName ? `soft-delete · ${m.deletedByName}` : 'soft-delete';
    }
    if (m.editedAt) {
      return 'editado';
    }
    return 'ok';
  }

  conversationLabel(row: AdminConversationItem): string {
    const prefix =
      row.type === 'Direct'
        ? 'DM'
        : row.type === 'Private'
          ? 'Private'
          : row.type === 'Group'
            ? 'Group'
            : '#';
    return `${prefix} ${row.name}`;
  }

  async onConversationSelected(channelId: string | null | undefined): Promise<void> {
    channelId = channelId ?? null;
    this.selectedConversationId.set(channelId);
    this.activeThreadId.set(null);
    this.threadMessages.set([]);
    this.conversationMessages.set([]);
    this.conversationMessagesError.set(false);
    if (!channelId) {
      return;
    }

    this.conversationMessagesBusy.set(true);
    try {
      const rows = await this.api.getAdminConversationMessages(channelId, { limit: 80 });
      this.conversationMessages.set(rows);
    } catch {
      this.conversationMessagesError.set(true);
      this.conversationMessages.set([]);
    } finally {
      this.conversationMessagesBusy.set(false);
    }
  }

  async openThread(threadId: string): Promise<void> {
    if (!threadId) {
      return;
    }
    this.activeThreadId.set(threadId);
    this.threadBusy.set(true);
    try {
      const rows = await this.api.getAdminThreadMessages(threadId, { limit: 80 });
      this.threadMessages.set(rows);
    } catch {
      this.threadMessages.set([]);
    } finally {
      this.threadBusy.set(false);
    }
  }

  closeThread(): void {
    this.activeThreadId.set(null);
    this.threadMessages.set([]);
  }

  private async loadConversations(): Promise<void> {
    try {
      const rows = await this.api.getAdminConversations({
        workspaceId: this.ctx.workspace()?.id,
        limit: 100,
      });
      this.conversations.set(rows);
      this.loadError.set(false);
    } catch {
      this.loadError.set(true);
      this.conversations.set([]);
    }
  }
}
