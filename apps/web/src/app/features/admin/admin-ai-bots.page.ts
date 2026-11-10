import { Component, inject, OnInit, signal } from '@angular/core';
import { AiBotApiService, AiBotDetail, AiBotDiff, AiBotDraft, AiBotSummary } from '../../core/api/ai-bot-api.service';
import { DirectoryApiService } from '../../core/api/directory-api.service';
import { fillTemplate, ui } from '../../core/i18n/strings';
import { Channel } from '../../shared/models/chat.models';
import { AdminContextService } from './admin-context.service';
import { AdminAreaId } from './admin-permissions';

type Step = 'identity' | 'personality' | 'capabilities' | 'limits' | 'review';

const STEPS: readonly Step[] = ['identity', 'personality', 'capabilities', 'limits', 'review'];

@Component({
  selector: 'vc-admin-ai-bots',
  standalone: true,
  templateUrl: './admin-ai-bots.page.html',
  styleUrl: './admin-shared.scss',
})
export class AdminAiBotsPage implements OnInit {
  readonly areaId: AdminAreaId = 'aiBots';
  readonly ui = ui;
  readonly steps = STEPS;
  readonly providers = ['none', 'mock', 'openrouter', 'local'];
  readonly dataClasses = ['public', 'internal', 'confidential', 'restricted'];

  private readonly api = inject(AiBotApiService);
  private readonly directory = inject(DirectoryApiService);
  readonly ctx = inject(AdminContextService);

  readonly loading = signal(true);
  readonly disabled = signal(false);
  readonly denied = signal(false);
  readonly loadError = signal(false);
  readonly actionError = signal('');
  readonly saved = signal(false);
  readonly bots = signal<AiBotSummary[]>([]);
  readonly channels = signal<Channel[]>([]);
  readonly selected = signal<AiBotDetail | null>(null);
  readonly diff = signal<AiBotDiff | null>(null);
  readonly step = signal<Step>('identity');
  readonly draft = signal<AiBotDraft>(emptyDraft());

  async ngOnInit(): Promise<void> {
    await this.reload();
  }

  async reload(): Promise<void> {
    await this.ctx.ensureReady();
    const workspaceId = this.ctx.workspace()?.id;
    this.loading.set(true);
    this.disabled.set(false);
    this.denied.set(false);
    this.loadError.set(false);
    if (!workspaceId) {
      this.loading.set(false);
      this.denied.set(true);
      return;
    }

    try {
      this.channels.set((await this.directory.getChannels(workspaceId)).filter((channel) => !channel.isDirect && !channel.isGroupDm));
      this.bots.set(await this.api.list(workspaceId));
    } catch (error) {
      this.fail(error, true);
    } finally {
      this.loading.set(false);
    }
  }

  startCreate(): void {
    this.selected.set(null);
    this.diff.set(null);
    this.draft.set(emptyDraft());
    this.step.set('identity');
    this.actionError.set('');
    this.saved.set(false);
  }

