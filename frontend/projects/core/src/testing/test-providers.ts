import { EnvironmentProviders, Provider, importProvidersFrom } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { APP_CONFIG, AppConfig } from '../lib/config/app-config';
import { errorNormalizationInterceptor } from '../lib/http/interceptors';
import en from '../../../crm-web/public/assets/i18n/en.json';
import ar from '../../../crm-web/public/assets/i18n/ar.json';

/**
 * Shared test setup so every feature's tests exercise the same platform the application uses -
 * in particular the error normalization interceptor, which is what turns HTTP failures into
 * AppError. A test that skips it would assert against a shape the application never sees.
 *
 * The real translation files are loaded rather than a hand-written stub: a test that passes
 * against invented keys proves nothing about the screens users actually see.
 */
export const TEST_APP_CONFIG: AppConfig = {
  apiBaseUrl: '',
  defaultLanguage: 'en',
  supportedLanguages: ['en', 'ar'],
};

export function provideCrmTesting(
  config: Partial<AppConfig> = {},
): (Provider | EnvironmentProviders)[] {
  return [
    { provide: APP_CONFIG, useValue: { ...TEST_APP_CONFIG, ...config } },
    provideHttpClient(withInterceptors([errorNormalizationInterceptor])),
    provideHttpClientTesting(),
    importProvidersFrom(
      TranslocoTestingModule.forRoot({
        langs: { en, ar },
        translocoConfig: {
          availableLangs: ['en', 'ar'],
          defaultLang: config.defaultLanguage ?? 'en',
          reRenderOnLangChange: true,
        },
        preloadLangs: true,
      }),
    ),
  ];
}
