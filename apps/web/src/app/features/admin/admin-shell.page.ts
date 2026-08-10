import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
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

  readonly loading = signal(true);
  readonly activeTitle = signal('Admin');

  readonly navItems = computed(() => this.ctx.navItems());
  readonly workspaceName = computed(() => this.ctx.workspace()?.name ?? '');

  async ngOnInit(): Promise<void> {
    await this.ctx.ensureReady();
    if (!this.ctx.canAccessAdmin()) {
      this.activeTitle.set('Sem acesso');
    }
    this.loading.set(false);
  }

  setToolbarTitle(area: string): void {
    const titles: Record<string, string> = {
      overview: areaTitle('overview'),
      members: areaTitle('members'),
      conversations: areaTitle('conversations'),
      audit: areaTitle('audit'),
      settings: areaTitle('settings'),
      plugins: areaTitle('plugins'),
    };
    this.activeTitle.set(titles[area] ?? 'Admin');
  }

  onChildActivate(component: { areaId?: AdminAreaId }): void {
    this.setToolbarTitle(component.areaId ?? 'overview');
  }
}
