import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { AuthService } from '@crm/core';
import { provideCrmTesting } from '@crm/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';
import { authGuard } from './auth.guard';

/**
 * Spec FR-033: an unauthenticated visitor is sent to sign-in, and the address they asked for
 * survives the round trip - the difference between landing on the ticket they were sent a link to
 * and landing on a home page with no idea what they were meant to see.
 */
describe('authGuard', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideCrmTesting(), provideRouter([])],
    });
  });

  function run(url: string) {
    return TestBed.runInInjectionContext(() => authGuard({} as never, { url } as never));
  }

  it('redirects an unauthenticated visitor to sign-in, preserving the destination', () => {
    const result = run('/tickets/42');
    const tree = TestBed.inject(Router).serializeUrl(result as never);

    expect(tree).toContain('/sign-in');
    expect(tree).toContain(`returnUrl=${encodeURIComponent('/tickets/42')}`);
  });

  it('permits a signed-in visitor', async () => {
    const auth = TestBed.inject(AuthService);
    const restored = auth.restore();

    TestBed.inject(HttpTestingController)
      .expectOne('/api/v1/auth/session')
      .flush({
        accessToken: 'issued-credential',
        expiresInSeconds: 900,
        user: {
          id: 'a1',
          displayName: 'Layla Hassan',
          email: 'layla@example.com',
          population: 'Staff',
          permissions: ['tickets.view'],
          scope: null,
        },
      });

    await restored;

    expect(run('/tickets/42')).toBe(true);
  });
});
