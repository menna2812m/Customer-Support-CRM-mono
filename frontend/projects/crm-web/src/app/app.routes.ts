import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';

/**
 * Feature routes are lazy by default (spec FR-033): a feature is loaded when it is first visited,
 * so adding features does not grow the initial bundle.
 *
 * Every feature route names `authGuard`, the extension point the authentication feature fills in.
 */
export const routes: Routes = [
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
  { path: '', pathMatch: 'full', redirectTo: 'home' },
  { path: '**', redirectTo: 'home' },
];
