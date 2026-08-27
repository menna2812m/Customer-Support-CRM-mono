import { Injectable, inject } from '@angular/core';
import { AuthService } from './auth.service';

/**
 * One renewal at a time (spec FR-012).
 *
 * An access credential expires while the tab is busy, so several requests can meet the same 401
 * within milliseconds of each other. Each renewal spends the renewal credential and issues a
 * replacement, so a second concurrent renewal would present a credential the first one has already
 * spent - which the server correctly reads as reuse and answers by revoking the whole session.
 *
 * Single-flight is therefore not an optimisation. Without it, ordinary concurrency would look
 * exactly like a stolen credential and sign the user out.
 *
 * Held apart from the interceptor so the promise survives across requests, and apart from
 * {@link AuthService} so that "renew" and "who is signed in" stay separate concerns.
 */
@Injectable({ providedIn: 'root' })
export class SessionRenewal {
  private readonly auth = inject(AuthService);

  private inFlight: Promise<boolean> | null = null;

  /**
   * Renews the access credential, joining a renewal already under way rather than starting a
   * second one. Resolves false when the session has ended - the caller must not retry.
   */
  renew(): Promise<boolean> {
    this.inFlight ??= this.run();

    return this.inFlight;
  }

  private async run(): Promise<boolean> {
    try {
      return await this.auth.renew();
    } finally {
      // Cleared as this attempt settles, so the next expiry starts a fresh one rather than
      // replaying a stale answer.
      this.inFlight = null;
    }
  }
}
