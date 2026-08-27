import { HttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Observable, firstValueFrom } from 'rxjs';
import { provideCrmTesting } from '../../testing/test-providers';
import { AuthSession } from '../auth/auth-session.store';
import { AuthUser } from '../auth/auth.models';

const USER: AuthUser = {
  id: 'a1',
  displayName: 'Layla Hassan',
  email: 'layla@example.com',
  population: 'Staff',
  permissions: ['tickets.view'],
  scope: null,
};

/**
 * Spec FR-012: renewal is invisible to the person using the application.
 *
 * The case worth testing is concurrency. An access credential expires at a moment, not at a
 * convenient point between requests, so several calls in flight meet the same 401 within
 * milliseconds. Each renewal spends the renewal credential and issues a replacement - so two
 * concurrent renewals would present the same spent credential and the server would correctly read
 * that as theft and end the session. Single-flight is what stops ordinary concurrency from
 * signing the user out.
 */
describe('authTokenInterceptor renewal', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideCrmTesting()] });

    TestBed.inject(AuthSession).set('expired-credential', USER);
  });

  function api(): HttpTestingController {
    return TestBed.inject(HttpTestingController);
  }

  function http(): HttpClient {
    return TestBed.inject(HttpClient);
  }

  it('renews once for three concurrent requests, and all three then succeed', async () => {
    const first = firstValueFrom(http().get('/api/v1/tickets/1'));
    const second = firstValueFrom(http().get('/api/v1/tickets/2'));
    const third = firstValueFrom(http().get('/api/v1/tickets/3'));

    const initial = api().match((request) => request.url.startsWith('/api/v1/tickets/'));
    expect(initial).toHaveLength(3);

    for (const request of initial) {
      expect(request.request.headers.get('Authorization')).toBe('Bearer expired-credential');
      unauthorized(request);
    }

    await settle();

    // The decisive assertion: three refusals, one renewal.
    const renewals = api().match('/api/v1/auth/session');
    expect(renewals).toHaveLength(1);

    renewals[0].flush({ accessToken: 'renewed-credential', expiresInSeconds: 900, user: USER });

    await settle();

    const retried = api().match((request) => request.url.startsWith('/api/v1/tickets/'));
    expect(retried).toHaveLength(3);

    for (const [index, request] of retried.entries()) {
      // Retried with the new credential, not the one that had just been refused.
      expect(request.request.headers.get('Authorization')).toBe('Bearer renewed-credential');
      request.flush({ id: index + 1 });
    }

    await expect(first).resolves.toEqual({ id: 1 });
    await expect(second).resolves.toEqual({ id: 2 });
    await expect(third).resolves.toEqual({ id: 3 });
  });

  it('retries exactly once, so a credential the server keeps refusing does not loop', async () => {
    const call = capture(http().get('/api/v1/tickets/1'));

    unauthorized(api().expectOne('/api/v1/tickets/1'));

    await settle();

    api()
      .expectOne('/api/v1/auth/session')
      .flush({ accessToken: 'renewed-credential', expiresInSeconds: 900, user: USER });

    await settle();

    unauthorized(api().expectOne('/api/v1/tickets/1'));

    await settle();

    // No second renewal and no third attempt: the failure is surfaced instead.
    api().expectNone('/api/v1/auth/session');

    expect(await call).toMatchObject({ kind: 'unauthenticated' });
  });

  it('does not attempt to renew the renewal call itself', async () => {
    const call = capture(http().post('/api/v1/auth/session', {}, { withCredentials: true }));

    unauthorized(api().expectOne('/api/v1/auth/session'));

    await settle();

    // Renewing a failed renewal is the shape of an infinite loop, and the endpoint is
    // cookie-authenticated anyway - the credential this interceptor holds is irrelevant to it.
    api().expectNone('/api/v1/auth/session');

    expect(await call).toMatchObject({ kind: 'unauthenticated' });
  });

  it('leaves a request alone when there is no credential to attach', async () => {
    TestBed.inject(AuthSession).clear();

    const call = capture(http().get('/api/v1/tickets/1'));
    const request = api().expectOne('/api/v1/tickets/1');

    expect(request.request.headers.has('Authorization')).toBe(false);

    request.flush({ id: 1 });

    expect(await call).toEqual({ id: 1 });
  });

  it('surfaces a failure that is not a refusal without touching the session', async () => {
    const call = capture(http().get('/api/v1/tickets/1'));

    api()
      .expectOne('/api/v1/tickets/1')
      .flush({ code: 'unexpected_error' }, { status: 500, statusText: 'Error' });

    await settle();

    api().expectNone('/api/v1/auth/session');

    expect(await call).toMatchObject({ kind: 'server' });
    expect(TestBed.inject(AuthSession).accessToken()).toBe('expired-credential');
  });

  afterEach(() => api().verify());
});

function unauthorized(request: TestRequest): void {
  request.flush({ code: 'session_expired' }, { status: 401, statusText: 'Unauthorized' });
}

/**
 * Renewal is a promise, so the retry it triggers is issued on a later microtask than the refusal
 * that provoked it. Yielding here is what lets the test observe the request the application makes
 * rather than the one it has not made yet.
 */
function settle(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * Subscribes and attaches the failure handler in the same turn, so a refusal the test is about to
 * assert on is never briefly an unhandled rejection. Returns the value or the error, whichever the
 * call produces.
 */
function capture(source: Observable<unknown>): Promise<unknown> {
  return firstValueFrom(source).catch((error: unknown) => error);
}
