import { Injectable, computed, inject, signal } from '@angular/core';
import { User, UserManager, WebStorageStateStore } from 'oidc-client-ts';
import { environment } from '../../../environments/environment';
import { DraftStoreService } from '../services/draft-store.service';
import { TenantContext } from '../tenant/tenant-context';

export interface AuthProfile {
  id: string;
  name: string;
  email?: string;
  roles: string[];
  tenantId?: string;
}

export type DevUserName = 'demo' | 'alice' | 'bob';

const DEV_KEY = 'vc.dev-auth';
const DEMO_KEY = 'vc.demo-auth';

const DEV_PROFILES: Record<DevUserName, AuthProfile> = {
  demo: {
    id: '33333333-3333-3333-3333-333333333333',
    name: 'Demo',
    email: 'demo@vibechat.local',
    roles: ['WorkspaceOwner'],
    tenantId: '11111111-1111-1111-1111-111111111111',
  },
  alice: {
    id: '44444444-4444-4444-4444-444444444444',
    name: 'Alice',
    email: 'alice@vibechat.local',
    roles: ['Member'],
    tenantId: '11111111-1111-1111-1111-111111111111',
  },
  bob: {
    id: '55555555-5555-5555-5555-555555555555',
    name: 'Bob',
    email: 'bob@vibechat.local',
    roles: ['Member'],
    tenantId: '11111111-1111-1111-1111-111111111111',
  },
};

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly tenant = inject(TenantContext);
  private readonly drafts = inject(DraftStoreService);
  private readonly userManager = new UserManager({
    authority: environment.keycloak.authority,
    client_id: environment.keycloak.clientId,
    redirect_uri: environment.keycloak.redirectUri,
    post_logout_redirect_uri: environment.keycloak.postLogoutRedirectUri,
    silent_redirect_uri: environment.keycloak.silentRedirectUri,
    response_type: 'code',
    scope: environment.keycloak.scope,
    automaticSilentRenew: true,
    userStore: new WebStorageStateStore({ store: window.localStorage }),
  });

  private readonly userSignal = signal<User | null>(null);
  private readonly devProfile = signal<AuthProfile | null>(null);
  private readonly devUserSignal = signal<DevUserName | null>(null);
  private readonly offlineDemo = signal(false);
  private readonly readySignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);

  readonly user = this.userSignal.asReadonly();
  readonly ready = this.readySignal.asReadonly();
  readonly error = this.errorSignal.asReadonly();
  readonly devUser = this.devUserSignal.asReadonly();
  readonly isOfflineDemo = this.offlineDemo.asReadonly();
  readonly isAuthenticated = computed(
    () => !!this.userSignal()?.access_token || !!this.devProfile() || this.offlineDemo(),
  );
  readonly accessToken = computed(() => this.userSignal()?.access_token ?? null);
  readonly profile = computed<AuthProfile | null>(() => {
    if (this.devProfile()) return this.devProfile();
    if (this.offlineDemo()) {
      return {
        id: 'offline-demo',
        name: 'Alice Mendes',
        email: 'alice@vibechat.local',
        roles: ['user', 'admin'],
        tenantId: 'tenant-demo',
      };
    }
    const u = this.userSignal();
    if (!u?.profile) return null;
    const p = u.profile as Record<string, unknown>;
    return {
      id: String(p['sub'] ?? ''),
      name: String(p['name'] ?? p['preferred_username'] ?? 'Usuário'),
      email: p['email'] ? String(p['email']) : undefined,
      roles: this.extractRoles(p),
      tenantId: p['tenant_id'] ? String(p['tenant_id']) : undefined,
    };
  });

  constructor() {
    this.userManager.events.addUserLoaded((user) => this.applyUser(user));
    this.userManager.events.addUserUnloaded(() => this.applyUser(null));
    this.userManager.events.addSilentRenewError((err) => {
      this.errorSignal.set(err.message);
    });
    void this.init();
  }

  async init(): Promise<void> {
    try {
      if (!environment.enableDevAuth) {
        this.clearDevAuthStorage();
      } else {
        const devRaw = localStorage.getItem(DEV_KEY);
        if (devRaw && (devRaw === 'demo' || devRaw === 'alice' || devRaw === 'bob')) {
          this.enterDevUser(devRaw);
          return;
        }
        const demoRaw = localStorage.getItem(DEMO_KEY);
        if (demoRaw) {
          this.offlineDemo.set(true);
          this.applyOfflineDemo();
          return;
        }
      }
      const user = await this.userManager.getUser();
      this.applyUser(user && !user.expired ? user : null);
    } catch (err) {
      this.errorSignal.set(err instanceof Error ? err.message : 'Falha ao iniciar autenticação');
      this.applyUser(null);
    } finally {
      this.readySignal.set(true);
    }
  }

  async login(): Promise<void> {
    this.errorSignal.set(null);
    localStorage.removeItem(DEV_KEY);
    localStorage.removeItem(DEMO_KEY);
    this.devProfile.set(null);
    this.devUserSignal.set(null);
    this.offlineDemo.set(false);
    await this.userManager.signinRedirect();
  }

  enterDevUser(name: DevUserName): void {
    if (!environment.enableDevAuth) {
      this.clearDevAuthStorage();
      return;
    }
    localStorage.removeItem(DEMO_KEY);
    localStorage.setItem(DEV_KEY, name);
    const profile = DEV_PROFILES[name];
    this.offlineDemo.set(false);
    this.devUserSignal.set(name);
    this.devProfile.set(profile);
    this.userSignal.set(null);
    this.tenant.setContext({
      tenantId: profile.tenantId ?? null,
      userId: profile.id,
      roles: profile.roles,
      displayName: profile.name,
    });
    this.readySignal.set(true);
  }

  /** Fallback visual sem API — não envia X-Dev-User. */
  enterOfflineDemo(): void {
    if (!environment.enableDevAuth) {
      this.clearDevAuthStorage();
      return;
    }
    localStorage.removeItem(DEV_KEY);
    localStorage.setItem(DEMO_KEY, '1');
    this.devUserSignal.set(null);
    this.devProfile.set(null);
    this.offlineDemo.set(true);
    this.applyOfflineDemo();
  }

  /** @deprecated use enterDevUser */
  enterDemoMode(): void {
    this.enterDevUser('alice');
  }

  async completeLogin(): Promise<User> {
    localStorage.removeItem(DEV_KEY);
    localStorage.removeItem(DEMO_KEY);
    this.devProfile.set(null);
    this.devUserSignal.set(null);
    this.offlineDemo.set(false);
    const user = await this.userManager.signinRedirectCallback();
    this.applyUser(user);
    return user;
  }

  async completeSilentRenew(): Promise<void> {
    await this.userManager.signinSilentCallback();
  }

  async logout(): Promise<void> {
    const tenantId = this.tenant.tenantId();
    const userId = this.tenant.userId();
    if (tenantId && userId) {
      await this.drafts.clearUser(tenantId, userId);
    }

    if (this.devProfile() || this.offlineDemo()) {
      this.clearDevAuthStorage();
      this.tenant.clear();
      window.location.href = '/login';
      return;
    }
    await this.userManager.signoutRedirect();
  }

  async getAccessToken(): Promise<string | null> {
    if (this.devProfile() || this.offlineDemo()) return null;
    const user = await this.userManager.getUser();
    if (!user || user.expired) {
      try {
        const renewed = await this.userManager.signinSilent();
        this.applyUser(renewed);
        return renewed?.access_token ?? null;
      } catch {
        this.applyUser(null);
        return null;
      }
    }
    this.applyUser(user);
    return user.access_token;
  }

  private clearDevAuthStorage(): void {
    localStorage.removeItem(DEV_KEY);
    localStorage.removeItem(DEMO_KEY);
    this.devProfile.set(null);
    this.devUserSignal.set(null);
    this.offlineDemo.set(false);
  }

  private applyOfflineDemo(): void {
    this.tenant.setContext({
      tenantId: 'tenant-demo',
      userId: 'offline-demo',
      roles: ['user', 'admin'],
      displayName: 'Alice Mendes',
    });
    this.readySignal.set(true);
  }

  private applyUser(user: User | null): void {
    this.userSignal.set(user);
    const profile = user?.profile as Record<string, unknown> | undefined;
    const tenantId = profile?.['tenant_id'] ? String(profile['tenant_id']) : null;
    const userId = profile?.['sub'] ? String(profile['sub']) : null;
    this.tenant.setContext({
      tenantId,
      userId,
      roles: profile ? this.extractRoles(profile) : [],
      displayName: profile
        ? String(profile['name'] ?? profile['preferred_username'] ?? 'Usuário')
        : null,
    });
  }

  private extractRoles(profile: Record<string, unknown>): string[] {
    const realmAccess = profile['realm_access'] as { roles?: string[] } | undefined;
    if (realmAccess?.roles?.length) return realmAccess.roles;
    const roles = profile['roles'];
    if (Array.isArray(roles)) return roles.map(String);
    return [];
  }
}
