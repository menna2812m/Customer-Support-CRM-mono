import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '@crm/core';

/**
 * Route protection (spec FR-033).
 *
 * Sends an unauthenticated visitor to sign-in carrying the address they asked for, so that after
 * the provider round trip they land where they meant to go rather than on a generic home page.
 *
 * Frontend guards shape the experience only. Authorization is always enforced by the backend
 * (Constitution IV); a guard that returns true never grants access to data, and one that returns
 * false hides nothing the API would otherwise have handed over.
 */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.isAuthenticated()) {
    return true;
  }

  // A UrlTree rather than a navigate() call: the router replaces this navigation instead of
  // adding one, so Back does not bounce the user off the page they just reached.
  return router.createUrlTree(['/sign-in'], { queryParams: { returnUrl: state.url } });
};