  async open(botId: string): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId) return;
    this.actionError.set('');
    try {
      const detail = await this.api.get(workspaceId, botId);
      this.selected.set(detail);
      this.draft.set({ ...detail.draft, channelIds: [...detail.draft.channelIds] });
      this.step.set('review');
      this.diff.set(null);
    } catch (error) {
      this.fail(error, false);
    }
  }

  patch(partial: Partial<AiBotDraft>): void {
    this.draft.update((current) => ({ ...current, ...partial }));
  }

  toggleChannel(channelId: string, checked: boolean): void {
    this.draft.update((current) => ({
      ...current,
      channelIds: checked ? [...current.channelIds, channelId] : current.channelIds.filter((id) => id !== channelId),
    }));
  }

  setReferences(field: 'skillVersionIds' | 'knowledgeGrantIds' | 'mcpToolGrantIds', value: string): void {
    const ids = value.split(/[\s,]+/).map((item) => item.trim()).filter((item) => item.length > 0);
    this.patch({ [field]: ids });
  }

  go(step: Step): void {
    this.step.set(step);
  }

  async save(): Promise<void> {
    await this.mutate((workspaceId, key) => {
      const current = this.selected();
      return current
        ? this.api.update(workspaceId, current.id, this.draft(), current.revision, key)
        : this.api.create(workspaceId, this.draft(), key);
    });
  }

  async publish(): Promise<void> {
    const current = this.selected();
    if (!current) {
      await this.save();
    }
    const saved = this.selected();
    if (!saved) return;
    await this.mutate((workspaceId, key) => this.api.publish(workspaceId, saved.id, this.draft(), saved.revision, key));
  }

  async disable(): Promise<void> {
    const current = this.selected();
    if (!current) return;
    await this.mutate((workspaceId, key) => this.api.disable(workspaceId, current.id, current.revision, key));
  }

  async rollback(versionNumber: number): Promise<void> {
    const current = this.selected();
    if (!current) return;
    await this.mutate((workspaceId, key) => this.api.rollback(workspaceId, current.id, current.revision, versionNumber, key));
  }

  async preview(): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    const current = this.selected();
    if (!workspaceId || !current) return;
    this.actionError.set('');
    try {
      const from = current.activeVersion ?? 0;
      this.diff.set(await this.api.diff(workspaceId, current.id, from, 0));
    } catch (error) {
      this.fail(error, false);
    }
  }

  stepLabel(step: Step): string {
    switch (step) {
      case 'identity': return ui.aiBotStepIdentity;
      case 'personality': return ui.aiBotStepPersonality;
      case 'capabilities': return ui.aiBotStepCapabilities;
      case 'limits': return ui.aiBotStepLimits;
      default: return ui.aiBotStepReview;
    }
  }

  statusLabel(status: string): string {
    switch (status) {
      case 'Published': return ui.aiBotStatusPublished;
      case 'Disabled': return ui.aiBotStatusDisabled;
      default: return ui.aiBotStatusDraft;
    }
  }

  rollbackNote(version: number): string {
    return fillTemplate(ui.aiBotRollbackImpact, { n: String(version) });
  }

  references(ids: string[]): string {
    return ids.join(', ');
  }

  inputValue(event: Event): string {
    return (event.target as HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement).value;
  }

  checked(event: Event): boolean {
    return (event.target as HTMLInputElement).checked;
  }

  numberValue(event: Event): number {
    return Number(this.inputValue(event));
  }

  private async mutate(action: (workspaceId: string, key: string) => Promise<AiBotDetail>): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId) return;
    this.actionError.set('');
    this.saved.set(false);
    try {
      const detail = await action(workspaceId, crypto.randomUUID());
      this.selected.set(detail);
      this.draft.set({ ...detail.draft, channelIds: [...detail.draft.channelIds] });
      this.saved.set(true);
      this.bots.set(await this.api.list(workspaceId));
    } catch (error) {
      this.fail(error, false);
    }
  }

  private fail(error: unknown, loading: boolean): void {
    const message = error instanceof Error ? error.message : '';
    if (message.includes('AiBotsDisabled')) {
      this.disabled.set(true);
      return;
    }
    if (message.includes('403') || (error as { status?: number }).status === 403) {
      if (loading) this.denied.set(true);
      else this.actionError.set(ui.aiBotDenied);
      return;
    }
    if (message.includes('BotRevisionConflict')) {
      this.actionError.set(ui.aiBotConflict);
      return;
    }
    if (message.includes('ProviderKeyRejected')) {
      this.actionError.set(ui.aiBotSecretRejected);
      return;
    }
    if (loading) this.loadError.set(true);
    else this.actionError.set(ui.aiBotError);
  }
}

function emptyDraft(): AiBotDraft {
  return {
    name: '',
    description: '',
    avatarKey: '',
    allowDms: false,
    systemPrompt: '',
    provider: 'none',
    modelId: 'none',
    parameters: { temperature: 0.2 },
    maxTokens: 1024,
    timeoutMs: 30000,
    fallbackModelId: null,
    dataClass: 'internal',
    channelIds: [],
    skillVersionIds: [],
    knowledgeGrantIds: [],
    mcpToolGrantIds: [],
    guardrailPolicyVersion: null,
    maxTurns: 4,
    maxInputTokens: 4096,
    maxOutputTokens: 1024,
    dailyBudgetUsd: 0,
  };
}
