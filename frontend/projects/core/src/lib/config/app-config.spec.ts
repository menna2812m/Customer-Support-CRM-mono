import { HttpBackend, provideHttpClient, withInterceptorsFromDi } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationInitStatus, inject, provideAppInitializer } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { APP_CONFIG, CONFIG_READY, provideAppConfig } from './app-config';

/**
 * Spec FR-009: the API base address is fetched at runtime, not baked into the bundle. Anything
 * that reads it before the fetch resolves would otherwise silently address the wrong origin -
 * which is what sends sign-in to the application's own host instead of the API.
 */
describe('provideAppConfig', () => {
  const CONFIG = {
    apiBaseUrl: 'https://api.example.test',
    defaultLanguage: 'en' as const,
    supportedLanguages: ['en', 'ar'] as const,
  };

  it('gives a concurrently registered initializer the loaded address, not the fallback', async () => {
    let seen = 'not read';

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptorsFromDi()),
        provideHttpClientTesting(),
        provideAppConfig(),
        // Angular starts every initializer together, so this one runs while config.json is still
        // in flight - exactly the position the session restore occupies in the application.
        provideAppInitializer(() => {
          const configReady = inject(CONFIG_READY);
          const config = inject(APP_CONFIG);

          return configReady().then(() => {
            seen = config.apiBaseUrl;
          });
        }),
      ],
    });

    const backend = TestBed.inject(HttpBackend) as unknown as HttpTestingController;
    const ready = TestBed.inject(ApplicationInitStatus).donePromise;

    TestBed.inject(HttpTestingController).expectOne('assets/config.json').flush(CONFIG);
    await ready;
    void backend;

    expect(seen).toBe('https://api.example.test');
  });

  it('keeps one object identity, so an early reader observes the loaded values', async () => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptorsFromDi()),
        provideHttpClientTesting(),
        provideAppConfig(),
      ],
    });

    // Resolve the token before the fetch completes - the injector caches whatever comes back.
    const early = TestBed.inject(APP_CONFIG);
    const ready = TestBed.inject(ApplicationInitStatus).donePromise;

    TestBed.inject(HttpTestingController).expectOne('assets/config.json').flush(CONFIG);
    await ready;

    expect(early.apiBaseUrl).toBe('https://api.example.test');
  });
});
