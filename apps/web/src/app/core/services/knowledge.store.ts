import { Injectable, inject, signal } from '@angular/core';
import { translateErrorCode, ui } from '../i18n/strings';
import {
  KnowledgeApiService,
  KnowledgeCollection,
  KnowledgePage,
  KnowledgePageSummary,
  KnowledgeVersion,
} from '../api/knowledge-api.service';
import { ChannelStore } from './channel.store';

interface LocalDraft {
  version: number;
  title: string;
  body: string;
}

@Injectable({ providedIn: 'root' })
export class KnowledgeStore {
  private readonly api = inject(KnowledgeApiService);
  private readonly channels = inject(ChannelStore);

  private readonly panelOpenSignal = signal(false);
  private readonly loadingSignal = signal(false);
  private readonly savingSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);
  private readonly conflictSignal = signal(false);
  private readonly draftLocalSignal = signal(false);
  private readonly canEditSignal = signal(false);
  private readonly canManageSignal = signal(false);
  private readonly collectionsSignal = signal<KnowledgeCollection[]>([]);
  private readonly pagesSignal = signal<KnowledgePageSummary[]>([]);
  private readonly resultsSignal = signal<KnowledgePageSummary[] | null>(null);
  private readonly selectedCollectionIdSignal = signal<string | null>(null);
  private readonly pageSignal = signal<KnowledgePage | null>(null);
  private readonly historySignal = signal<KnowledgeVersion[]>([]);
  private readonly titleSignal = signal('');
  private readonly bodySignal = signal('');

  readonly panelOpen = this.panelOpenSignal.asReadonly();
  readonly loading = this.loadingSignal.asReadonly();
  readonly saving = this.savingSignal.asReadonly();
  readonly error = this.errorSignal.asReadonly();
  readonly conflict = this.conflictSignal.asReadonly();
  readonly draftLocal = this.draftLocalSignal.asReadonly();
  readonly canEdit = this.canEditSignal.asReadonly();
  readonly canManage = this.canManageSignal.asReadonly();
  readonly collections = this.collectionsSignal.asReadonly();
  readonly pages = this.pagesSignal.asReadonly();
  readonly results = this.resultsSignal.asReadonly();
  readonly selectedCollectionId = this.selectedCollectionIdSignal.asReadonly();
  readonly page = this.pageSignal.asReadonly();
  readonly history = this.historySignal.asReadonly();
  readonly title = this.titleSignal.asReadonly();
  readonly body = this.bodySignal.asReadonly();

  openPanel(): void {
    this.panelOpenSignal.set(true);
    void this.reload();
  }

  closePanel(): void {
    this.panelOpenSignal.set(false);
  }

  async reload(): Promise<void> {
    const workspaceId = this.workspaceId();
    if (!workspaceId) {
      this.collectionsSignal.set([]);
      return;
    }
    this.loadingSignal.set(true);
    this.errorSignal.set(null);
    try {
      const listed = await this.api.listCollections(workspaceId);
      this.collectionsSignal.set(listed.items);
      this.canEditSignal.set(listed.canEdit);
      this.canManageSignal.set(listed.canManage);
    } catch (error) {
      this.errorSignal.set(this.message(error, ui.knowledgeLoadError));
    } finally {
      this.loadingSignal.set(false);
    }
  }

  async selectCollection(id: string): Promise<void> {
    this.selectedCollectionIdSignal.set(id);
    this.pageSignal.set(null);
    this.resultsSignal.set(null);
    const workspaceId = this.workspaceId();
    if (!workspaceId) return;
    try {
      this.pagesSignal.set(await this.api.listPages(workspaceId, id));
    } catch (error) {
      this.errorSignal.set(this.message(error, ui.knowledgeLoadError));
    }
  }

  async selectPage(id: string): Promise<void> {
    const workspaceId = this.workspaceId();
    if (!workspaceId) return;
    this.conflictSignal.set(false);
    this.errorSignal.set(null);
    try {
      const page = await this.api.getPage(workspaceId, id);
      this.pageSignal.set(page);
      this.historySignal.set(await this.api.history(workspaceId, id));
      const draft = this.readDraft(id);
      if (draft && draft.version === page.version) {
        this.titleSignal.set(draft.title);
        this.bodySignal.set(draft.body);
        this.draftLocalSignal.set(true);
      } else {
        this.clearDraft(id);
        this.titleSignal.set(page.title);
        this.bodySignal.set(page.body);
        this.draftLocalSignal.set(false);
      }
    } catch (error) {
      this.errorSignal.set(this.message(error, ui.knowledgeLoadError));
    }
  }

  setTitle(value: string): void {
    this.titleSignal.set(value);
    this.rememberDraft();
  }

  setBody(value: string): void {
    this.bodySignal.set(value);
    this.rememberDraft();
  }

  async createCollection(title: string, scope: 'workspace' | 'channel'): Promise<void> {
    const workspaceId = this.workspaceId();
    if (!workspaceId) return;
    const scopeId = scope === 'channel' ? this.channels.activeChannelId() : null;
    if (scope === 'channel' && !scopeId) {
      this.errorSignal.set(ui.knowledgeSaveError);
      return;
    }
    this.savingSignal.set(true);
    this.errorSignal.set(null);
    try {
      const created = await this.api.createCollection(workspaceId, {
        idempotencyKey: crypto.randomUUID(),
        title,
        scope,
        scopeId,
      });
      await this.reload();
      await this.selectCollection(created.id);
    } catch (error) {
      this.errorSignal.set(this.message(error, ui.knowledgeSaveError));
    } finally {
      this.savingSignal.set(false);
    }
  }

  async createPage(title: string): Promise<void> {
    const workspaceId = this.workspaceId();
    const collectionId = this.selectedCollectionIdSignal();
    if (!workspaceId || !collectionId) return;
    this.savingSignal.set(true);
    this.errorSignal.set(null);
    try {
      const page = await this.api.createPage(workspaceId, {
        idempotencyKey: crypto.randomUUID(),
        collectionId,
        title,
        body: '',
      });
      await this.selectCollection(collectionId);
      await this.selectPage(page.id);
    } catch (error) {
      this.errorSignal.set(this.message(error, ui.knowledgeSaveError));
    } finally {
      this.savingSignal.set(false);
    }
  }

  async save(): Promise<void> {
    const workspaceId = this.workspaceId();
    const page = this.pageSignal();
    if (!workspaceId || !page) return;
    this.savingSignal.set(true);
    this.errorSignal.set(null);
    try {
      const updated = await this.api.updatePage(workspaceId, page.id, {
        version: page.version,
        title: this.titleSignal(),
        body: this.bodySignal(),
      });
      this.clearDraft(page.id);
      this.draftLocalSignal.set(false);
      this.conflictSignal.set(false);
      await this.selectPage(updated.id);
    } catch (error) {
      if (this.code(error) === 'KnowledgeVersionConflict') {
        this.conflictSignal.set(true);
        this.errorSignal.set(ui.knowledgeConflict);
      } else {
        this.errorSignal.set(this.message(error, ui.knowledgeSaveError));
      }
    } finally {
      this.savingSignal.set(false);
    }
  }

  async reloadPage(): Promise<void> {
    const page = this.pageSignal();
    if (!page) return;
    this.clearDraft(page.id);
    await this.selectPage(page.id);
  }

  async restore(version: number): Promise<void> {
    const workspaceId = this.workspaceId();
    const page = this.pageSignal();
    if (!workspaceId || !page) return;
    this.savingSignal.set(true);
    try {
      await this.api.restore(workspaceId, page.id, page.version, version);
      this.clearDraft(page.id);
      await this.selectPage(page.id);
    } catch (error) {
      if (this.code(error) === 'KnowledgeVersionConflict') {
        this.conflictSignal.set(true);
      }
      this.errorSignal.set(this.message(error, ui.knowledgeSaveError));
    } finally {
      this.savingSignal.set(false);
    }
  }

  async removePage(): Promise<void> {
    const workspaceId = this.workspaceId();
    const page = this.pageSignal();
    const collectionId = this.selectedCollectionIdSignal();
    if (!workspaceId || !page || !collectionId) return;
    this.savingSignal.set(true);
    try {
      await this.api.removePage(workspaceId, page.id);
      this.clearDraft(page.id);
      this.pageSignal.set(null);
      await this.selectCollection(collectionId);
    } catch (error) {
      this.errorSignal.set(this.message(error, ui.knowledgeSaveError));
    } finally {
      this.savingSignal.set(false);
    }
  }

  async search(query: string): Promise<void> {
    const workspaceId = this.workspaceId();
    if (!workspaceId || query.trim().length < 2) {
      this.resultsSignal.set(null);
      return;
    }
    try {
      this.resultsSignal.set(await this.api.search(workspaceId, query.trim()));
    } catch (error) {
      this.errorSignal.set(this.message(error, ui.knowledgeLoadError));
    }
  }

  private workspaceId(): string | null {
    if (this.channels.isDemo()) return null;
    return this.channels.activeWorkspace()?.id ?? null;
  }

  private rememberDraft(): void {
    const page = this.pageSignal();
    if (!page) return;
    const draft: LocalDraft = { version: page.version, title: this.titleSignal(), body: this.bodySignal() };
    localStorage.setItem(this.draftKey(page.id), JSON.stringify(draft));
    this.draftLocalSignal.set(draft.title !== page.title || draft.body !== page.body);
  }

  private readDraft(pageId: string): LocalDraft | null {
    const raw = localStorage.getItem(this.draftKey(pageId));
    if (!raw) return null;
    try {
      const parsed = JSON.parse(raw) as LocalDraft;
      if (typeof parsed.version !== 'number' || typeof parsed.body !== 'string' || typeof parsed.title !== 'string') {
        return null;
      }
      return parsed;
    } catch {
      return null;
    }
  }

  private clearDraft(pageId: string): void {
    localStorage.removeItem(this.draftKey(pageId));
  }

  private draftKey(pageId: string): string {
    return `vc.knowledge.draft.${pageId}`;
  }

  private code(error: unknown): string | null {
    if (!(error instanceof Error) || !error.message) return null;
    try {
      const parsed = JSON.parse(error.message) as { error?: string };
      return parsed.error ?? null;
    } catch {
      return null;
    }
  }

  private message(error: unknown, fallback: string): string {
    const code = this.code(error);
    if (code === 'KnowledgeDisabled') return ui.knowledgeDisabled;
    if (code === 'KnowledgeVersionConflict') return ui.knowledgeConflict;
    return code ? translateErrorCode(code) || fallback : fallback;
  }
}
