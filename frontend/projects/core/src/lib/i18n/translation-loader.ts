import { HttpBackend, HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Translation, TranslocoLoader } from '@jsverse/transloco';

/**
 * Loads translation resources at runtime (spec FR-035, FR-036).
 *
 * Resources are fetched rather than compiled in, because the language switch has to happen in
 * place - a compile-time locale bundle would require a separate build and a page reload per
 * language.
 *
 * HttpBackend is used deliberately: translations are static assets, not API calls, so they must
 * not pass through the base-URL, correlation, or auth interceptors.
 */
@Injectable({ providedIn: 'root' })
export class TranslationLoader implements TranslocoLoader {
  private readonly http = new HttpClient(inject(HttpBackend));

  getTranslation(lang: string) {
    return this.http.get<Translation>(`assets/i18n/${lang}.json`);
  }
}
