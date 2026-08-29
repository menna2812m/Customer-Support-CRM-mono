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

/**
 * Resolves once `assets/config.json` has been applied. Angular starts every application
 * initializer together and awaits them as a group, so registration order does not sequence them:
 * anything that reads {@link APP_CONFIG} during start-up must await this first, or it reads the
 * fallback and addresses the wrong origin.
 */
export const CONFIG_READY = new InjectionToken<() => Promise<void>>('CRM_CONFIG_READY');

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
  // One object for the lifetime of the injector, populated in place. The injector caches whatever
  // the factory returns the first time the token is resolved, so swapping a variable afterwards
  // would leave every early reader holding the fallback for good.
  const config: AppConfig = { ...FALLBACK_CONFIG };
  let ready: Promise<void> = Promise.resolve();

  return [
    provideAppInitializer(() => {
      ready = loadAppConfig().then((loaded) => {
        Object.assign(config, loaded);
      });

      return ready;
    }),
    { provide: APP_CONFIG, useFactory: () => config },
    { provide: CONFIG_READY, useFactory: () => () => ready },
  ];
}
