import { DOCUMENT, Injectable, inject } from '@angular/core';
import { Router } from '@angular/router';
import { APP_CONFIG } from '../config/app-config';
import { LanguageService } from '../i18n/language.service';
import { AuthApiService } from './auth-api.service';
import { AuthSession } from './auth-session.store';
import { SignOutOptions } from './auth.models';

/**
 * The application's view of who is signed in (contracts/frontend-contracts.md).
 *
 * There is no login form anywhere in this application, and there never will be: the provider owns
 * credential entry. Sign-in is a full-page departure to the API, which redirects onward to the
 * provider; the browser comes back to `/auth/complete` holding a cookie, and this service turns
 * that cookie into a session.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly api = inject(AuthApiService);
  private readonly session = inject(AuthSession);
  private readonly config = inject(APP_CONFIG);
  private readonly language = inject(LanguageService);
  private readonly document = inject(DOCUMENT);
  private readonly router = inject(Router);

  /** The signed-in person, or null. */
  readonly user = this.session.user;

  readonly isAuthenticated = this.session.isAuthenticated;

  readonly permissions = this.session.permissions;

  /** Presentation only: hides what the user cannot use. The API refuses regardless. */
  hasPermission(permission: string): boolean {
    return this.session.has(permission);
  }

  /**
   * Leaves the application for the provider, remembering where the user meant to go.
   *
   * A full-page navigation rather than a request: the provider needs to run its own pages - a
   * password prompt, a second factor, a consent screen - and none of that can happen inside an
   * XHR.
   */
  signIn(returnUrl = '/'): void {
    const parameters = new URLSearchParams({
      returnUrl: safeReturnUrl(returnUrl),
      lang: this.language.language(),
    });

    this.document.location.assign(`${this.config.apiBaseUrl}/api/v1/auth/sign-in?${parameters}`);
  }

  /**
   * Rebuilds the session from the renewal cookie. Called at application start, which is what makes
   * a reload - or a return the next morning - cost no provider round trip.
   *
   * Returns false rather than throwing when there is no live session: not being signed in is an
   * ordinary state, not a failure.
   */
  async restore(): Promise<boolean> {
    try {
      const response = await this.api.session();
      this.session.set(response.accessToken, response.user);

      return true;
    } catch {
      this.session.clear();

      return false;
    }
  }

  /**
   * Exchanges the renewal cookie for a fresh access credential, mid-session.
   *
   * Distinct from {@link restore} in what a failure means. At start-up, no session is the ordinary
   * state of a visitor who has not signed in. Here, the person was working a moment ago, so a
   * failure is the session ending under them and is treated as such.
   *
   * Callers must go through {@link SessionRenewal} rather than calling this directly: two
   * concurrent renewals present the same renewal credential twice, which the server reads as reuse.
   */
  async renew(): Promise<boolean> {
    try {
      const response = await this.api.session();
      this.session.set(response.accessToken, response.user);

      return true;
    } catch {
      this.expire();

      return false;
    }
  }

  /**
   * The session ended on the server - it expired, it was revoked, or the account was deactivated.
   *
   * Says so in the user's own language on the sign-in screen and keeps where they were, rather
   * than leaving a generic failure on a page that can no longer load anything (spec FR-018). Does
   * nothing when there was no session to lose, so a burst of failing requests clears and routes
   * once rather than once each.
   */
  expire(): void {
    if (!this.session.isAuthenticated()) {
      return;
    }

    const returnUrl = safeReturnUrl(this.router.url);

    this.session.clear();

    // replaceUrl, so Back does not return to a page the session can no longer serve.
    void this.router.navigate(['/sign-in'], {
      queryParams: { returnUrl, error: 'session_expired' },
      replaceUrl: true,
    });
  }

  /**
   * Ends the CRM session. The local session is cleared whatever the server says, because a user who
   * asked to sign out must not be left looking at a signed-in screen.
   *
   * Returns the provider's sign-out address when the user asked for it, so the caller can finish
   * its own cleanup before leaving.
   */
  async signOut(options: SignOutOptions = {}): Promise<string | null> {
    try {
      const response = await this.api.signOut(options);

      return response.providerSignOutUrl;
    } catch {
      return null;
    } finally {
      this.session.clear();
    }
  }

  /** Drops the local session without calling the server - for a session the server already ended. */
  clear(): void {
    this.session.clear();
  }
}

/**
 * Keeps a hostile value from becoming the destination. The API refuses one too; doing it here as
 * well means the application never even asks.
 */
export function safeReturnUrl(candidate: string | null | undefined): string {
  if (!candidate || !candidate.startsWith('/')) {
    return '/';
  }

  // `//host` and `/\host` are URLs, not paths: a browser reads both as another origin.
  if (candidate.startsWith('//') || candidate.startsWith('/\\') || candidate.includes('://')) {
    return '/';
  }

  return candidate;
}
