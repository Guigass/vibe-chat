import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AdminContextService } from './admin-context.service';
import { AdminAreaId, canAccessArea, firstAllowedPath, hasAdminDashboard } from './admin-permissions';

/** Parent `/admin` shell: always allow; denied Members see feedback in the shell. */
export const adminGuard: CanActivateFn = async () => {
  const ctx = inject(AdminContextService);
  await ctx.ensureReady();
  return true;
};

/** Landing `''`: Owners/Admins/Auditors go to first area; Members stay on `/admin`. */
export const adminLandingGuard: CanActivateFn = async () => {
  const ctx = inject(AdminContextService);
  const router = inject(Router);
  await ctx.ensureReady();
  const role = ctx.role();

  if (!hasAdminDashboard(role)) {
    return true;
  }

  return router.createUrlTree(['/admin', firstAllowedPath(role)]);
};

export function adminAreaGuard(area: AdminAreaId): CanActivateFn {
  return async () => {
    const ctx = inject(AdminContextService);
    const router = inject(Router);
    await ctx.ensureReady();
    const role = ctx.role();

    if (!hasAdminDashboard(role)) {
      return router.createUrlTree(['/admin']);
    }

    if (!canAccessArea(role, area)) {
      return router.createUrlTree(['/admin', firstAllowedPath(role)]);
    }

    return true;
  };
}

export const adminRouteGuard = adminGuard;
