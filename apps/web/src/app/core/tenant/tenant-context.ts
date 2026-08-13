import { Injectable, computed, signal } from '@angular/core';

export interface TenantState {
  tenantId: string | null;
  userId: string | null;
  roles: string[];
  displayName: string | null;
}

@Injectable({ providedIn: 'root' })
export class TenantContext {
  private readonly state = signal<TenantState>({
    tenantId: null,
    userId: null,
    roles: [],
    displayName: null,
  });

  readonly snapshot = this.state.asReadonly();
  readonly tenantId = computed(() => this.state().tenantId);
  readonly userId = computed(() => this.state().userId);

  setContext(partial: Partial<TenantState>): void {
    this.state.update((current) => ({ ...current, ...partial }));
  }

  clear(): void {
    this.state.set({
      tenantId: null,
      userId: null,
      roles: [],
      displayName: null,
    });
  }
}
