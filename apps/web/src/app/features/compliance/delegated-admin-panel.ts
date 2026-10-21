import { Component, OnInit, inject, signal } from '@angular/core';
import {
  DelegatedAdminApiService,
  DelegatedAlert,
  DelegatedAssignment,
  DelegatedUsageRow,
} from '../../core/api/delegated-admin-api.service';
import { DirectoryApiService } from '../../core/api/directory-api.service';
import { translateErrorCode, ui } from '../../core/i18n/strings';
import { Space, WorkspaceMember } from '../../shared/models/chat.models';

@Component({
  selector: 'vc-delegated-admin-panel',
  standalone: true,
  template: `
    <section data-testid="delegated-admin-panel">
      <h2>{{ ui.delegatedAdminTitle }}</h2>
      <p role="note">{{ ui.delegatedAdminLead }}</p>
      @if (loading()) {
        <p role="status">{{ ui.complianceLoading }}</p>
      } @else if (disabled()) {
        <p role="status" data-testid="delegated-admin-disabled">{{ ui.delegatedAdminDisabled }}</p>
      } @else if (denied()) {
        <p role="status">{{ ui.complianceDenied }}</p>
      } @else {
        @if (notice()) {
          <p role="status">{{ notice() }}</p>
        }
        @if (error()) {
          <p role="alert">{{ error() }}</p>
        }
        <p>{{ ui.delegatedAdminScopes }} {{ areas().join(', ') }}</p>
        @if (canGrant()) {
          <form (submit)="grant($event)">
            <label>{{ ui.complianceGrantUser }}
              <select data-testid="delegated-admin-user" (change)="granteeId.set(text($event))">
                <option value="">{{ ui.complianceGrantUser }}</option>
                @for (member of members(); track member.userId) {
                  <option [value]="member.userId">{{ member.displayName }}</option>
                }
              </select>
            </label>
            <label>{{ ui.delegatedAdminScope }}
              <select data-testid="delegated-admin-scope" (change)="scope.set(text($event))">
                @for (item of scopes(); track item) {
                  <option [value]="item">{{ item }}</option>
                }
              </select>
            </label>
            @if (scope() === 'space') {
              <label>{{ ui.delegatedAdminSpace }}
                <select data-testid="delegated-admin-space" (change)="spaceId.set(text($event))">
                  @for (space of spaces(); track space.id) {
                    <option [value]="space.id">{{ space.name }}</option>
                  }
                </select>
              </label>
            }
            <label>{{ ui.delegatedAdminReason }}
              <input data-testid="delegated-admin-reason" [value]="reason()" (input)="reason.set(text($event))" />
            </label>
            <label>{{ ui.delegatedAdminBreakGlass }}
              <input type="checkbox" [checked]="breakGlass()" (change)="breakGlass.set(checked($event))" />
            </label>
            <button type="submit" data-testid="delegated-admin-grant">{{ ui.delegatedAdminGrant }}</button>
          </form>
        }
        @if (assignments().length === 0) {
          <p>{{ ui.delegatedAdminEmpty }}</p>
        }
        <ul>
          @for (row of assignments(); track row.id) {
            <li>
              {{ row.scope }} · {{ row.granteeId }}
              <button type="button" (click)="revoke(row.id)">{{ ui.delegatedAdminRevoke }}</button>
            </li>
          }
        </ul>
        <h3>{{ ui.delegatedAdminUsage }}</h3>
        <ul>
          @for (row of usage(); track row.resource) {
            <li>{{ row.resource }} · {{ row.used }} · {{ trendLabel(row.trend) }} · {{ actionLabel(row.action) }}</li>
          }
        </ul>
        <h3>{{ ui.delegatedAdminAlerts }}</h3>
        <ul>
          @for (row of alerts(); track row.id) {
            <li>{{ row.kind }} · {{ row.resource }}</li>
          }
        </ul>
      }
    </section>
  `,
})
export class DelegatedAdminPanel implements OnInit {
  private readonly admin = inject(DelegatedAdminApiService);
  private readonly directory = inject(DirectoryApiService);
  protected readonly ui = ui;
  protected readonly loading = signal(true);
  protected readonly disabled = signal(false);
  protected readonly denied = signal(false);
  protected readonly notice = signal('');
  protected readonly error = signal('');
  protected readonly areas = signal<string[]>([]);
  protected readonly canGrant = signal(false);
  protected readonly scopes = signal<string[]>([]);
  protected readonly members = signal<WorkspaceMember[]>([]);
  protected readonly spaces = signal<Space[]>([]);
  protected readonly assignments = signal<DelegatedAssignment[]>([]);
  protected readonly usage = signal<DelegatedUsageRow[]>([]);
  protected readonly alerts = signal<DelegatedAlert[]>([]);
  protected readonly granteeId = signal('');
  protected readonly scope = signal('workspace');
  protected readonly spaceId = signal('');
  protected readonly reason = signal('');
  protected readonly breakGlass = signal(false);
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
      const me = await this.admin.me(workspace.id);
      this.areas.set(me.areas);
      this.canGrant.set(me.canGrant);
      if (me.canGrant) {
        const catalog = await this.admin.catalog(workspace.id);
        this.scopes.set(catalog.scopes);
        this.scope.set(catalog.scopes[0] ?? 'workspace');
        this.members.set(await this.directory.getMembers(workspace.id));
        const spaces = await this.directory.getSpaces(workspace.id);
        this.spaces.set(spaces);
        this.spaceId.set(spaces[0]?.id ?? '');
        const list = await this.admin.assignments(workspace.id);
        this.assignments.set(list.assignments);
      }
      const usage = await this.admin.usage(workspace.id);
      this.usage.set(usage.resources);
      const alerts = await this.admin.alerts(workspace.id);
      this.alerts.set(alerts.alerts);
    } catch (error) {
      if (statusOf(error) === 404) {
        this.disabled.set(true);
        return;
      }
      this.denied.set(true);
    } finally {
      this.loading.set(false);
    }
  }

  protected async grant(event: Event): Promise<void> {
    event.preventDefault();
    await this.run(async () => {
      await this.admin.grant(this.workspaceId, {
        granteeId: this.granteeId(),
        scope: this.scope(),
        spaceId: this.scope() === 'space' ? this.spaceId() : undefined,
        reason: this.reason(),
        breakGlass: this.breakGlass(),
      });
      const list = await this.admin.assignments(this.workspaceId);
      this.assignments.set(list.assignments);
      this.notice.set(ui.delegatedAdminSaved);
    });
  }

  protected async revoke(id: string): Promise<void> {
    await this.run(async () => {
      await this.admin.revoke(this.workspaceId, id);
      this.assignments.set(this.assignments().filter((row) => row.id !== id));
    });
  }

  protected trendLabel(trend: string): string {
    if (trend === 'up') {
      return ui.delegatedAdminTrendUp;
    }
    if (trend === 'down') {
      return ui.delegatedAdminTrendDown;
    }
    return ui.delegatedAdminTrendFlat;
  }

  protected actionLabel(action: string): string {
    if (action === 'warning') {
      return ui.delegatedAdminActionWarning;
    }
    if (action === 'blocked') {
      return ui.delegatedAdminActionBlocked;
    }
    return ui.delegatedAdminActionNone;
  }

  protected text(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected checked(event: Event): boolean {
    return (event.target as HTMLInputElement).checked;
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.error.set('');
    try {
      await action();
    } catch (error) {
      this.error.set(translateErrorCode(codeOf(error)) || ui.delegatedAdminError);
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
