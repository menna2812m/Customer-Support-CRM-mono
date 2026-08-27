import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { AuthService, provideCrmCore } from '@crm/core';
import { routes } from './app.routes';

/**
 * The application composes the platform once, here. Features add nothing to this file - that is
 * what keeps "add a feature" from touching shared infrastructure (spec SC-002).
 */
export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    provideRouter(routes, withComponentInputBinding()),
    provideCrmCore(),

    // Rebuild the session before the first route resolves, so a reload lands the user back where
    // they were instead of bouncing them through the provider. Ordered after provideCrmCore
    // because it needs the runtime configuration the initializer there loads.
    provideAppInitializer(async () => {
      await inject(AuthService).restore();
    }),
  ],
};
