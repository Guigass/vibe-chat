import { Component, inject, OnInit, signal } from '@angular/core';
import { environment } from '../../../environments/environment';
import { ApiService } from '../../core/api/api.service';
import { AdminStats } from '../../shared/models/chat.models';
import { Skeleton } from '../../shared/ui';
import { AdminContextService } from './admin-context.service';
import { AdminAreaId } from './admin-permissions';

@Component({
  selector: 'vc-admin-overview',
  standalone: true,
  imports: [Skeleton],
  templateUrl: './admin-overview.page.html',
  styleUrl: './admin-shared.scss',
})
export class AdminOverviewPage implements OnInit {
  readonly areaId: AdminAreaId = 'overview';
  readonly webBuildId = environment.buildId;

  private readonly api = inject(ApiService);
  readonly ctx = inject(AdminContextService);

  readonly loading = signal(true);
  readonly stats = signal<AdminStats | null>(null);
  readonly usingDemo = signal(false);

  async ngOnInit(): Promise<void> {
    try {
      const stats = await this.api.getAdminStats();
      this.stats.set(stats);
    } catch {
      this.usingDemo.set(true);
      this.stats.set({
        users: 128,
        onlineUsers: 17,
        workspaces: 6,
        channels: 42,
        messages: 18420,
        realtimeConnections: 23,
        outboxPending: 2,
        processingFailures: 0,
        health: { postgres: 'up', redis: 'up', storage: 'up' },
        appVersion: environment.appVersion,
        grafanaUrl: environment.grafanaUrl,
      });
    } finally {
      this.loading.set(false);
    }
  }
}
