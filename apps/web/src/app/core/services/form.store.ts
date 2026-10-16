import { Injectable, inject, signal } from '@angular/core';
import { translateErrorCode, ui } from '../i18n/strings';
import {
  FormApiService,
  FormDetail,
  FormListItem,
  FormSchema,
  FormSubmission,
} from '../api/form-api.service';
import { ChannelStore } from './channel.store';

@Injectable({ providedIn: 'root' })
export class FormStore {
  private readonly api = inject(FormApiService);
  private readonly channels = inject(ChannelStore);
  private readonly panelOpenSignal = signal(false);
  private readonly itemsSignal = signal<FormListItem[]>([]);
  private readonly selectedSignal = signal<FormDetail | null>(null);
  private readonly submissionsSignal = signal<FormSubmission[]>([]);
  private readonly loadingSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);
  private readonly saveErrorSignal = signal<string | null>(null);
  private readonly savingSignal = signal(false);
  private draftKey = crypto.randomUUID();
  private submitKey = crypto.randomUUID();

  readonly panelOpen = this.panelOpenSignal.asReadonly();
  readonly items = this.itemsSignal.asReadonly();
  readonly selected = this.selectedSignal.asReadonly();
  readonly submissions = this.submissionsSignal.asReadonly();
  readonly loading = this.loadingSignal.asReadonly();
  readonly error = this.errorSignal.asReadonly();
  readonly saveError = this.saveErrorSignal.asReadonly();
  readonly saving = this.savingSignal.asReadonly();

  openPanel(): void {
    this.panelOpenSignal.set(true);
    this.selectedSignal.set(null);
    void this.reload();
  }

  closePanel(): void {
    this.panelOpenSignal.set(false);
  }

  back(): void {
    this.selectedSignal.set(null);
    this.submissionsSignal.set([]);
    this.saveErrorSignal.set(null);
    void this.reload();
  }

  async reload(): Promise<void> {
    const workspaceId = this.channels.activeWorkspace()?.id;
    const channelId = this.channels.activeChannelId();
    if (!workspaceId || !channelId || this.channels.isDemo()) {
      this.itemsSignal.set([]);
      return;
    }
    this.loadingSignal.set(true);
    this.errorSignal.set(null);
    try {
      this.itemsSignal.set(await this.api.list(workspaceId, channelId));
    } catch {
      this.errorSignal.set(ui.formLoadError);
    } finally {
      this.loadingSignal.set(false);
    }
  }

  async open(id: string): Promise<void> {
    const workspaceId = this.workspaceId();
    if (!workspaceId) return;
    this.loadingSignal.set(true);
    this.saveErrorSignal.set(null);
    this.submitKey = crypto.randomUUID();
    try {
      const detail = await this.api.get(workspaceId, id);
      this.selectedSignal.set(detail);
      this.submissionsSignal.set(await this.api.submissions(workspaceId, id));
    } catch {
      this.errorSignal.set(ui.formLoadError);
    } finally {
      this.loadingSignal.set(false);
    }
  }

  async save(schema: FormSchema, title: string, publish: boolean): Promise<boolean> {
    const workspaceId = this.workspaceId();
    const channelId = this.channels.activeChannelId();
    if (!workspaceId || !channelId) return false;
    this.savingSignal.set(true);
    this.saveErrorSignal.set(null);
    try {
      const current = this.selectedSignal();
      const detail = current
        ? await this.api.update(workspaceId, current.id, { title, schema })
        : await this.api.create(workspaceId, {
            idempotencyKey: this.draftKey,
            channelId,
            title,
            schema,
          });
      const published = publish ? await this.api.publish(workspaceId, detail.id) : detail;
      this.selectedSignal.set(published);
      this.draftKey = crypto.randomUUID();
      this.submissionsSignal.set(await this.api.submissions(workspaceId, published.id));
      return true;
    } catch (error) {
      this.saveErrorSignal.set(message(error));
      return false;
    } finally {
      this.savingSignal.set(false);
    }
  }

  async submit(answers: Record<string, string | number>): Promise<boolean> {
    const workspaceId = this.workspaceId();
    const form = this.selectedSignal();
    if (!workspaceId || !form) return false;
    this.savingSignal.set(true);
    this.saveErrorSignal.set(null);
    try {
      await this.api.submit(workspaceId, form.id, { idempotencyKey: this.submitKey, answers });
      this.submitKey = crypto.randomUUID();
      this.submissionsSignal.set(await this.api.submissions(workspaceId, form.id));
      return true;
    } catch (error) {
      this.saveErrorSignal.set(message(error));
      return false;
    } finally {
      this.savingSignal.set(false);
    }
  }

  async decide(submissionId: string, decision: 'approve' | 'reject'): Promise<void> {
    const workspaceId = this.workspaceId();
    const form = this.selectedSignal();
    if (!workspaceId || !form) return;
    this.saveErrorSignal.set(null);
    try {
      await this.api.decide(workspaceId, form.id, submissionId, {
        idempotencyKey: crypto.randomUUID(),
        decision,
      });
      this.submissionsSignal.set(await this.api.submissions(workspaceId, form.id));
    } catch (error) {
      this.saveErrorSignal.set(message(error));
    }
  }

  async archive(): Promise<void> {
    const workspaceId = this.workspaceId();
    const form = this.selectedSignal();
    if (!workspaceId || !form) return;
    try {
      this.selectedSignal.set(await this.api.archive(workspaceId, form.id));
    } catch (error) {
      this.saveErrorSignal.set(message(error));
    }
  }

  private workspaceId(): string | null {
    return this.channels.isDemo() ? null : this.channels.activeWorkspace()?.id ?? null;
  }
}

function message(error: unknown): string {
  if (!(error instanceof Error) || !error.message) return ui.formSaveError;
  try {
    const parsed = JSON.parse(error.message) as { error?: string };
    return parsed.error ? translateErrorCode(parsed.error) : ui.formSaveError;
  } catch {
    return ui.formSaveError;
  }
}
