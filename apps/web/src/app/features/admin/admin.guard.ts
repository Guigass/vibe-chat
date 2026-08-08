import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AdminContextService } from './admin-context.service';
import { AdminAreaId, canAccessArea, firstAllowedPath, hasAdminDashboard } from './admin-permissions';

export const adminGuard: CanActivateFn = async () => {
  const ctx = inject(AdminContextService);
  const router = inject(Router);
  await ctx.ensureReady();

  if (!hasAdminDashboard(ctx.role())) {
    return router.createUrlTree(['/app']);
  }

  return true;
};

export function adminAreaGuard(area: AdminAreaId): CanActivateFn {
  return async () => {
    const ctx = inject(AdminContextService);
    const router = inject(Router);
    await ctx.ensureReady();
    const role = ctx.role();

    if (!canAccessArea(role, area)) {
      return router.createUrlTree(['/admin', firstAllowedPath(role)]);
    }

    return true;
  };
}
