import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { AuthService, CONFIG_READY, provideCrmCore } from '@crm/core';
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
    // they were instead of bouncing them through the provider. It needs the runtime configuration
    // that provideCrmCore loads, and awaits it explicitly: Angular starts every initializer
    // together, so appearing later in this array does not make this one run second.
    // Both are injected synchronously: an inject() after an await has left the injection context.
    provideAppInitializer(() => {
      const configReady = inject(CONFIG_READY);
      const auth = inject(AuthService);

      return configReady().then(() => auth.restore());
    }),
  ],
};
