import { TestBed } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';
import { DOCUMENT } from '@angular/core';
import { Router } from '@angular/router';
import { provideCrmTesting } from '../../testing/test-providers';
import { AuthService, safeReturnUrl } from './auth.service';
import { AuthSession } from './auth-session.store';
import { SessionRenewal } from './session-renewal.service';
import { SessionResponse } from './auth.models';

const SESSION: SessionResponse = {
  accessToken: 'issued-credential',
  expiresInSeconds: 900,
  user: {
    id: 'a1',
    displayName: 'Layla Hassan',
    email: 'layla@example.com',
    population: 'Staff',
    permissions: ['tickets.view', 'tickets.create'],
    scope: null,
  },
};

describe('AuthService', () => {
  let assigned: string[];

  beforeEach(() => {
    assigned = [];

    TestBed.configureTestingModule({
      providers: [
        provideCrmTesting(),
        {
          // A real navigation would end the test run, so the departure is observed rather than
          // performed. Everything up to that point is the real service.
          provide: DOCUMENT,
          useValue: { location: { assign: (url: string) => assigned.push(url) } },
        },
      ],
    });
  });

  function api(): HttpTestingController {
    return TestBed.inject(HttpTestingController);
  }

  it('restores a session from the renewal cookie and exposes the user and permissions', async () => {
    const auth = TestBed.inject(AuthService);
    const restored = auth.restore();

    const request = api().expectOne('/api/v1/auth/session');
    expect(request.request.method).toBe('POST');

    // Authenticated by a cookie script cannot read, and proven to be ours by a header a
    // cross-site form cannot set.
    expect(request.request.withCredentials).toBe(true);
    expect(request.request.headers.get('X-Requested-With')).toBe('CrmWeb');

    request.flush(SESSION);

    expect(await restored).toBe(true);
    expect(auth.isAuthenticated()).toBe(true);
    expect(auth.user()?.displayName).toBe('Layla Hassan');
    expect(auth.hasPermission('tickets.create')).toBe(true);
    expect(auth.hasPermission('users.manage')).toBe(false);
  });

  it('reports no session rather than failing when the cookie is gone', async () => {
    const auth = TestBed.inject(AuthService);
    const restored = auth.restore();

    api()
      .expectOne('/api/v1/auth/session')
      .flush({ code: 'session_expired' }, { status: 401, statusText: 'Unauthorized' });

    // Not being signed in is an ordinary state on a first visit, not an error to surface.
    expect(await restored).toBe(false);
    expect(auth.isAuthenticated()).toBe(false);
  });

  it('never puts the credential anywhere a script or another tab could read it', async () => {
    const auth = TestBed.inject(AuthService);
    const restored = auth.restore();

    api().expectOne('/api/v1/auth/session').flush(SESSION);
    await restored;

    expect(TestBed.inject(AuthSession).accessToken()).toBe('issued-credential');
    expect(JSON.stringify(localStorage)).not.toContain('issued-credential');
    expect(JSON.stringify(sessionStorage)).not.toContain('issued-credential');
    expect(document.cookie).not.toContain('issued-credential');
  });

  it('leaves for the provider carrying the destination and the active language', () => {
    TestBed.inject(AuthService).signIn('/tickets/42');

    expect(assigned).toHaveLength(1);

    const url = new URL(assigned[0], 'http://localhost');
    expect(url.pathname).toBe('/api/v1/auth/sign-in');
    expect(url.searchParams.get('returnUrl')).toBe('/tickets/42');
    expect(url.searchParams.get('lang')).toBe('en');
  });

  it('clears the session on sign-out and returns the provider address only when asked', async () => {
    const auth = TestBed.inject(AuthService);

    const restored = auth.restore();
    api().expectOne('/api/v1/auth/session').flush(SESSION);
    await restored;

    const signedOut = auth.signOut({ endProviderSession: true });
    const request = api().expectOne('/api/v1/auth/sign-out');

    expect(request.request.body).toEqual({ allSessions: false, endProviderSession: true });
    request.flush({ signedOut: true, providerSignOutUrl: 'https://idp.example/logout' });

    expect(await signedOut).toBe('https://idp.example/logout');
    expect(auth.isAuthenticated()).toBe(false);
  });

  it('clears the session even when the sign-out call fails', async () => {
    const auth = TestBed.inject(AuthService);

    const restored = auth.restore();
    api().expectOne('/api/v1/auth/session').flush(SESSION);
    await restored;

    const signedOut = auth.signOut();
    api().expectOne('/api/v1/auth/sign-out').flush(null, { status: 500, statusText: 'Error' });

    // A user who asked to sign out must not be left looking at a signed-in screen because the
    // network was unhappy.
    expect(await signedOut).toBeNull();
    expect(auth.isAuthenticated()).toBe(false);
  });

  describe('when the session ends under the user', () => {
    async function signedIn(): Promise<AuthService> {
      const auth = TestBed.inject(AuthService);
      const restored = auth.restore();

      api().expectOne('/api/v1/auth/session').flush(SESSION);
      await restored;

      return auth;
    }

    it('clears the session once and routes to sign-in with the destination preserved', async () => {
      const auth = await signedIn();
      const router = TestBed.inject(Router);

      // Where the user was when the session ended. Stubbed rather than navigated to: the test is
      // about what the service does with the current address, not about the route table.
      vi.spyOn(router, 'url', 'get').mockReturnValue('/tickets/42');

      const navigations: unknown[][] = [];
      vi.spyOn(router, 'navigate').mockImplementation((commands, extras) => {
        navigations.push([commands, extras]);

        return Promise.resolve(true);
      });

      const renewed = auth.renew();
      api()
        .expectOne('/api/v1/auth/session')
        .flush({ code: 'session_expired' }, { status: 401, statusText: 'Unauthorized' });

      expect(await renewed).toBe(false);
      expect(auth.isAuthenticated()).toBe(false);

      expect(navigations).toHaveLength(1);
      expect(navigations[0][0]).toEqual(['/sign-in']);
      expect(navigations[0][1]).toMatchObject({
        // Told what happened in their own language, rather than shown a generic failure...
        queryParams: { error: 'session_expired', returnUrl: '/tickets/42' },

        // ...and Back does not return to a page the session can no longer serve.
        replaceUrl: true,
      });
    });

    it('does not clear or route a second time when there is no session left to lose', async () => {
      const auth = await signedIn();
      const router = TestBed.inject(Router);

      const navigations: unknown[][] = [];
      vi.spyOn(router, 'navigate').mockImplementation((commands) => {
        navigations.push([commands]);

        return Promise.resolve(true);
      });

      auth.expire();
      auth.expire();
      auth.expire();

      // A burst of failing requests must not produce a burst of navigations.
      expect(navigations).toHaveLength(1);
    });

    it('shares one renewal between concurrent callers', async () => {
      await signedIn();

      const renewal = TestBed.inject(SessionRenewal);

      const first = renewal.renew();
      const second = renewal.renew();

      // The same promise, not two calls: presenting the renewal credential twice is what the
      // server reads as theft.
      expect(first).toBe(second);

      api().expectOne('/api/v1/auth/session').flush(SESSION);

      expect(await first).toBe(true);
      expect(await second).toBe(true);
    });
  });
});

describe('safeReturnUrl', () => {
  it('keeps a path inside this application', () => {
    expect(safeReturnUrl('/tickets/42?tab=history')).toBe('/tickets/42?tab=history');
  });

  it.each([
    'https://evil.example/steal',
    '//evil.example',
    '/\\evil.example',
    'javascript:alert(1)',
    '',
    null,
  ])('refuses %p, which would otherwise make sign-in an open redirect', (candidate) => {
    expect(safeReturnUrl(candidate)).toBe('/');
  });
});
