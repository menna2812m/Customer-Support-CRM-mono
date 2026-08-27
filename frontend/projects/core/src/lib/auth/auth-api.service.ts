import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { SessionResponse, SignOutOptions, SignOutResponse } from './auth.models';

/** A cross-site form post cannot set this, which is what closes the renewal cookie's CSRF surface. */
export const APPLICATION_HEADER = { 'X-Requested-With': 'CrmWeb' };

/**
 * The two calls the session depends on.
 *
 * Both send `withCredentials`, because both are authenticated by the renewal cookie rather than by
 * anything this code can read - the cookie is HttpOnly by design, so the browser attaches it and
 * script never sees it.
 */
@Injectable({ providedIn: 'root' })
export class AuthApiService {
  private readonly http = inject(HttpClient);

  /**
   * Exchanges the renewal cookie for an access credential. One path serves both the moment after
   * sign-in and every renewal afterwards, so there is one behaviour to reason about rather than two.
   */
  session(): Promise<SessionResponse> {
    return firstValueFrom(
      this.http.post<SessionResponse>(
        '/api/v1/auth/session',
        {},
        { headers: APPLICATION_HEADER, withCredentials: true },
      ),
    );
  }

  signOut(options: SignOutOptions): Promise<SignOutResponse> {
    return firstValueFrom(
      this.http.post<SignOutResponse>(
        '/api/v1/auth/sign-out',
        {
          allSessions: options.allSessions ?? false,
          endProviderSession: options.endProviderSession ?? false,
        },
        { headers: APPLICATION_HEADER, withCredentials: true },
      ),
    );
  }
}
