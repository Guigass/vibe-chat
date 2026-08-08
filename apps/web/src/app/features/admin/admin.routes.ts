import { Routes } from '@angular/router';
import { adminAreaGuard, adminGuard } from './admin.guard';

export const ADMIN_CHILD_ROUTES: Routes = [
  {
    path: '',
    pathMatch: 'full',
    redirectTo: 'overview',
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
    path: 'plugins',
    canActivate: [adminAreaGuard('plugins')],
    loadComponent: () =>
      import('./admin-plugins.page').then((m) => m.AdminPluginsPage),
  },
  {
    path: '**',
    redirectTo: 'overview',
  },
];

export const adminRouteGuard = adminGuard;
