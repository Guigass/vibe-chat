import { Routes } from '@angular/router';
import { authGuard } from './core/auth/auth.guard';
import { adminRouteGuard } from './features/admin/admin.routes';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    redirectTo: 'login',
  },
  {
    path: 'login',
    loadComponent: () =>
      import('./features/auth/login/login.page').then((m) => m.LoginPage),
  },
  {
    path: 'auth/callback',
    loadComponent: () =>
      import('./features/auth/callback/callback.page').then((m) => m.CallbackPage),
  },
  {
    path: 'auth/silent-renew',
    loadComponent: () =>
      import('./features/auth/silent-renew/silent-renew.page').then((m) => m.SilentRenewPage),
  },
  {
    path: 'app',
    canActivate: [authGuard],
    loadComponent: () => import('./layout/shell.page').then((m) => m.ShellPage),
  },
  {
    path: 'admin',
    canActivate: [authGuard, adminRouteGuard],
    loadComponent: () =>
      import('./features/admin/admin-shell.page').then((m) => m.AdminShellPage),
    loadChildren: () =>
      import('./features/admin/admin.routes').then((m) => m.ADMIN_CHILD_ROUTES),
  },
  {
    path: '**',
    redirectTo: 'login',
  },
];
