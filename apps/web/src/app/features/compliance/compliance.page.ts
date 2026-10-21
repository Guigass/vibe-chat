import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ComplianceApiService, ComplianceGrant, CustodyEvent, DiscoveryCase, DiscoveryExport, LegalHold } from '../../core/api/compliance-api.service';
import { DlpPanel } from './dlp-panel';
import { SiemPanel } from './siem-panel';
import { PolicyPackPanel } from './policy-pack-panel';
import { DelegatedAdminPanel } from './delegated-admin-panel';
import { DirectoryApiService } from '../../core/api/directory-api.service';
import { ui, translateErrorCode } from '../../core/i18n/strings';
import { Workspace, WorkspaceMember } from '../../shared/models/chat.models';

@Component({
  selector: 'vc-compliance-page',
  standalone: true,
  imports: [RouterLink, DlpPanel, SiemPanel, PolicyPackPanel, DelegatedAdminPanel],
  template: `
    <main class="compliance" data-testid="compliance-page">
      <header class="compliance__bar">
        <a routerLink="/app">{{ ui.complianceBack }}</a>
        <h1>{{ ui.complianceTitle }}</h1>
      </header>
      <p class="compliance__lead">{{ ui.complianceLead }}</p>
      <p class="compliance__warn" role="note">{{ ui.complianceWarning }}</p>
      <vc-dlp-panel />
      <vc-siem-panel />
      <vc-policy-pack-panel />
      <vc-delegated-admin-panel />

      @if (loading()) {
        <p role="status">{{ ui.complianceLoading }}</p>
      } @else if (disabled()) {
        <p role="status" data-testid="compliance-disabled">{{ ui.complianceDisabled }}</p>
      } @else if (denied()) {
        <p role="alert" data-testid="compliance-denied">{{ ui.complianceDenied }}</p>
      } @else {
        @if (error()) {
          <p role="alert">{{ error() }}</p>
        }
        @if (canGrant()) {
          <section>
            <h2>{{ ui.complianceGrantTitle }}</h2>
            <form (submit)="grant($event)">
              <label>{{ ui.complianceGrantUser }}
                <select data-testid="compliance-grant-user" (change)="userId.set(value($event))">
                  <option value="">{{ ui.complianceGrantUser }}</option>
                  @for (member of members(); track member.userId) {
                    <option [value]="member.userId">{{ member.displayName }}</option>
                  }
                </select>
              </label>
              <label>{{ ui.complianceGrantHold }}
                <input type="radio" name="capability" value="hold" (change)="capability.set('hold')" [checked]="capability() === 'hold'" />
              </label>
              <label>{{ ui.complianceGrantDiscovery }}
                <input type="radio" name="capability" value="discovery" (change)="capability.set('discovery')" [checked]="capability() === 'discovery'" />
              </label>
              <label>{{ ui.complianceGrantDlp }}
                <input type="radio" name="capability" value="dlp" (change)="capability.set('dlp')" [checked]="capability() === 'dlp'" />
              </label>
              <label>{{ ui.complianceGrantSiem }}
                <input type="radio" name="capability" value="audit.export" (change)="capability.set('audit.export')" [checked]="capability() === 'audit.export'" />
              </label>
              <label>{{ ui.complianceGrantPolicy }}
                <input type="radio" name="capability" value="policy" (change)="capability.set('policy')" [checked]="capability() === 'policy'" />
              </label>
              <button type="submit" data-testid="compliance-grant">{{ ui.complianceGrantSave }}</button>
            </form>
            <ul>
              @for (row of grants(); track row.id) {
                <li>{{ row.capability }} · {{ row.userId }}</li>
              }
            </ul>
          </section>
        }
        @if (hold()) {
          <section>
            <h2>{{ ui.complianceHoldTitle }}</h2>
            <form (submit)="requestHold($event)">
              <label>{{ ui.complianceScope }}
                <select data-testid="compliance-scope" (change)="scope.set(value($event))">
                  <option value="workspace">{{ ui.complianceScopeWorkspace }}</option>
                  <option value="user">{{ ui.complianceScopeUser }}</option>
                  <option value="channel">{{ ui.complianceScopeChannel }}</option>
                  <option value="conversation">{{ ui.complianceScopeConversation }}</option>
                  <option value="interval">{{ ui.complianceScopeInterval }}</option>
                </select>
              </label>
              @if (scope() !== 'workspace' && scope() !== 'interval') {
                <label>{{ ui.complianceScope }}
                  <input data-testid="compliance-scope-id" [value]="scopeId()" (input)="scopeId.set(value($event))" />
                </label>
              }
              @if (scope() === 'interval') {
                <label>{{ ui.complianceScopeInterval }}
                  <input type="datetime-local" [value]="intervalStart()" (input)="intervalStart.set(value($event))" />
                  <input type="datetime-local" [value]="intervalEnd()" (input)="intervalEnd.set(value($event))" />
                </label>
              }
              <label>{{ ui.complianceReason }}
                <input data-testid="compliance-reason" [value]="reason()" (input)="reason.set(value($event))" />
              </label>
              <label>{{ ui.complianceExpiry }}
                <input type="datetime-local" [value]="expiresAt()" (input)="expiresAt.set(value($event))" />
              </label>
              <label><input type="checkbox" data-testid="compliance-confirm" (change)="confirmed.set(checked($event))" /> {{ ui.complianceConfirmPreserve }}</label>
              <label><input type="checkbox" data-testid="compliance-ack" (change)="acknowledged.set(checked($event))" /> {{ ui.complianceConfirmAudit }}</label>
              <button type="submit" [disabled]="!confirmed() || !acknowledged()">{{ ui.complianceRequest }}</button>
            </form>
            <ul>
              @for (item of holds(); track item.id) {
                <li>
                  <span>{{ item.status }} · {{ item.scopeKind }} · {{ item.reason }}</span>
                  @if (item.status === 'requested') {
                    <button type="button" (click)="approve(item.id)">{{ ui.complianceApprove }}</button>
                  }
                  @if (item.status === 'active') {
                    <button type="button" (click)="release(item.id)">{{ ui.complianceRelease }}</button>
                  }
                  @if (item.status === 'release_requested') {
                    <button type="button" (click)="approveRelease(item.id)">{{ ui.complianceReleaseApprove }}</button>
                  }
                </li>
              }
            </ul>
            <h3>{{ ui.complianceHistory }}</h3>
            <ul>
              @for (event of history(); track event.id) {
                <li>{{ event.action }} · {{ event.payloadHash }}</li>
              }
            </ul>
          </section>
        }
        @if (discovery()) {
          <section>
            <h2>{{ ui.complianceCaseTitle }}</h2>
            <form (submit)="createCase($event)">
              <label>{{ ui.complianceCaseName }}
                <input data-testid="compliance-case-title" [value]="caseTitle()" (input)="caseTitle.set(value($event))" />
              </label>
              <label>{{ ui.complianceReason }}
                <input [value]="caseReason()" (input)="caseReason.set(value($event))" />
              </label>
              <button type="submit">{{ ui.complianceCaseTitle }}</button>
            </form>
            <ul>
              @for (item of cases(); track item.id) {
                <li>
                  {{ item.title }}
                  <button type="button" (click)="exportCase(item.id)">{{ ui.complianceExport }}</button>
                </li>
              }
            </ul>
            @if (exported(); as result) {
              <p data-testid="compliance-export">{{ ui.complianceProgress }} {{ result.progress }} · {{ ui.complianceChecksum }} {{ result.checksum }}</p>
            }
          </section>
        }
      }
    </main>
  `,
  styles: `
    .compliance { max-width: 46rem; margin: 0 auto; padding: var(--vc-space-6); color: var(--vc-ink); font-family: var(--vc-font-body); }
    .compliance__bar, form, li { display: flex; gap: var(--vc-space-3); align-items: center; flex-wrap: wrap; }
    .compliance__warn { padding: var(--vc-space-3); border: 1px solid var(--vc-danger); border-radius: var(--vc-radius-md); }
    section { margin-top: var(--vc-space-6); }
    label { display: flex; flex-direction: column; gap: var(--vc-space-1); }
    button, input, select { font: inherit; }
  `,
})
export class CompliancePage implements OnInit {
  private readonly compliance = inject(ComplianceApiService);
  private readonly directory = inject(DirectoryApiService);
  protected readonly ui = ui;
  protected readonly loading = signal(true);
  protected readonly disabled = signal(false);
  protected readonly denied = signal(false);
  protected readonly error = signal('');
  protected readonly canGrant = signal(false);
  protected readonly hold = signal(false);
  protected readonly discovery = signal(false);
  protected readonly members = signal<WorkspaceMember[]>([]);
  protected readonly grants = signal<ComplianceGrant[]>([]);
  protected readonly holds = signal<LegalHold[]>([]);
  protected readonly history = signal<CustodyEvent[]>([]);
  protected readonly cases = signal<DiscoveryCase[]>([]);
  protected readonly exported = signal<DiscoveryExport | null>(null);
  protected readonly userId = signal('');
  protected readonly capability = signal<'hold' | 'discovery' | 'dlp' | 'audit.export' | 'policy'>('hold');
  protected readonly scope = signal('workspace');
  protected readonly scopeId = signal('');
  protected readonly intervalStart = signal('');
  protected readonly intervalEnd = signal('');
  protected readonly reason = signal('');
  protected readonly expiresAt = signal('');
  protected readonly confirmed = signal(false);
  protected readonly acknowledged = signal(false);
  protected readonly caseTitle = signal('');
  protected readonly caseReason = signal('');
  private workspaceId = '';

