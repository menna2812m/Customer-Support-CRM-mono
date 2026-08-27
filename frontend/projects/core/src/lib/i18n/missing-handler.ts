import { Injectable } from '@angular/core';
import { TranslocoMissingHandler, TranslocoConfig } from '@jsverse/transloco';

/**
 * A missing key renders a documented fallback and is reported to developers - never an empty or
 * broken label in front of a user (spec FR-039).
 *
 * The key itself is the fallback: it is unmistakable in review and screenshots, and it tells the
 * developer exactly what to add. The parity check in the verification script is what stops these
 * from reaching a release.
 */
@Injectable({ providedIn: 'root' })
export class CrmMissingHandler implements TranslocoMissingHandler {
  handle(key: string, config: TranslocoConfig): string {
    if (!config.prodMode) {
      console.warn(`[crm][i18n] Missing translation for "${key}" in the active language.`);
    }

    return key;
  }
}
