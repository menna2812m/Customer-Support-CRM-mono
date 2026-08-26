import { CanActivateFn } from '@angular/router';

/**
 * Route protection extension point (spec FR-033).
 *
 * Deliberately inert in this feature: it permits every navigation. The authentication feature
 * replaces the body of this function - checking the session and redirecting to sign-in - and no
 * route definition changes, because every protected route already references it.
 *
 * Frontend guards shape the experience only. Authorization is always enforced by the backend
 * (Constitution IV); a guard that returns true never grants access to data.
 */
export const authGuard: CanActivateFn = () => true;
