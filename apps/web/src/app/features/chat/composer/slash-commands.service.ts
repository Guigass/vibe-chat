import { Injectable, inject, signal } from '@angular/core';
import { ApiService } from '../../../core/api/api.service';
import { fillTemplate, translateErrorCode, ui } from '../../../core/i18n/strings';
import { ChannelStore } from '../../../core/services/channel.store';
import { CommandPaletteService } from '../../../core/services/command-palette.service';
import { MessageStore } from '../../../core/services/message.store';
import {
  ParsedSlashCommand,
  SlashCommandDef,
  parseSlashCommand,
} from '../../../shared/markdown/slash-tokens';

export type SlashNoticeKind = 'info' | 'error' | 'help' | 'summary';

export interface SlashNotice {
  kind: SlashNoticeKind;
  text: string;
  lines?: string[];
}

export interface SlashExecResult {
  ok: boolean;
  clearDraft: boolean;
  notice: SlashNotice | null;
  openPollComposer?: boolean;
  pollQuestion?: string;
  pollOptions?: string[];
}

const DEMO_COMMANDS: SlashCommandDef[] = [
  { name: 'dm', description: ui.slashDmDesc, usage: '/dm @pessoa' },
  { name: 'topico', description: ui.slashTopicDesc, usage: '/topico <texto>' },
  { name: 'convidar', description: ui.slashInviteDesc, usage: '/convidar <email>' },
  { name: 'resumir', description: ui.slashSummarizeDesc, usage: '/resumir' },
  { name: 'apagar', description: ui.slashDeleteDesc, usage: '/apagar' },
  { name: 'enquete', description: ui.slashPollDesc, usage: '/enquete' },
  { name: 'ajuda', description: ui.slashHelpDesc, usage: '/ajuda' },
];

@Injectable({ providedIn: 'root' })
export class SlashCommandsService {
  private readonly api = inject(ApiService);
  private readonly channels = inject(ChannelStore);
  private readonly messages = inject(MessageStore);
  private readonly palette = inject(CommandPaletteService);

  private readonly cache = new Map<string, SlashCommandDef[]>();
  readonly notice = signal<SlashNotice | null>(null);

  clearNotice(): void {
    this.notice.set(null);
  }

  async listCommands(workspaceId: string): Promise<SlashCommandDef[]> {
    if (this.channels.isDemo()) {
      return DEMO_COMMANDS;
    }

    const cached = this.cache.get(workspaceId);
    if (cached) return cached;

    const rows = await this.api.getCommands(workspaceId);
    this.cache.set(workspaceId, rows);
    return rows;
  }

  invalidateCache(workspaceId?: string): void {
    if (workspaceId) {
      this.cache.delete(workspaceId);
      return;
    }
    this.cache.clear();
  }

  async execute(raw: string): Promise<SlashExecResult> {
    const parsed = parseSlashCommand(raw);
    if (!parsed || !parsed.name) {
      return this.fail(ui.slashInvalid, false);
    }

    const workspace = this.channels.activeWorkspace();
    const channel = this.channels.activeChannel();
    if (!workspace || !channel) {
      return this.fail(ui.slashNeedChannel, false);
    }

    const available = await this.listCommands(workspace.id);
    const known = available.find((c) => c.name === parsed.name);
    if (!known) {
      return this.fail(fillTemplate(ui.slashUnknownHelp, { name: parsed.name }), false);
    }

    switch (parsed.name) {
      case 'ajuda':
        return this.runAjuda(available);
      case 'dm':
        return this.runDm(parsed);
      case 'topico':
        return this.runTopico(parsed, workspace.id, channel.id, channel.isDirect === true);
      case 'convidar':
        return this.runConvidar(parsed, workspace.id);
      case 'resumir':
        return this.runResumir(workspace.id, channel.id);
      case 'apagar':
        return this.runApagar();
      case 'enquete':
        return this.runEnquete(parsed, channel.isDirect === true);
      default:
        return this.fail(fillTemplate(ui.slashUnknown, { name: parsed.name }), false);
    }
  }

  private runAjuda(commands: SlashCommandDef[]): SlashExecResult {
    const notice: SlashNotice = {
      kind: 'help',
      text: ui.slashAvailable,
      lines: commands.map((c) => `${c.usage} — ${c.description}`),
    };
    this.notice.set(notice);
    this.palette.openShortcutSheet();
    return { ok: true, clearDraft: true, notice };
  }

  private async runDm(parsed: ParsedSlashCommand): Promise<SlashExecResult> {
    const handle = parsed.argsRaw.replace(/^@/, '').trim();
    if (!handle) {
      return this.fail(ui.slashDmUsage, false);
    }

    const members = this.channels.members();
    const match = members.find(
      (m) =>
        m.displayName.toLowerCase() === handle.toLowerCase() ||
        m.email.toLowerCase() === handle.toLowerCase() ||
        m.displayName.toLowerCase().startsWith(handle.toLowerCase()),
    );

    if (!match) {
      return this.fail(fillTemplate(ui.slashMemberNotFound, { handle }), false);
    }

    const channel = await this.channels.openDirectMessage(match.userId);
    if (!channel) {
      return this.fail(ui.slashDmOpenError, false);
    }

    const notice: SlashNotice = {
      kind: 'info',
      text: fillTemplate(ui.slashDmOpened, { name: match.displayName }),
    };
    this.notice.set(notice);
    return { ok: true, clearDraft: true, notice };
  }

