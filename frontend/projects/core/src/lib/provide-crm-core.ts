import {
  EnvironmentProviders,
  ErrorHandler,
  Provider,
  makeEnvironmentProviders,
} from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideAppConfig } from './config/app-config';
import { provideCrmI18n } from './i18n/provide-crm-i18n';
import { GlobalErrorHandler } from './errors/global-error-handler';
import {
  authTokenInterceptor,
  baseUrlInterceptor,
  correlationInterceptor,
  errorNormalizationInterceptor,
} from './http/interceptors';

/**
 * Single entry point for the cross-cutting platform: runtime configuration, the HTTP seam, and
 * global error handling. An application adds this once in its `app.config.ts`; features add
 * nothing.
 *
 * Interceptor order is deliberate - the base URL is resolved before correlation and credentials
 * are attached, and normalization wraps everything so no feature ever sees an HttpErrorResponse.
 */
export function provideCrmCore(): EnvironmentProviders {
  const providers: (Provider | EnvironmentProviders)[] = [
    provideAppConfig(),
    provideHttpClient(
      withInterceptors([
        baseUrlInterceptor,
        correlationInterceptor,
        authTokenInterceptor,
        errorNormalizationInterceptor,
      ]),
    ),
    { provide: ErrorHandler, useClass: GlobalErrorHandler },
    provideCrmI18n(),
  ];

  return makeEnvironmentProviders(providers);
}
