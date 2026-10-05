import { Injectable, WritableSignal, computed, inject, signal } from '@angular/core';
import { ApiService } from '../../../core/api/api.service';
import { AuthService } from '../../../core/auth/auth.service';
import { ui } from '../../../core/i18n/strings';
import { ChannelStore } from '../../../core/services/channel.store';
import { UserStatusStore } from '../../../core/services/user-status.store';
import { updateTextareaSelection } from '../../../shared/markdown/markdown-format';
import {
  MentionAutocompleteItem,
  composerMentionDisplay,
  detectMentionQuery,
  encodeMentionPlainText,
  filterMentionItems,
  formatMentionPlainText,
  insertMentionToken,
} from '../../../shared/markdown/mention-tokens';
import { ComposerState } from './composer-state';

@Injectable()
export class ComposerMentions {
  private readonly api = inject(ApiService);
  private readonly channels = inject(ChannelStore);
  private readonly auth = inject(AuthService);
  private readonly userStatus = inject(UserStatusStore);
  private readonly state = inject(ComposerState);

  readonly open = signal(false);
  readonly activeIndex = signal(0);
  readonly remoteItems = signal<MentionAutocompleteItem[]>([]);
  readonly context = signal<{ query: string; atIndex: number } | null>(null);
  private readonly aliases = signal<Record<string, string>>({});
  private mentionFetchTimer: ReturnType<typeof setTimeout> | null = null;

  readonly items = computed(() => {
    const context = this.context();
    if (!context) return [];
    const base: MentionAutocompleteItem[] = [
      { kind: 'here', displayName: ui.mentionHere, subtitle: ui.mentionHereNotify },
      { kind: 'channel', displayName: ui.mentionChannel, subtitle: ui.mentionChannelNotify },
      ...this.remoteItems(),
    ];
    return filterMentionItems(base, context.query, {
      excludeUserId: this.auth.profile()?.id,
    });
  });

  mentionLabelMap(): Record<string, string> {
    return { ...this.channels.mentionLabels(), ...this.aliases() };
  }

  toComposerDisplay(source: string): string {
    return formatMentionPlainText(source, this.mentionLabelMap());
  }

  toSendBody(source: string): string {
    return encodeMentionPlainText(source.trim(), this.mentionLabelMap());
  }

  sync(text: string, cursor: number): void {
    const context = detectMentionQuery(text, cursor);
    if (!context) {
      this.close();
      return;
    }

    this.context.set(context);
    this.open.set(true);
    this.activeIndex.set(0);
    this.scheduleFetch(context.query);
  }

  close(): void {
    this.open.set(false);
    this.context.set(null);
    this.activeIndex.set(0);
    if (this.mentionFetchTimer) {
      clearTimeout(this.mentionFetchTimer);
      this.mentionFetchTimer = null;
    }
  }

  apply(item: MentionAutocompleteItem, draft: WritableSignal<string>): void {
    const context = this.context();
    const textarea = this.state.textarea();
    if (!context || !textarea) return;

    if (item.kind === 'user' && item.userId) {
      const name = item.displayName.replace(/^@/, '').trim();
      if (name) {
        this.aliases.update((current) => ({ ...current, [item.userId!]: name }));
      }
    }

    const display = composerMentionDisplay(item);
    const result = insertMentionToken(draft(), context.atIndex, context.query.length, display);
    draft.set(result.value);
    this.close();
    updateTextareaSelection(textarea, result.value, result.cursor, result.cursor);
  }

  hasActiveQuery(): boolean {
    const textarea = this.state.textarea();
    if (!textarea) return this.context() !== null;
    return detectMentionQuery(textarea.value, textarea.selectionStart ?? textarea.value.length) !== null;
  }

  private scheduleFetch(query: string): void {
    if (this.mentionFetchTimer) {
      clearTimeout(this.mentionFetchTimer);
    }

    const workspace = this.channels.activeWorkspace();
    const channel = this.channels.activeChannel();
    if (!workspace || !channel || this.channels.isDemo()) {
      this.remoteItems.set(
        this.channels.members().map((member) => ({
          kind: 'user' as const,
          userId: member.userId,
          displayName: member.displayName,
          email: member.email,
          subtitle: this.mentionSubtitle(member.userId),
        })),
      );
      return;
    }

    this.mentionFetchTimer = setTimeout(() => {
      void this.api
        .getChannelMembers(workspace.id, channel.id, query)
        .then((members) => {
          this.remoteItems.set(
            members.map((member) => ({
              kind: 'user' as const,
              userId: member.userId,
              displayName: member.displayName,
              email: member.email,
              subtitle: this.mentionSubtitle(member.userId),
            })),
          );
        })
        .catch(() => {
          this.remoteItems.set([]);
        });
    }, 200);
  }

  private mentionStatus(userId: string): string | undefined {
    const status = this.userStatus.entryOf(userId)?.status;
    if (!status) return undefined;
    const line = `${status.emoji} ${status.text}`.trim();
    return line || undefined;
  }

  private contactSubtitle(userId: string): string | undefined {
    const label = this.channels.contactLabel?.(userId);
    return label || undefined;
  }

  private mentionSubtitle(userId: string): string | undefined {
    const department = this.contactSubtitle(userId);
    const status = this.mentionStatus(userId);
    if (department && status) return `${department} · ${status}`;
    return department || status;
  }
}
