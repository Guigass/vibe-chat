import { Component, inject, OnInit, signal } from '@angular/core';
import { BotKnowledgeAnswer, BotKnowledgeApiService, BotKnowledgeDetail, BotKnowledgeDraft, BotKnowledgeSummary } from '../../core/api/bot-knowledge-api.service';
import { ui } from '../../core/i18n/strings';
import { AdminContextService } from './admin-context.service';
import { AdminAreaId } from './admin-permissions';

@Component({
  selector: 'vc-admin-ai-knowledge',
  standalone: true,
  templateUrl: './admin-ai-knowledge.page.html',
  styleUrl: './admin-shared.scss',
})
export class AdminAiKnowledgePage implements OnInit {
  readonly areaId: AdminAreaId = 'aiKnowledge';
  readonly ui = ui;
  readonly classifications = ['public', 'internal', 'confidential', 'restricted'];
  readonly kinds = ['file', 'page', 'url'] as const;

  private readonly api = inject(BotKnowledgeApiService);
  readonly ctx = inject(AdminContextService);

  readonly loading = signal(true);
  readonly disabled = signal(false);
  readonly denied = signal(false);
  readonly loadError = signal(false);
  readonly actionError = signal('');
  readonly saved = signal(false);
  readonly sources = signal<BotKnowledgeSummary[]>([]);
  readonly selected = signal<BotKnowledgeDetail | null>(null);
  readonly answer = signal<BotKnowledgeAnswer | null>(null);
  readonly draft = signal<BotKnowledgeDraft>(emptyDraft());
  readonly botId = signal('');
  readonly question = signal('');

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
      this.sources.set(await this.api.list(workspaceId));
    } catch (error) {
      this.fail(error, true);
    } finally {
      this.loading.set(false);
    }
  }

  startCreate(): void {
    this.selected.set(null);
    this.answer.set(null);
    this.draft.set(emptyDraft());
    this.actionError.set('');
    this.saved.set(false);
  }

  async open(sourceId: string): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId) return;
    this.actionError.set('');
    try {
      this.selected.set(await this.api.get(workspaceId, sourceId));
      this.answer.set(null);
    } catch (error) {
      this.fail(error, false);
    }
  }

  async save(): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId) return;
    this.actionError.set('');
    this.saved.set(false);
    try {
      this.selected.set(await this.api.create(workspaceId, this.payload(), crypto.randomUUID()));
      this.saved.set(true);
      this.sources.set(await this.api.list(workspaceId));
    } catch (error) {
      this.fail(error, false);
    }
  }

  async pause(): Promise<void> {
    await this.touch((workspaceId, sourceId, key) => this.api.pause(workspaceId, sourceId, key));
  }

  async resume(): Promise<void> {
    await this.touch((workspaceId, sourceId, key) => this.api.resume(workspaceId, sourceId, key));
  }

  async reindex(): Promise<void> {
    await this.touch((workspaceId, sourceId, key) => this.api.reindex(workspaceId, sourceId, key));
  }

  async remove(): Promise<void> {
    const current = this.selected();
    const workspaceId = this.ctx.workspace()?.id;
    if (!current || !workspaceId) return;
    this.actionError.set('');
    try {
      await this.api.remove(workspaceId, current.id, crypto.randomUUID());
      this.selected.set(null);
      this.answer.set(null);
      this.sources.set(await this.api.list(workspaceId));
    } catch (error) {
      this.fail(error, false);
    }
  }

  async grant(): Promise<void> {
    const current = this.selected();
    const workspaceId = this.ctx.workspace()?.id;
    if (!current || !workspaceId || !this.botId()) return;
    this.actionError.set('');
    try {
      await this.api.grant(workspaceId, current.id, this.botId(), crypto.randomUUID());
      this.selected.set(await this.api.get(workspaceId, current.id));
    } catch (error) {
      this.fail(error, false);
    }
  }

  async revoke(grantId: string): Promise<void> {
    const current = this.selected();
    const workspaceId = this.ctx.workspace()?.id;
    if (!current || !workspaceId) return;
    this.actionError.set('');
    try {
      await this.api.revoke(workspaceId, current.id, grantId, crypto.randomUUID());
      this.selected.set(await this.api.get(workspaceId, current.id));
      this.answer.set(null);
    } catch (error) {
      this.fail(error, false);
    }
  }

  async ask(): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId || !this.botId() || !this.question()) return;
    this.actionError.set('');
    try {
      this.answer.set(await this.api.ask(workspaceId, this.botId(), this.question()));
    } catch (error) {
      this.fail(error, false);
    }
  }

  private payload(): BotKnowledgeDraft {
    const draft = this.draft();
    return {
      ...draft,
      text: draft.kind === 'file' ? draft.text : '',
      url: draft.kind === 'url' ? draft.url : '',
      pageId: draft.kind === 'page' ? draft.pageId : '',
    };
  }

  patch(partial: Partial<BotKnowledgeDraft>): void {
    this.draft.set({ ...this.draft(), ...partial });
  }

  setKind(event: Event): void {
    const value = this.inputValue(event);
    const kind = value === 'page' || value === 'url' ? value : 'file';
    this.patch({ kind });
  }

  inputValue(event: Event): string {
    return (event.target as HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement).value;
  }

  kindLabel(kind: string): string {
    switch (kind) {
      case 'page': return ui.aiKnowledgeKindPage;
      case 'url': return ui.aiKnowledgeKindUrl;
      default: return ui.aiKnowledgeKindFile;
    }
  }

  statusLabel(status: string): string {
    switch (status) {
      case 'Ready': return ui.aiKnowledgeReady;
      case 'Failed': return ui.aiKnowledgeFailed;
      case 'Paused': return ui.aiKnowledgePaused;
      case 'Deleting': return ui.aiKnowledgeDeleting;
      default: return ui.aiKnowledgeWorking;
    }
  }

  private async touch(action: (workspaceId: string, sourceId: string, key: string) => Promise<BotKnowledgeDetail>): Promise<void> {
    const current = this.selected();
    const workspaceId = this.ctx.workspace()?.id;
    if (!current || !workspaceId) return;
    this.actionError.set('');
    try {
      this.selected.set(await action(workspaceId, current.id, crypto.randomUUID()));
      this.sources.set(await this.api.list(workspaceId));
    } catch (error) {
      this.fail(error, false);
    }
  }

  private fail(error: unknown, loading: boolean): void {
    const message = error instanceof Error ? error.message : '';
    if (message.includes('BotKnowledgeDisabled')) {
      this.disabled.set(true);
      return;
    }
    if (message.includes('403') || (error as { status?: number }).status === 403) {
      if (loading) this.denied.set(true);
      else this.actionError.set(ui.aiKnowledgeDenied);
      return;
    }
    if (loading) this.loadError.set(true);
    else this.actionError.set(ui.aiKnowledgeError);
  }
}

function emptyDraft(): BotKnowledgeDraft {
  return { name: '', classification: 'internal', kind: 'file', text: '', url: '', pageId: '' };
}