  async ngOnInit(): Promise<void> {
    try {
      const workspaces = await this.directory.getWorkspaces();
      const workspace = workspaces[0];
      if (!workspace) {
        this.denied.set(true);
        return;
      }
      this.workspaceId = workspace.id;
      await this.load(workspace);
    } catch {
      this.denied.set(true);
    } finally {
      this.loading.set(false);
    }
  }

  async grant(event: Event): Promise<void> {
    event.preventDefault();
    if (!this.userId()) {
      return;
    }
    await this.run(async () => {
      await this.compliance.grant(this.workspaceId, this.userId(), this.capability());
      this.grants.set(await this.compliance.listGrants(this.workspaceId));
    });
  }

  async requestHold(event: Event): Promise<void> {
    event.preventDefault();
    await this.run(async () => {
      await this.compliance.requestHold(this.workspaceId, {
        scopeKind: this.scope(),
        scopeId: this.scope() === 'workspace' || this.scope() === 'interval' ? null : this.scopeId(),
        intervalStart: this.intervalStart() ? new Date(this.intervalStart()).toISOString() : null,
        intervalEnd: this.intervalEnd() ? new Date(this.intervalEnd()).toISOString() : null,
        reason: this.reason(),
        expiresAt: this.expiresAt() ? new Date(this.expiresAt()).toISOString() : null,
        confirmed: this.confirmed(),
        acknowledged: this.acknowledged(),
      });
      await this.reloadHolds();
    });
  }

