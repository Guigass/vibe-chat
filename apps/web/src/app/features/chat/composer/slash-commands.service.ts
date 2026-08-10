import { Injectable, inject, signal } from '@angular/core';
import { ApiService } from '../../../core/api/api.service';
import { ChannelStore } from '../../../core/services/channel.store';
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
}

const DEMO_COMMANDS: SlashCommandDef[] = [
  { name: 'dm', description: 'Abre ou cria uma DM', usage: '/dm @pessoa' },
  { name: 'topico', description: 'Altera a descrição do canal', usage: '/topico <texto>' },
  { name: 'convidar', description: 'Convida alguém para o workspace', usage: '/convidar <email>' },
  { name: 'resumir', description: 'Resume as mensagens recentes do canal', usage: '/resumir' },
  { name: 'apagar', description: 'Apaga a sua última mensagem neste canal', usage: '/apagar' },
  { name: 'ajuda', description: 'Lista os comandos disponíveis', usage: '/ajuda' },
];

@Injectable({ providedIn: 'root' })
export class SlashCommandsService {
  private readonly api = inject(ApiService);
  private readonly channels = inject(ChannelStore);
  private readonly messages = inject(MessageStore);

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
      return this.fail('Comando inválido.', false);
    }

    const workspace = this.channels.activeWorkspace();
    const channel = this.channels.activeChannel();
    if (!workspace || !channel) {
      return this.fail('Selecione um canal para usar comandos.', false);
    }

    const available = await this.listCommands(workspace.id);
    const known = available.find((c) => c.name === parsed.name);
    if (!known) {
      return this.fail(
        `Comando desconhecido: /${parsed.name}. Digite /ajuda para ver a lista.`,
        false,
      );
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
      default:
        return this.fail(`Comando desconhecido: /${parsed.name}.`, false);
    }
  }

  private runAjuda(commands: SlashCommandDef[]): SlashExecResult {
    const notice: SlashNotice = {
      kind: 'help',
      text: 'Comandos disponíveis',
      lines: commands.map((c) => `${c.usage} — ${c.description}`),
    };
    this.notice.set(notice);
    return { ok: true, clearDraft: true, notice };
  }

  private async runDm(parsed: ParsedSlashCommand): Promise<SlashExecResult> {
    const handle = parsed.argsRaw.replace(/^@/, '').trim();
    if (!handle) {
      return this.fail('Uso: /dm @pessoa', false);
    }

    const members = this.channels.members();
    const match = members.find(
      (m) =>
        m.displayName.toLowerCase() === handle.toLowerCase() ||
        m.email.toLowerCase() === handle.toLowerCase() ||
        m.displayName.toLowerCase().startsWith(handle.toLowerCase()),
    );

    if (!match) {
      return this.fail(`Não encontrei o membro "${handle}".`, false);
    }

    const channel = await this.channels.openDirectMessage(match.userId);
    if (!channel) {
      return this.fail('Não foi possível abrir a DM.', false);
    }

    const notice: SlashNotice = {
      kind: 'info',
      text: `DM aberta com ${match.displayName}.`,
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
      return this.fail('DMs não têm tópico.', false);
    }
    if (!parsed.argsRaw) {
      return this.fail('Uso: /topico <texto>', false);
    }
    if (parsed.argsRaw.length > 250) {
      return this.fail('O tópico deve ter no máximo 250 caracteres.', false);
    }

    if (this.channels.isDemo()) {
      this.channels.patchChannel(channelId, { description: parsed.argsRaw });
      const notice: SlashNotice = { kind: 'info', text: 'Tópico atualizado.' };
      this.notice.set(notice);
      return { ok: true, clearDraft: true, notice };
    }

    try {
      const updated = await this.api.updateChannelTopic(workspaceId, channelId, parsed.argsRaw);
      this.channels.patchChannel(channelId, { description: updated.description });
      const notice: SlashNotice = { kind: 'info', text: 'Tópico atualizado.' };
      this.notice.set(notice);
      return { ok: true, clearDraft: true, notice };
    } catch (err) {
      return this.fail(this.errorMessage(err, 'Não foi possível atualizar o tópico.'), false);
    }
  }

  private async runConvidar(
    parsed: ParsedSlashCommand,
    workspaceId: string,
  ): Promise<SlashExecResult> {
    const email = parsed.argsRaw.trim();
    if (!email || !email.includes('@')) {
      return this.fail('Uso: /convidar <email>', false);
    }

    if (this.channels.isDemo()) {
      const notice: SlashNotice = {
        kind: 'info',
        text: `Convite simulado para ${email}.`,
      };
      this.notice.set(notice);
      return { ok: true, clearDraft: true, notice };
    }

    try {
      await this.api.inviteMember(workspaceId, { email });
      const notice: SlashNotice = { kind: 'info', text: `Convite enviado para ${email}.` };
      this.notice.set(notice);
      return { ok: true, clearDraft: true, notice };
    } catch (err) {
      const status = (err as { status?: number })?.status;
      if (status === 403) {
        return this.fail('Você não tem permissão para convidar membros.', false);
      }
      return this.fail(this.errorMessage(err, 'Não foi possível enviar o convite.'), false);
    }
  }

  private async runResumir(workspaceId: string, channelId: string): Promise<SlashExecResult> {
    if (this.channels.isDemo()) {
      const notice: SlashNotice = {
        kind: 'summary',
        text: 'Resumo (demo): conversa recente sem conteúdo real.',
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
        return this.fail(
          'A IA está desligada neste workspace. Ative em Configurações para usar /resumir.',
          false,
        );
      }
      return this.fail(
        this.errorMessage(err, 'IA indisponível ou desabilitada para este workspace.'),
        false,
      );
    }
  }

  private async runApagar(): Promise<SlashExecResult> {
    const lastOwn = [...this.messages.messages()]
      .reverse()
      .find((m) => m.mine && !m.deletedAt);

    if (!lastOwn) {
      return this.fail('Você não tem mensagem recente para apagar neste canal.', false);
    }

    const confirmed =
      typeof globalThis.confirm === 'function'
        ? globalThis.confirm('Apagar a sua última mensagem neste canal?')
        : true;
    if (!confirmed) {
      return { ok: false, clearDraft: false, notice: null };
    }

    try {
      await this.messages.remove(lastOwn.id);
      const notice: SlashNotice = { kind: 'info', text: 'Mensagem apagada.' };
      this.notice.set(notice);
      return { ok: true, clearDraft: true, notice };
    } catch (err) {
      return this.fail(this.errorMessage(err, 'Não foi possível apagar a mensagem.'), false);
    }
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
          return 'A IA está desligada neste workspace. Ative em Configurações para usar /resumir.';
        }
        return parsed.error;
      }
    } catch {
      /* not JSON */
    }
    if (err.message.length < 180) return err.message;
    return fallback;
  }
}
