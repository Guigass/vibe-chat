import { Injectable, computed, inject, signal } from '@angular/core';
import { User, UserManager, WebStorageStateStore } from 'oidc-client-ts';
import { environment } from '../../../environments/environment';
import { TenantContext } from '../tenant/tenant-context';

export interface AuthProfile {
  id: string;
  name: string;
  email?: string;
  roles: string[];
  tenantId?: string;
}

const DEMO_KEY = 'vc.demo-auth';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly tenant = inject(TenantContext);
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
  private readonly demoProfile = signal<AuthProfile | null>(null);
  private readonly readySignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);

  readonly user = this.userSignal.asReadonly();
  readonly ready = this.readySignal.asReadonly();
  readonly error = this.errorSignal.asReadonly();
  readonly isAuthenticated = computed(
    () => !!this.userSignal()?.access_token || !!this.demoProfile(),
  );
  readonly accessToken = computed(() => this.userSignal()?.access_token ?? null);
  readonly profile = computed<AuthProfile | null>(() => {
    if (this.demoProfile()) return this.demoProfile();
    const u = this.userSignal();
    if (!u?.profile) return null;
    const p = u.profile as Record<string, unknown>;
    const roles = this.extractRoles(p);
    return {
      id: String(p['sub'] ?? ''),
      name: String(p['name'] ?? p['preferred_username'] ?? 'Usuário'),
      email: p['email'] ? String(p['email']) : undefined,
      roles,
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
      const demoRaw = localStorage.getItem(DEMO_KEY);
      if (demoRaw) {
        const demo = JSON.parse(demoRaw) as AuthProfile;
        this.applyDemo(demo);
        return;
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
    await this.userManager.signinRedirect();
  }

  enterDemoMode(): void {
    const demo: AuthProfile = {
      id: 'demo-user',
      name: 'Alice Mendes',
      email: 'alice@vibechat.local',
      roles: ['user', 'admin'],
      tenantId: 'tenant-demo',
    };
    localStorage.setItem(DEMO_KEY, JSON.stringify(demo));
    this.applyDemo(demo);
  }

  async completeLogin(): Promise<User> {
    localStorage.removeItem(DEMO_KEY);
    this.demoProfile.set(null);
    const user = await this.userManager.signinRedirectCallback();
    this.applyUser(user);
    return user;
  }

  async completeSilentRenew(): Promise<void> {
    await this.userManager.signinSilentCallback();
  }

  async logout(): Promise<void> {
    if (this.demoProfile()) {
      localStorage.removeItem(DEMO_KEY);
      this.demoProfile.set(null);
      this.tenant.clear();
      window.location.href = '/login';
      return;
    }
    await this.userManager.signoutRedirect();
  }

  async getAccessToken(): Promise<string | null> {
    if (this.demoProfile()) return 'demo-token';
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

  private applyDemo(demo: AuthProfile): void {
    this.demoProfile.set(demo);
    this.userSignal.set(null);
    this.tenant.setContext({
      tenantId: demo.tenantId ?? null,
      userId: demo.id,
      roles: demo.roles,
      displayName: demo.name,
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
