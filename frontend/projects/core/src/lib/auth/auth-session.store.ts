import { Injectable, computed, signal } from '@angular/core';
import { AuthUser } from './auth.models';

/**
 * Where the session lives while the tab is open: in memory, in signals, and nowhere else.
 *
 * Deliberately separate from {@link AuthService}. The HTTP interceptor needs to read the credential
 * on every request, and the service needs HttpClient to obtain one; splitting state from behaviour
 * keeps that from becoming a circular dependency.
 *
 * Nothing here is persisted. `localStorage` and `sessionStorage` survive the tab and are readable
 * by any script on the origin, which is exactly what an access credential must not be (spec
 * FR-016). Surviving a reload is the renewal cookie's job, and the browser holds that.
 */
@Injectable({ providedIn: 'root' })
export class AuthSession {
  private readonly credential = signal<string | null>(null);
  private readonly currentUser = signal<AuthUser | null>(null);

  /** The signed-in person, or null. */
  readonly user = this.currentUser.asReadonly();

  readonly isAuthenticated = computed(() => this.currentUser() !== null);

  /** Permission names from the current session. Presentation only - the API decides. */
  readonly permissions = computed<readonly string[]>(() => this.currentUser()?.permissions ?? []);

  private readonly granted = computed(() => new Set(this.permissions()));

  /**
   * Read by the HTTP interceptor. A function rather than a signal so that reading the credential
   * never registers a reactive dependency and re-runs somebody's effect on every renewal.
   */
  accessToken(): string | null {
    return this.credential();
  }

  /** True when the session carries this permission. Hides what the user cannot use; grants nothing. */
  has(permission: string): boolean {
    return this.granted().has(permission);
  }

  set(accessToken: string, user: AuthUser): void {
    this.credential.set(accessToken);
    this.currentUser.set(user);
  }

  clear(): void {
    this.credential.set(null);
    this.currentUser.set(null);
  }
}
