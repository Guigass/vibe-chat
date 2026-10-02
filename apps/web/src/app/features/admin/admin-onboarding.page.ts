import { Component, ElementRef, inject, OnInit, signal, viewChild } from '@angular/core';
import { ApiService } from '../../core/api/api.service';
import { fillTemplate, translateErrorCode, ui } from '../../core/i18n/strings';
import {
  OnboardingItem,
  TemplatePlan,
  TemplatePlanItem,
  WorkspaceOnboardingState,
  WorkspaceTemplateSummary,
} from '../../shared/models/chat.models';
import { AdminContextService } from './admin-context.service';
import { AdminAreaId } from './admin-permissions';

type WizardStep = 'choose' | 'preview' | 'conflicts' | 'applying' | 'summary';
type TemplateSelection = { templateId: string } | { manifest: Record<string, unknown> };

@Component({
  selector: 'vc-admin-onboarding',
  standalone: true,
  templateUrl: './admin-onboarding.page.html',
  styleUrls: ['./admin-shared.scss', './admin-onboarding.page.scss'],
})
export class AdminOnboardingPage implements OnInit {
  readonly areaId: AdminAreaId = 'onboarding';
  readonly ui = ui;

  private readonly api = inject(ApiService);
  readonly ctx = inject(AdminContextService);
  private readonly stepHeading = viewChild<ElementRef<HTMLElement>>('stepHeading');

  readonly loading = signal(true);
  readonly loadError = signal(false);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly builtins = signal<WorkspaceTemplateSummary[]>([]);
  readonly selectedId = signal('team');
  readonly manifestText = signal('');
  readonly step = signal<WizardStep>('choose');
  readonly plan = signal<TemplatePlan | null>(null);
  readonly onboarding = signal<WorkspaceOnboardingState | null>(null);

  private selection: TemplateSelection = { templateId: 'team' };
  private applyKey = '';
  private started = false;

  async ngOnInit(): Promise<void> {
    if (this.started) {
      return;
    }

    this.started = true;
    await this.ctx.ensureReady();
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId) {
      this.loading.set(false);
      this.loadError.set(true);
      return;
    }

