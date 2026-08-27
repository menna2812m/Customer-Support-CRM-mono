import { DOCUMENT } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Directionality } from '@angular/cdk/bidi';
import { TranslocoService } from '@jsverse/transloco';
import { provideCrmTesting } from '../../testing/test-providers';
import { LanguageService } from './language.service';

/**
 * Spec FR-036, FR-037, SC-004: switching language changes text and direction together, in place,
 * and the choice survives a reload.
 */
describe('LanguageService', () => {
  let service: LanguageService;
  let document: Document;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [provideCrmTesting()] });

    service = TestBed.inject(LanguageService);
    document = TestBed.inject(DOCUMENT);
  });

  it('starts in the configured default language', () => {
    service.initialize();

    expect(service.language()).toBe('en');
    expect(service.direction()).toBe('ltr');
  });

  it('switches text and direction together', () => {
    service.initialize();
    service.setLanguage('ar');

    expect(service.language()).toBe('ar');
    expect(service.direction()).toBe('rtl');
    expect(TestBed.inject(TranslocoService).getActiveLang()).toBe('ar');

    // Direction is applied globally rather than per screen.
    expect(document.documentElement.getAttribute('dir')).toBe('rtl');
    expect(document.documentElement.getAttribute('lang')).toBe('ar');
  });

  it('mirrors Angular Material components by updating the CDK directionality', () => {
    service.initialize();
    service.setLanguage('ar');

    // Without this, Material components keep the direction they were constructed with and the
    // layout only half-mirrors.
    expect(TestBed.inject(Directionality).value).toBe('rtl');
  });

  it('remembers the choice across sessions', () => {
    service.initialize();
    service.setLanguage('ar');

    // A fresh injector, as though the application had been reloaded.
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideCrmTesting()] });

    const reloaded = TestBed.inject(LanguageService);
    reloaded.initialize();

    expect(reloaded.language()).toBe('ar');
    expect(reloaded.direction()).toBe('rtl');
  });

  it('toggles between the two supported languages', () => {
    service.initialize();

    service.toggle();
    expect(service.language()).toBe('ar');

    service.toggle();
    expect(service.language()).toBe('en');
    expect(service.direction()).toBe('ltr');
  });

  it('falls back to the configured default when stored data is unusable', () => {
    localStorage.setItem('crm.language', 'klingon');

    service.initialize();

    expect(service.language()).toBe('en');
  });
});
