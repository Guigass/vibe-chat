import { DatePipe } from '@angular/common';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { ApiService } from '../../core/api/api.service';
import { AuditEventItem } from '../../shared/models/chat.models';
import { AdminContextService } from './admin-context.service';
import { AdminAreaId } from './admin-permissions';

@Component({
  selector: 'vc-admin-audit',
  standalone: true,
  imports: [DatePipe],
  templateUrl: './admin-audit.page.html',
  styleUrl: './admin-shared.scss',
})
export class AdminAuditPage implements OnInit {
  readonly areaId: AdminAreaId = 'audit';

  private readonly api = inject(ApiService);
  readonly ctx = inject(AdminContextService);

  readonly loading = signal(true);
  readonly loadError = signal(false);
  readonly auditEvents = signal<AuditEventItem[]>([]);

  readonly actionFilter = signal('all');
  readonly dateFrom = signal('');
  readonly dateTo = signal('');

  readonly actionOptions = computed(() => {
    const actions = new Set(this.auditEvents().map((ev) => ev.action));
    return ['all', ...Array.from(actions).sort()];
  });

  readonly filteredEvents = computed(() => {
    const action = this.actionFilter();
    const from = this.dateFrom() ? new Date(this.dateFrom()) : null;
    const to = this.dateTo() ? new Date(`${this.dateTo()}T23:59:59`) : null;

    return this.auditEvents().filter((ev) => {
      if (action !== 'all' && ev.action !== action) {
        return false;
      }
      const when = new Date(ev.occurredAt);
      if (from && when < from) {
        return false;
      }
      if (to && when > to) {
        return false;
      }
      return true;
    });
  });

  async ngOnInit(): Promise<void> {
    await this.ctx.ensureReady();
    try {
      const events = await this.api.getAdminAuditEvents(80);
      this.auditEvents.set(events);
      this.loadError.set(false);
    } catch {
      this.loadError.set(true);
      this.auditEvents.set([]);
    } finally {
      this.loading.set(false);
    }
  }
}
