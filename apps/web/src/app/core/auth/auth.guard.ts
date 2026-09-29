import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

export const authGuard: CanActivateFn = async (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (!auth.ready()) {
    await auth.init();
  }

  if (auth.isAuthenticated()) {
    return true;
  }

  if (typeof sessionStorage !== 'undefined' && state.url && state.url !== '/login') {
    sessionStorage.setItem('vc.returnUrl', state.url);
  }

  return router.createUrlTree(['/login']);
};
