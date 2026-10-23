import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { environment } from '../../../environments/environment';

/** Lab catalog. Production builds without DevAuth redirect away. */
export const designSystemGuard: CanActivateFn = () => {
  if (environment.production && !environment.enableDevAuth) {
    return inject(Router).parseUrl('/login');
  }
  return true;
};
