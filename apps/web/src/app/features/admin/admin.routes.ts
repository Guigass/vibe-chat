import { Routes } from '@angular/router';
import { adminAreaGuard, adminLandingGuard, adminRouteGuard } from './admin.guard';

export { adminRouteGuard };

export const ADMIN_CHILD_ROUTES: Routes = [
  {
    path: '',
    pathMatch: 'full',
    canActivate: [adminLandingGuard],
    loadComponent: () =>
      import('./admin-landing.page').then((m) => m.AdminLandingPage),
  },
  {
    path: 'overview',
    canActivate: [adminAreaGuard('overview')],
    loadComponent: () =>
      import('./admin-overview.page').then((m) => m.AdminOverviewPage),
  },
  {
    path: 'members',
    canActivate: [adminAreaGuard('members')],
    loadComponent: () =>
      import('./admin-members.page').then((m) => m.AdminMembersPage),
  },
  {
    path: 'conversations',
    canActivate: [adminAreaGuard('conversations')],
    loadComponent: () =>
      import('./admin-conversations.page').then((m) => m.AdminConversationsPage),
  },
  {
    path: 'audit',
    canActivate: [adminAreaGuard('audit')],
    loadComponent: () => import('./admin-audit.page').then((m) => m.AdminAuditPage),
  },
  {
    path: 'settings',
    canActivate: [adminAreaGuard('settings')],
    loadComponent: () =>
      import('./admin-settings.page').then((m) => m.AdminSettingsPage),
  },
  {
    path: 'onboarding',
    canActivate: [adminAreaGuard('onboarding')],
    loadComponent: () =>
      import('./admin-onboarding.page').then((m) => m.AdminOnboardingPage),
  },
  {
    path: 'plugins',
    canActivate: [adminAreaGuard('plugins')],
    loadComponent: () =>
      import('./admin-plugins.page').then((m) => m.AdminPluginsPage),
  },
  {
    path: 'automations',
    canActivate: [adminAreaGuard('automations')],
    loadComponent: () =>
      import('./admin-automations.page').then((m) => m.AdminAutomationsPage),
  },
  {
    path: 'connectors',
    canActivate: [adminAreaGuard('connectors')],
    loadComponent: () =>
      import('./admin-connectors.page').then((m) => m.AdminConnectorsPage),
  },
  {
    path: 'import',
    canActivate: [adminAreaGuard('import')],
    loadComponent: () => import('./admin-import.page').then((m) => m.AdminImportPage),
  },
  {
    path: 'diagnostics',
    canActivate: [adminAreaGuard('diagnostics')],
    loadComponent: () =>
      import('./admin-diagnostics.page').then((m) => m.AdminDiagnosticsPage),
  },
  {
    path: 'scim',
    canActivate: [adminAreaGuard('scim')],
    loadComponent: () => import('./admin-scim.page').then((m) => m.AdminScimPage),
  },
  {
    path: 'quarantine',
    canActivate: [adminAreaGuard('quarantine')],
    loadComponent: () => import('./admin-quarantine.page').then((m) => m.AdminQuarantinePage),
  },
  {
    path: 'capacity',
    canActivate: [adminAreaGuard('capacity')],
    loadComponent: () => import('./admin-capacity.page').then((m) => m.AdminCapacityPage),
  },
  {
    path: 'developer',
    canActivate: [adminAreaGuard('developer')],
    loadComponent: () => import('./admin-developer.page').then((m) => m.AdminDeveloperPage),
  },
  {
    path: 'branding',
    canActivate: [adminAreaGuard('branding')],
    loadComponent: () => import('./admin-branding.page').then((m) => m.AdminBrandingPage),
  },
  {
    path: 'storage',
    canActivate: [adminAreaGuard('storage')],
    loadComponent: () => import('./admin-storage.page').then((m) => m.AdminStoragePage),
  },
  {
    path: '**',
    redirectTo: 'overview',
  },
];
