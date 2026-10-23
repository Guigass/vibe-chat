import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { ApiService } from '../../core/api/api.service';
import { ui } from '../../core/i18n/strings';
import { EmptyState, ThemeToggle } from '../../shared/ui';
import { AdminContextService } from './admin-context.service';
import { areaTitle, AdminAreaId } from './admin-permissions';

@Component({
  selector: 'vc-admin-shell',
  standalone: true,
  imports: [RouterLink, RouterLinkActive, RouterOutlet, ThemeToggle, EmptyState],
  templateUrl: './admin-shell.page.html',
  styleUrl: './admin-shell.page.scss',
})
export class AdminShellPage implements OnInit {
  readonly ctx = inject(AdminContextService);

  private readonly api = inject(ApiService);

  readonly ui = ui;
  readonly loading = signal(true);
  readonly activeTitle = signal(ui.admin);
  readonly confidentialMode = signal(false);

  readonly navItems = computed(() => this.ctx.navItems());
  readonly workspaceName = computed(() => this.ctx.workspace()?.name ?? '');

  async ngOnInit(): Promise<void> {
    await this.ctx.ensureReady();
    if (!this.ctx.canAccessAdmin()) {
      this.activeTitle.set(ui.adminNoAccessTitle);
    } else if (typeof this.api.getAdminStats === 'function') {
      try {
        const stats = await this.api.getAdminStats();
        this.confidentialMode.set(stats.contentAuditEnabled === false);
      } catch {
        this.confidentialMode.set(false);
      }
    }
    this.loading.set(false);
  }

  setToolbarTitle(area: string): void {
    this.activeTitle.set(areaTitle(area as AdminAreaId));
  }

  onChildActivate(component: { areaId?: AdminAreaId }): void {
    this.setToolbarTitle(component.areaId ?? 'overview');
  }
}
