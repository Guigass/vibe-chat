import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { environment } from '../../../environments/environment';
import { ApiService } from '../../core/api/api.service';
import { AdminStats } from '../../shared/models/chat.models';
import { Skeleton, ThemeToggle } from '../../shared/ui';

@Component({
  selector: 'vc-admin-page',
  standalone: true,
  imports: [RouterLink, Skeleton, ThemeToggle],
  templateUrl: './admin.page.html',
  styleUrl: './admin.page.scss',
})
export class AdminPage implements OnInit {
  private readonly api = inject(ApiService);

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
        health: {
          postgres: 'up',
          redis: 'up',
          storage: 'up',
        },
        appVersion: environment.appVersion,
        grafanaUrl: environment.grafanaUrl,
      });
    } finally {
      this.loading.set(false);
    }
  }
}
