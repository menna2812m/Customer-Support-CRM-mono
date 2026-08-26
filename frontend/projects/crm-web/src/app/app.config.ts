import {
  ApplicationConfig,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { provideCrmCore } from '@crm/core';
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
  ],
};