    try {
      const [catalog, onboarding] = await Promise.all([
        this.api.listWorkspaceTemplates(workspaceId),
        this.api.getOnboarding(workspaceId),
      ]);
      this.builtins.set(catalog.builtins);
      const first = catalog.builtins[0]?.id;
      if (first) {
        this.selectedId.set(first);
        this.selection = { templateId: first };
      }
      this.onboarding.set(onboarding);
    } catch {
      this.loadError.set(true);
    } finally {
      this.loading.set(false);
    }
  }

  templateName(id: string): string {
    switch (id) {
      case 'team':
        return ui.adminTemplateTeam;
      case 'project':
        return ui.adminTemplateProject;
      case 'community':
        return ui.adminTemplateCommunity;
      case 'incidents':
        return ui.adminTemplateIncidents;
      default:
        return id;
    }
  }

  templateLead(id: string): string {
    switch (id) {
      case 'team':
        return ui.adminTemplateTeamLead;
      case 'project':
        return ui.adminTemplateProjectLead;
      case 'community':
        return ui.adminTemplateCommunityLead;
      case 'incidents':
        return ui.adminTemplateIncidentsLead;
      default:
        return '';
    }
  }

  checklistLabel(key: string): string {
    switch (key) {
      case 'invite-members':
        return ui.adminChecklistInviteMembers;
      case 'review-channels':
        return ui.adminChecklistReviewChannels;
      case 'confirm-policy':
        return ui.adminChecklistConfirmPolicy;
      default:
        return key;
    }
  }

  kindLabel(kind: string): string {
    switch (kind) {
      case 'space':
        return ui.adminOnboardingKindSpace;
      case 'channel':
        return ui.adminOnboardingKindChannel;
      case 'policy':
        return ui.adminOnboardingKindPolicy;
      default:
        return kind;
    }
  }

  actionLabel(action: string): string {
    switch (action) {
      case 'create':
        return ui.adminOnboardingWillCreate;
      case 'reuse':
        return ui.adminOnboardingWillReuse;
      case 'conflict':
        return ui.adminOnboardingConflictItem;
      default:
        return action;
    }
  }

  conflictItems(): TemplatePlanItem[] {
    return (this.plan()?.items ?? []).filter((item) => item.action === 'conflict');
  }

  detailLabel(detail: string | null | undefined): string {
    switch (detail) {
      case 'type':
        return ui.adminOnboardingConflictType;
      case 'space':
        return ui.adminOnboardingConflictSpace;
      case 'policy':
        return ui.adminOnboardingConflictPolicy;
      default:
        return '';
    }
  }

  async previewSelected(): Promise<void> {
    const templateId = this.selectedId();
    if (!templateId) {
      return;
    }

    this.selection = { templateId };
    await this.preview(this.selection);
  }

  async previewImport(): Promise<void> {
    const raw = this.manifestText().trim();
    if (!raw) {
      return;
    }

    let manifest: Record<string, unknown>;
    try {
      const parsed: unknown = JSON.parse(raw);
      if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
        this.error.set(ui.adminOnboardingValidateError);
        return;
      }
      manifest = parsed as Record<string, unknown>;
    } catch {
      this.error.set(ui.adminOnboardingValidateError);
      return;
    }

    this.selection = { manifest };
    await this.preview(this.selection);
  }

  async apply(): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId || this.busy()) {
      return;
    }

    this.busy.set(true);
    this.error.set('');
    this.step.set('applying');
    this.focusStep();
    try {
      const result = await this.api.applyWorkspaceTemplate(
        workspaceId,
        this.selection,
        this.applyKey || crypto.randomUUID(),
      );
      this.plan.set(result);
      if (result.hasConflicts) {
        this.step.set('conflicts');
      } else {
        this.step.set('summary');
        this.onboarding.set(await this.api.getOnboarding(workspaceId));
      }
      this.focusStep();
    } catch (error) {
      const code = readError(error).code;
      this.error.set(this.describe(error));
      this.step.set(code === 'TemplateConflict' ? 'conflicts' : 'preview');
    } finally {
      this.busy.set(false);
    }
  }

  back(): void {
    this.error.set('');
    this.step.set('choose');
    this.focusStep();
  }

  async skip(): Promise<void> {
    await this.saveStatus('skipped');
  }

  async resume(): Promise<void> {
    await this.saveStatus('in_progress');
    this.step.set('choose');
    this.focusStep();
  }

  async toggleItem(item: OnboardingItem, done: boolean): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId || this.busy()) {
      return;
    }

    this.busy.set(true);
    this.error.set('');
    try {
      this.onboarding.set(await this.api.updateOnboarding(workspaceId, {
        items: [{ key: item.key, state: done ? 'done' : 'open' }],
      }));
    } catch (error) {
      this.error.set(this.describe(error));
    } finally {
      this.busy.set(false);
    }
  }

  async exportTemplate(): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId || this.busy()) {
      return;
    }

    this.busy.set(true);
    this.error.set('');
    try {
      const manifest = await this.api.exportWorkspaceTemplate(workspaceId);
      const blob = new Blob([JSON.stringify(manifest, null, 2)], { type: 'application/json' });
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = 'workspace-template.json';
      link.click();
      URL.revokeObjectURL(url);
    } catch (error) {
      this.error.set(this.describe(error));
    } finally {
      this.busy.set(false);
    }
  }

  private async preview(selection: TemplateSelection): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId || this.busy()) {
      return;
    }

    this.busy.set(true);
    this.error.set('');
    try {
      const plan = await this.api.previewWorkspaceTemplate(workspaceId, selection);
      this.plan.set(plan);
      this.applyKey = crypto.randomUUID();
      this.step.set(plan.hasConflicts ? 'conflicts' : 'preview');
      this.focusStep();
    } catch (error) {
      this.error.set(this.describe(error));
    } finally {
      this.busy.set(false);
    }
  }

  private async saveStatus(status: string): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId || this.busy()) {
      return;
    }

    this.busy.set(true);
    this.error.set('');
    try {
      this.onboarding.set(await this.api.updateOnboarding(workspaceId, { status }));
    } catch (error) {
      this.error.set(this.describe(error));
    } finally {
      this.busy.set(false);
    }
  }

  private describe(error: unknown): string {
    const parsed = readError(error);
    if (parsed.code === 'UnknownTemplateField') {
      return fillTemplate(ui.errorUnknownTemplateField, { path: parsed.path ?? '' });
    }

    const translated = translateErrorCode(parsed.code);
    return translated && translated !== parsed.code ? translated : ui.adminOnboardingApplyError;
  }

  private focusStep(): void {
    queueMicrotask(() => this.stepHeading()?.nativeElement.focus());
  }
}

function readError(error: unknown): { code: string; path?: string } {
  if (!(error instanceof Error) || !error.message) {
    return { code: '' };
  }

  try {
    const parsed: unknown = JSON.parse(error.message);
    if (!parsed || typeof parsed !== 'object') {
      return { code: '' };
    }

    const body = parsed as { error?: string; path?: string };
    return { code: body.error ?? '', path: body.path };
  } catch {
    return { code: '' };
  }
}
