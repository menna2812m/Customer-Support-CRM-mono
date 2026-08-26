import {
  EnvironmentProviders,
  isDevMode,
  makeEnvironmentProviders,
  inject,
  provideAppInitializer,
} from '@angular/core';
import { provideTransloco, provideTranslocoMissingHandler } from '@jsverse/transloco';
import { CrmMissingHandler } from './missing-handler';
import { LanguageService } from './language.service';
import { TranslationLoader } from './translation-loader';

/**
 * Arabic and English are first-class from the start (Constitution VII). Both are always
 * available; neither is a later addition bolted onto an English-only UI.
 */
export function provideCrmI18n(): EnvironmentProviders {
  return makeEnvironmentProviders([
    provideTransloco({
      config: {
        availableLangs: ['en', 'ar'],
        defaultLang: 'en',
        fallbackLang: 'en',
        reRenderOnLangChange: true,
        prodMode: !isDevMode(),
        missingHandler: { allowEmpty: false, logMissingKey: true, useFallbackTranslation: true },
      },
      loader: TranslationLoader,
    }),
    provideTranslocoMissingHandler(CrmMissingHandler),
    provideAppInitializer(() => {
      // Runs after the runtime config has loaded, so the configured default language applies.
      inject(LanguageService).initialize();
    }),
  ]);
}
