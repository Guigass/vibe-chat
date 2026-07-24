import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

export const authGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (!auth.ready()) {
    await auth.init();
  }

  if (auth.isAuthenticated()) {
    return true;
  }

  return router.createUrlTree(['/login']);
};
