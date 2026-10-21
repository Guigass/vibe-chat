import { Component, inject, OnInit, signal } from '@angular/core';
import { CapacityApiService, CapacityDashboard, CapacityRecovery, CapacitySli, CapacityTrigger } from '../../core/api/capacity-api.service';
import { fillTemplate, ui } from '../../core/i18n/strings';
import { AdminContextService } from './admin-context.service';
import { AdminAreaId } from './admin-permissions';

@Component({
  selector: 'vc-admin-capacity',
  standalone: true,
  templateUrl: './admin-capacity.page.html',
  styleUrl: './admin-shared.scss',
})
export class AdminCapacityPage implements OnInit {
  readonly areaId: AdminAreaId = 'capacity';
  readonly ui = ui;

  private readonly api = inject(CapacityApiService);
  readonly ctx = inject(AdminContextService);

  readonly loading = signal(true);
  readonly disabled = signal(false);
  readonly denied = signal(false);
  readonly loadError = signal(false);
  readonly dashboard = signal<CapacityDashboard | null>(null);

  async ngOnInit(): Promise<void> {
    await this.ctx.ensureReady();
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId) {
      this.loading.set(false);
      this.denied.set(true);
      return;
    }

    try {
      this.dashboard.set(await this.api.dashboard(workspaceId));
    } catch (error) {
      const status = (error as { status?: number }).status;
      const message = error instanceof Error ? error.message : '';
      if (status === 404 && message.includes('CapacitySloDisabled')) {
        this.disabled.set(true);
      } else if (status === 403) {
        this.denied.set(true);
      } else {
        this.loadError.set(true);
      }
    } finally {
      this.loading.set(false);
    }
  }

  sliLabel(id: string): string {
    switch (id) {
      case 'api-availability':
        return ui.capacitySliApiAvailability;
      case 'api-latency':
        return ui.capacitySliApiLatency;
      case 'message-echo':
        return ui.capacitySliMessageEcho;
      case 'outbox-lag':
        return ui.capacitySliOutboxLag;
      case 'search-lag':
        return ui.capacitySliSearchLag;
      case 'persistence':
        return ui.capacitySliPersistence;
      case 'realtime-delivery':
        return ui.capacitySliRealtime;
      case 'reconnect':
        return ui.capacitySliReconnect;
      case 'sync':
        return ui.capacitySliSync;
      case 'revocation':
        return ui.capacitySliRevocation;
      case 'attachments':
        return ui.capacitySliAttachments;
      case 'restore-verified':
        return ui.capacitySliRestore;
      default:
        return ui.capacityObjective;
    }
  }

  triggerLabel(trigger: CapacityTrigger): string {
    switch (trigger.id) {
      case 'adr-015-outbox':
        return ui.capacityTriggerBus;
      case 'adr-016-search-query':
        return ui.capacityTriggerSearch;
      case 'adr-016-search-cpu':
        return ui.capacityTriggerSearchCpu;
      case 'adr-017-replicas':
        return ui.capacityTriggerReplicas;
      case 'adr-017-connections':
        return ui.capacityTriggerConnections;
      default:
        return trigger.adr;
    }
  }

  burnLabel(sli: CapacitySli): string {
    if (sli.burnState === 'within') {
      return ui.capacityWithin;
    }
    if (sli.burnState === 'burning') {
      return ui.capacityBurning;
    }
    return ui.capacityUnmeasured;
  }

  minutes(value: number): string {
    return fillTemplate(ui.capacityMinutes, { n: value });
  }

  days(value: number): string {
    return fillTemplate(ui.capacityDays, { n: value });
  }

  hardwareLine(dashboard: CapacityDashboard): string {
    return fillTemplate(ui.capacityHardwareLine, {
      vcpu: dashboard.hardware.vcpu,
      memory: dashboard.hardware.memoryGb,
    });
  }

  recoveryNote(row: CapacityRecovery): string {
    if (!row.current && row.availableAfter) {
      return fillTemplate(ui.capacityLater, { item: row.availableAfter });
    }
    if (row.bestEffort) {
      return ui.capacityBestEffort;
    }
    return ui.capacityNotSla;
  }
}
