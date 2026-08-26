import { InjectionToken, inject, provideAppInitializer } from '@angular/core';
import { HttpBackend, HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

/**
 * Runtime configuration, fetched before the application renders.
 *
 * It is deliberately NOT baked into the bundle: production serves the static build from IIS, so
 * one artifact has to be promotable across environments without a rebuild (spec FR-009).
 */
export interface AppConfig {
  /** Absolute base address of the CRM API, without a trailing slash. */
  apiBaseUrl: string;
  /** Language used when the visitor has no stored preference. */
  defaultLanguage: SupportedLanguage;
  /** Languages the application offers. Arabic and English are both first-class. */
  supportedLanguages: SupportedLanguage[];
}

export type SupportedLanguage = 'ar' | 'en';

export const APP_CONFIG = new InjectionToken<AppConfig>('CRM_APP_CONFIG');

const CONFIG_URL = 'assets/config.json';

const FALLBACK_CONFIG: AppConfig = {
  apiBaseUrl: '',
  defaultLanguage: 'en',
  supportedLanguages: ['en', 'ar'],
};

/**
 * Loads `assets/config.json` through {@link HttpBackend} rather than {@link HttpClient} so the
 * interceptor chain - which itself depends on the configuration - is not involved.
 */
export function loadAppConfig(): Promise<AppConfig> {
  const backend = inject(HttpBackend);
  const http = new HttpClient(backend);

  return firstValueFrom(http.get<AppConfig>(CONFIG_URL))
    .then((config) => normalize(config))
    .catch(() => {
      // A missing or malformed config file is a deployment fault. Fail visibly in the console
      // and continue with same-origin defaults so the shell can still render an error state.
      console.error(
        `[crm] Could not load ${CONFIG_URL}. Falling back to same-origin API access. ` +
          'Check the deployment: this file is environment-specific and must be present.',
      );
      return FALLBACK_CONFIG;
    });
}

function normalize(config: AppConfig): AppConfig {
  return {
    ...FALLBACK_CONFIG,
    ...config,
    apiBaseUrl: (config.apiBaseUrl ?? '').replace(/\/+$/, ''),
  };
}

export function provideAppConfig() {
  let loaded: AppConfig = FALLBACK_CONFIG;

  return [
    provideAppInitializer(async () => {
      loaded = await loadAppConfig();
    }),
    { provide: APP_CONFIG, useFactory: () => loaded },
  ];
}
