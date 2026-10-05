import { Injectable, computed, inject, signal } from '@angular/core';
import { ChannelStore } from '../../../core/services/channel.store';
import { DraftStoreService } from '../../../core/services/draft-store.service';
import { updateTextareaSelection } from '../../../shared/markdown/markdown-format';
import {
  SlashCommandDef,
  detectSlashQuery,
  filterSlashCommands,
  insertSlashCommand,
} from '../../../shared/markdown/slash-tokens';
import { ComposerMentions } from './composer-mentions';
import { ComposerPoll } from './composer-poll';
import { ComposerState } from './composer-state';
import { SlashCommandsService } from './slash-commands.service';

@Injectable()
export class ComposerSlashSession {
  private readonly slash = inject(SlashCommandsService);
  private readonly channels = inject(ChannelStore);
  private readonly drafts = inject(DraftStoreService);
  private readonly state = inject(ComposerState);
  private readonly polls = inject(ComposerPoll);

  readonly open = signal(false);
  readonly activeIndex = signal(0);
  readonly catalog = signal<SlashCommandDef[]>([]);
  readonly context = signal<{ query: string; slashIndex: number } | null>(null);

  readonly items = computed(() => {
    const context = this.context();
    if (!context) return [];
    return filterSlashCommands(this.catalog(), context.query);
  });

  close(): void {
    this.open.set(false);
    this.context.set(null);
    this.activeIndex.set(0);
  }

  syncWithMentions(text: string, cursor: number, editing: boolean, mentions: ComposerMentions): void {
    if (editing) {
      this.close();
      mentions.sync(text, cursor);
      return;
    }

    const slash = detectSlashQuery(text, cursor);
    if (slash) {
      mentions.close();
      this.context.set(slash);
      this.open.set(true);
      this.activeIndex.set(0);
      void this.ensureCatalog();
      return;
    }

    this.close();
    mentions.sync(text, cursor);
  }

  apply(item: SlashCommandDef): void {
    const textarea = this.state.textarea();
    if (!textarea) return;
    const result = insertSlashCommand(this.state.draft(), item.name);
    this.state.draft.set(result.value);
    this.close();
    updateTextareaSelection(textarea, result.value, result.cursor, result.cursor);
  }

  async ensureCatalog(): Promise<void> {
    const workspace = this.channels.activeWorkspace();
    if (!workspace) {
      this.catalog.set([]);
      return;
    }
    try {
      const commands = await this.slash.listCommands(workspace.id);
      this.catalog.set(commands);
    } catch {
      this.catalog.set([]);
    }
  }

  async run(raw: string): Promise<void> {
    if (this.state.submitting()) return;
    this.state.submitting.set(true);
    this.close();
    this.state.validationError.set(null);
    try {
      const result = await this.slash.execute(raw);
      if (result.openPollComposer) {
        this.polls.openWith(result.pollQuestion, result.pollOptions);
      }
      if (result.clearDraft) {
        const channelId = this.state.boundChannelId;
        this.state.draft.set('');
        if (channelId) {
          await this.drafts.remove(channelId);
        }
      }
    } finally {
      this.state.submitting.set(false);
    }
  }
}
