import { DOCUMENT, Injectable, computed, inject, signal } from '@angular/core';
import { Directionality } from '@angular/cdk/bidi';
import { TranslocoService } from '@jsverse/transloco';
import { APP_CONFIG, SupportedLanguage } from '../config/app-config';

export type LayoutDirection = 'rtl' | 'ltr';

const STORAGE_KEY = 'crm.language';

/**
 * The only way language changes (spec FR-036, FR-037, contracts/frontend-contracts.md).
 *
 * Text and direction are updated together, atomically, so they can never disagree: the active
 * translation, the document `dir` and `lang`, and the CDK directionality that Angular Material
 * components mirror from all move in one call. The choice is persisted, so a user who works in
 * Arabic does not re-select it every morning.
 */
@Injectable({ providedIn: 'root' })
export class LanguageService {
  private readonly transloco = inject(TranslocoService);
  private readonly directionality = inject(Directionality);
  private readonly document = inject(DOCUMENT);
  private readonly config = inject(APP_CONFIG);

  private readonly current = signal<SupportedLanguage>('en');

  /** Active language. */
  readonly language = this.current.asReadonly();

  /** Derived from the language - never set independently, so the two cannot drift apart. */
  readonly direction = computed<LayoutDirection>(() => (this.current() === 'ar' ? 'rtl' : 'ltr'));

  readonly supported = computed(() => this.config.supportedLanguages);

  /** Applies the stored choice, or the configured default, during startup. */
  initialize(): void {
    this.setLanguage(this.readStoredLanguage() ?? this.config.defaultLanguage);
  }

  setLanguage(language: SupportedLanguage): void {
    this.current.set(language);
    this.transloco.setActiveLang(language);

    const direction = this.direction();
    const root = this.document.documentElement;

    root.setAttribute('lang', language);
    root.setAttribute('dir', direction);

    // Material components read direction from here; updating the signal mirrors them immediately
    // without a reload.
    this.directionality.valueSignal.set(direction);
    this.directionality.change.emit(direction);

    this.persist(language);
  }

  toggle(): void {
    this.setLanguage(this.current() === 'ar' ? 'en' : 'ar');
  }

  private readStoredLanguage(): SupportedLanguage | null {
    try {
      const stored = this.document.defaultView?.localStorage.getItem(STORAGE_KEY);
      return stored === 'ar' || stored === 'en' ? stored : null;
    } catch {
      // Private browsing or blocked storage: fall back to the configured default rather than
      // failing to start.
      return null;
    }
  }

  private persist(language: SupportedLanguage): void {
    try {
      this.document.defaultView?.localStorage.setItem(STORAGE_KEY, language);
    } catch {
      // Not being able to remember the choice is not a reason to refuse to change it.
    }
  }
}
