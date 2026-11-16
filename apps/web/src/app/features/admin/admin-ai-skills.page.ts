import { Component, inject, OnInit, signal } from '@angular/core';
import { AiSkillApiService, AiSkillComposition, AiSkillDetail, AiSkillDiff, AiSkillDraft, AiSkillSummary } from '../../core/api/ai-skill-api.service';
import { ui } from '../../core/i18n/strings';
import { AdminContextService } from './admin-context.service';
import { AdminAreaId } from './admin-permissions';

@Component({
  selector: 'vc-admin-ai-skills',
  standalone: true,
  templateUrl: './admin-ai-skills.page.html',
  styleUrl: './admin-shared.scss',
})
export class AdminAiSkillsPage implements OnInit {
  readonly areaId: AdminAreaId = 'aiSkills';
  readonly ui = ui;
  readonly classifications = ['public', 'internal', 'confidential', 'restricted'];

  private readonly api = inject(AiSkillApiService);
  readonly ctx = inject(AdminContextService);

  readonly loading = signal(true);
  readonly disabled = signal(false);
  readonly denied = signal(false);
  readonly loadError = signal(false);
  readonly actionError = signal('');
  readonly saved = signal(false);
  readonly skills = signal<AiSkillSummary[]>([]);
  readonly selected = signal<AiSkillDetail | null>(null);
  readonly diff = signal<AiSkillDiff | null>(null);
  readonly composition = signal<AiSkillComposition | null>(null);
  readonly manifest = signal('');
  readonly importBody = signal('');
  readonly picked = signal<string[]>([]);
  readonly draft = signal<AiSkillDraft>(emptyDraft());
  readonly schemaText = signal('{"type":"object"}');
  readonly examplesText = signal('[]');

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
      this.skills.set(await this.api.list(workspaceId));
    } catch (error) {
      this.fail(error, true);
    } finally {
      this.loading.set(false);
    }
  }

  startCreate(): void {
    this.selected.set(null);
    this.diff.set(null);
    this.composition.set(null);
    this.manifest.set('');
    this.draft.set(emptyDraft());
    this.schemaText.set('{"type":"object"}');
    this.examplesText.set('[]');
    this.actionError.set('');
    this.saved.set(false);
  }

  async open(skillId: string): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId) return;
    this.actionError.set('');
    try {
      const detail = await this.api.get(workspaceId, skillId);
      this.apply(detail);
    } catch (error) {
      this.fail(error, false);
    }
  }

  async save(): Promise<void> {
    if (!this.applyTextFields()) return;
    const current = this.selected();
    if (!current) {
      await this.mutate((workspaceId, key) => this.api.create(workspaceId, this.draft(), key));
      return;
    }
    await this.mutate((workspaceId, key) => this.api.update(workspaceId, current.id, this.draft(), current.revision, key));
  }

  async publish(): Promise<void> {
    if (!this.applyTextFields()) return;
    const current = this.selected();
    if (!current) return;
    await this.mutate((workspaceId, key) => this.api.publish(workspaceId, current.id, this.draft(), current.revision, key));
  }

  async deprecate(): Promise<void> {
    const current = this.selected();
    if (!current) return;
    await this.mutate((workspaceId, key) => this.api.deprecate(workspaceId, current.id, current.revision, key));
  }

  async clone(versionNumber: number): Promise<void> {
    const current = this.selected();
    if (!current) return;
    await this.mutate((workspaceId, key) => this.api.clone(workspaceId, current.id, versionNumber, key));
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

  async exportManifest(): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    const current = this.selected();
    if (!workspaceId || !current || current.activeVersion === null) return;
    this.actionError.set('');
    try {
      this.manifest.set(JSON.stringify(await this.api.export(workspaceId, current.id, current.activeVersion), null, 2));
    } catch (error) {
      this.fail(error, false);
    }
  }

  async importManifest(): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId) return;
    this.actionError.set('');
    this.saved.set(false);
    try {
      const detail = await this.api.import(workspaceId, JSON.parse(this.importBody()) as unknown, crypto.randomUUID());
      this.apply(detail);
      this.saved.set(true);
      this.skills.set(await this.api.list(workspaceId));
    } catch (error) {
      this.fail(error, false);
    }
  }

  async compose(): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId) return;
    this.actionError.set('');
    try {
      this.composition.set(await this.api.compose(workspaceId, this.picked()));
    } catch (error) {
      this.fail(error, false);
    }
  }

  togglePick(versionId: string, checked: boolean): void {
    const current = this.picked().filter((id) => id !== versionId);
    this.picked.set(checked ? [...current, versionId] : current);
  }

  movePick(versionId: string, direction: -1 | 1): void {
    const current = [...this.picked()];
    const index = current.indexOf(versionId);
    const next = index + direction;
    if (index < 0 || next < 0 || next >= current.length) return;
    const [item] = current.splice(index, 1);
    current.splice(next, 0, item);
    this.picked.set(current);
  }

  statusLabel(status: string): string {
    switch (status) {
      case 'Published': return ui.aiBotStatusPublished;
      case 'Deprecated': return ui.aiSkillStatusDeprecated;
      default: return ui.aiBotStatusDraft;
    }
  }

  layerLabel(layer: string): string {
    switch (layer) {
      case 'platform': return ui.aiSkillLayerPlatform;
      case 'workspace': return ui.aiSkillLayerWorkspace;
      case 'bot': return ui.aiSkillLayerBot;
      default: return ui.aiSkillLayerSkill;
    }
  }

  patch(partial: Partial<AiSkillDraft>): void {
    this.draft.set({ ...this.draft(), ...partial });
  }

  inputValue(event: Event): string {
    return (event.target as HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement).value;
  }

  checked(event: Event): boolean {
    return (event.target as HTMLInputElement).checked;
  }

  private apply(detail: AiSkillDetail): void {
    this.selected.set(detail);
    this.draft.set({
      ...detail.draft,
      examples: detail.draft.examples ?? [],
      knowledgeGrantIds: [...(detail.draft.knowledgeGrantIds ?? [])],
      mcpToolGrantIds: [...(detail.draft.mcpToolGrantIds ?? [])],
    });
    this.schemaText.set(JSON.stringify(detail.draft.argumentsSchema ?? { type: 'object' }));
    this.examplesText.set(JSON.stringify(detail.draft.examples ?? []));
    this.diff.set(null);
    this.manifest.set('');
    this.saved.set(false);
  }

  private applyTextFields(): boolean {
    try {
      const schema = JSON.parse(this.schemaText()) as { type?: string };
      const examples = JSON.parse(this.examplesText()) as AiSkillDraft['examples'];
      this.patch({ argumentsSchema: schema, examples });
      return true;
    } catch {
      this.actionError.set(ui.aiSkillRejected);
      return false;
    }
  }

  private async mutate(action: (workspaceId: string, key: string) => Promise<AiSkillDetail>): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId) return;
    this.actionError.set('');
    this.saved.set(false);
    try {
      this.apply(await action(workspaceId, crypto.randomUUID()));
      this.saved.set(true);
      this.skills.set(await this.api.list(workspaceId));
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
      else this.actionError.set(ui.aiSkillDenied);
      return;
    }
    if (message.includes('SkillRevisionConflict') || message.includes('SkillSlugVersionConflict')) {
      this.actionError.set(ui.aiBotConflict);
      return;
    }
    if (message.includes('Skill') && (message.includes('Rejected') || message.includes('Invalid') || message.includes('Unknown'))) {
      this.actionError.set(ui.aiSkillRejected);
      return;
    }
    if (loading) this.loadError.set(true);
    else this.actionError.set(ui.aiSkillError);
  }
}

function emptyDraft(): AiSkillDraft {
  return {
    slug: '',
    name: '',
    description: '',
    instructions: '',
    argumentsSchema: { type: 'object' },
    examples: [],
    compatibility: 'bots.v1',
    classification: 'internal',
    knowledgeGrantIds: [],
    mcpToolGrantIds: [],
  };
}
