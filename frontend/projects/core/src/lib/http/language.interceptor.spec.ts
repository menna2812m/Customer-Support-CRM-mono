import { HttpClient } from '@angular/common/http';
import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideCrmTesting } from '../../testing/test-providers';
import { LanguageService } from '../i18n/language.service';

/**
 * Spec LR-002: a list is ordered by the name the reader actually sees.
 *
 * The server does that ordering in the database, and reads the language from `Accept-Language`
 * because ordering a single page after the fact would sort within each page and not across them.
 * That only works if the client says which language it is reading in - so the header is part of
 * the contract, not a nicety. Without it the server correctly defaults to English and an Arabic
 * reader gets a list sorted by names they are not looking at.
 */
describe('languageInterceptor', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideCrmTesting()] });
  });

  function api(): HttpTestingController {
    return TestBed.inject(HttpTestingController);
  }

  function sendAndReadAcceptLanguage(): string | null {
    TestBed.inject(HttpClient).get('/api/v1/organization/departments').subscribe();

    const request = api().expectOne('/api/v1/organization/departments');
    request.flush({});

    return request.request.headers.get('Accept-Language');
  }

  it('asks for English while the reader is in English', () => {
    TestBed.inject(LanguageService).setLanguage('en');

    expect(sendAndReadAcceptLanguage()).toBe('en');
  });

  it('asks for Arabic once the reader switches to Arabic', () => {
    TestBed.inject(LanguageService).setLanguage('ar');

    expect(sendAndReadAcceptLanguage()).toBe('ar');
  });

  it('leaves a header the caller set explicitly alone', () => {
    TestBed.inject(LanguageService).setLanguage('ar');

    TestBed.inject(HttpClient)
      .get('/api/v1/organization/departments', { headers: { 'Accept-Language': 'en' } })
      .subscribe();

    const request = api().expectOne('/api/v1/organization/departments');
    request.flush({});

    expect(request.request.headers.get('Accept-Language')).toBe('en');
  });
});