  async approve(holdId: string): Promise<void> {
    await this.run(async () => {
      await this.compliance.approveHold(this.workspaceId, holdId);
      await this.reloadHolds();
    });
  }

  async release(holdId: string): Promise<void> {
    await this.run(async () => {
      await this.compliance.requestRelease(this.workspaceId, holdId, this.reason());
      await this.reloadHolds();
    });
  }

  async approveRelease(holdId: string): Promise<void> {
    await this.run(async () => {
      await this.compliance.approveRelease(this.workspaceId, holdId);
      await this.reloadHolds();
    });
  }

  async createCase(event: Event): Promise<void> {
    event.preventDefault();
    await this.run(async () => {
      await this.compliance.createCase(this.workspaceId, this.caseTitle(), this.caseReason(), this.scopeId());
      this.cases.set(await this.compliance.listCases(this.workspaceId));
    });
  }

  async exportCase(caseId: string): Promise<void> {
    await this.run(async () => {
      this.exported.set(await this.compliance.exportCase(this.workspaceId, caseId));
    });
  }

  protected value(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected checked(event: Event): boolean {
    return (event.target as HTMLInputElement).checked;
  }

  private async load(workspace: Workspace): Promise<void> {
    try {
      const access = await this.compliance.access(workspace.id);
      this.canGrant.set(access.canGrant);
      this.hold.set(access.hold);
      this.discovery.set(access.discovery);
      if (!access.canGrant && !access.hold && !access.discovery) {
        this.denied.set(true);
        return;
      }
      if (access.canGrant) {
        this.members.set(await this.directory.getMembers(workspace.id));
        this.grants.set(await this.compliance.listGrants(workspace.id));
      }
      if (access.hold) {
        await this.reloadHolds();
      }
      if (access.discovery) {
        this.cases.set(await this.compliance.listCases(workspace.id));
      }
    } catch (error) {
      if (statusOf(error) === 404) {
        this.disabled.set(true);
        return;
      }
      this.denied.set(true);
    }
  }

  private async reloadHolds(): Promise<void> {
    const list = await this.compliance.listHolds(this.workspaceId);
    this.holds.set(list.holds);
    this.history.set(list.history);
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.error.set('');
    try {
      await action();
    } catch (error) {
      this.error.set(translateErrorCode(codeOf(error)) || ui.complianceError);
    }
  }
}

function statusOf(error: unknown): number {
  return typeof error === 'object' && error !== null && 'status' in error ? Number((error as { status: number }).status) : 0;
}

function codeOf(error: unknown): string {
  if (!(error instanceof Error)) {
    return '';
  }
  try {
    const parsed = JSON.parse(error.message) as { error?: string };
    return parsed.error ?? '';
  } catch {
    return '';
  }
}
