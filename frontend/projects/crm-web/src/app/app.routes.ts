import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';

/**
 * Feature routes are lazy by default (spec FR-033): a feature is loaded when it is first visited,
 * so adding features does not grow the initial bundle.
 *
 * Every feature route names `authGuard`. The three authentication routes deliberately do not - a
 * guard that redirects to sign-in cannot itself guard the sign-in screen.
 */
export const routes: Routes = [
  {
    path: 'sign-in',
    loadComponent: () => import('./features/auth/sign-in.page').then((m) => m.SignInPage),
  },
  {
    path: 'auth/complete',
    loadComponent: () =>
      import('./features/auth/auth-complete.page').then((m) => m.AuthCompletePage),
  },
  {
    path: 'no-access',
    loadComponent: () => import('./features/auth/no-access.page').then((m) => m.NoAccessPage),
  },
  {
    path: 'home',
    canActivate: [authGuard],
    loadComponent: () => import('./features/home/home.page').then((m) => m.HomePage),
  },
  {
    path: 'diagnostics',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/diagnostics/diagnostics.page').then((m) => m.DiagnosticsPage),
  },
  {
    path: 'organization/departments',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/organization/departments.page').then((m) => m.DepartmentsPage),
  },
  {
    path: 'organization/branches',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/organization/branches.page').then((m) => m.BranchesPage),
  },
  {
    path: 'identity/people',
    canActivate: [authGuard],
    loadComponent: () => import('./features/identity/people.page').then((m) => m.PeoplePage),
  },
  {
    path: 'identity/people/:id',
    canActivate: [authGuard],
    loadComponent: () => import('./features/identity/person.page').then((m) => m.PersonPage),
  },
  { path: 'organization', pathMatch: 'full', redirectTo: 'organization/departments' },
  { path: '', pathMatch: 'full', redirectTo: 'home' },
  { path: '**', redirectTo: 'home' },
];