  private async runTopico(
    parsed: ParsedSlashCommand,
    workspaceId: string,
    channelId: string,
    isDirect: boolean,
  ): Promise<SlashExecResult> {
    if (isDirect) {
      return this.fail(ui.slashDmNoTopic, false);
    }
    if (!parsed.argsRaw) {
      return this.fail(ui.slashTopicUsage, false);
    }
    if (parsed.argsRaw.length > 250) {
      return this.fail(ui.slashTopicTooLong, false);
    }

    if (this.channels.isDemo()) {
      this.channels.patchChannel(channelId, { description: parsed.argsRaw });
      const notice: SlashNotice = { kind: 'info', text: ui.slashTopicUpdated };
      this.notice.set(notice);
      return { ok: true, clearDraft: true, notice };
    }

    try {
      const updated = await this.api.updateChannelTopic(workspaceId, channelId, parsed.argsRaw);
      this.channels.patchChannel(channelId, { description: updated.description });
      const notice: SlashNotice = { kind: 'info', text: ui.slashTopicUpdated };
      this.notice.set(notice);
      return { ok: true, clearDraft: true, notice };
    } catch (err) {
      return this.fail(this.errorMessage(err, ui.slashTopicError), false);
    }
  }

  private async runConvidar(
    parsed: ParsedSlashCommand,
    workspaceId: string,
  ): Promise<SlashExecResult> {
    const email = parsed.argsRaw.trim();
    if (!email || !email.includes('@')) {
      return this.fail(ui.slashInviteUsage, false);
    }

    if (this.channels.isDemo()) {
      const notice: SlashNotice = {
        kind: 'info',
        text: fillTemplate(ui.slashInviteSimulated, { email }),
      };
      this.notice.set(notice);
      return { ok: true, clearDraft: true, notice };
    }

    try {
      await this.api.inviteMember(workspaceId, { email });
      const notice: SlashNotice = {
        kind: 'info',
        text: fillTemplate(ui.slashInviteSent, { email }),
      };
      this.notice.set(notice);
      return { ok: true, clearDraft: true, notice };
    } catch (err) {
      const status = (err as { status?: number })?.status;
      if (status === 403) {
        return this.fail(ui.slashInviteForbidden, false);
      }
      return this.fail(this.errorMessage(err, ui.slashInviteError), false);
    }
  }

  private async runResumir(workspaceId: string, channelId: string): Promise<SlashExecResult> {
    if (this.channels.isDemo()) {
      const notice: SlashNotice = {
        kind: 'summary',
        text: ui.slashSummarizeDemo,
      };
      this.notice.set(notice);
      return { ok: true, clearDraft: true, notice };
    }

    try {
      const result = await this.api.summarizeChannel(workspaceId, channelId);
      const notice: SlashNotice = { kind: 'summary', text: result.summary };
      this.notice.set(notice);
      return { ok: true, clearDraft: true, notice };
    } catch (err) {
      const status = (err as { status?: number })?.status;
      const raw = err instanceof Error ? err.message : '';
      if (status === 503 || /AiDisabled/i.test(raw)) {
        return this.fail(ui.slashAiDisabled, false);
      }
      return this.fail(this.errorMessage(err, ui.slashAiUnavailable), false);
    }
  }

  private async runApagar(): Promise<SlashExecResult> {
    const lastOwn = [...this.messages.messages()]
      .reverse()
      .find((m) => m.mine && !m.deletedAt);

    if (!lastOwn) {
      return this.fail(ui.slashNoRecentToDelete, false);
    }

    const confirmed =
      typeof globalThis.confirm === 'function'
        ? globalThis.confirm(ui.slashDeleteConfirm)
        : true;
    if (!confirmed) {
      return { ok: false, clearDraft: false, notice: null };
    }

    try {
      await this.messages.remove(lastOwn.id);
      const notice: SlashNotice = { kind: 'info', text: ui.slashDeleted };
      this.notice.set(notice);
      return { ok: true, clearDraft: true, notice };
    } catch (err) {
      return this.fail(this.errorMessage(err, ui.slashDeleteError), false);
    }
  }

  private runEnquete(parsed: ParsedSlashCommand, isDirect: boolean): SlashExecResult {
    if (isDirect) {
      return this.fail(ui.slashPollChannelsOnly, false);
    }

    const parts = parsed.argsRaw
      .split('|')
      .map((part) => part.trim())
      .filter(Boolean);
    if (parts.length >= 3) {
      const [question, ...options] = parts;
      if (
        question.length > 500 ||
        options.length > 10 ||
        options.some((option) => option.length > 100)
      ) {
        return this.fail(ui.slashPollHint, false);
      }
      this.notice.set(null);
      return {
        ok: true,
        clearDraft: true,
        notice: null,
        openPollComposer: true,
        pollQuestion: question,
        pollOptions: options,
      };
    }

    this.notice.set(null);
    return {
      ok: true,
      clearDraft: true,
      notice: null,
      openPollComposer: true,
      pollQuestion: parts[0] ?? '',
    };
  }

  private fail(text: string, clearDraft: boolean): SlashExecResult {
    const notice: SlashNotice = { kind: 'error', text };
    this.notice.set(notice);
    return { ok: false, clearDraft, notice };
  }

  private errorMessage(err: unknown, fallback: string): string {
    if (!(err instanceof Error) || !err.message) return fallback;
    try {
      const parsed = JSON.parse(err.message) as { error?: string };
      if (parsed?.error && typeof parsed.error === 'string') {
        if (parsed.error === 'AiDisabled') {
          return ui.slashAiDisabled;
        }
        return translateErrorCode(parsed.error);
      }
    } catch {
      /* not JSON */
    }
    if (err.message.length < 180) return err.message;
    return fallback;
  }
}
