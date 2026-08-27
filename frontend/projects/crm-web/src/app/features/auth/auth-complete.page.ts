import { ChangeDetectionStrategy, Component, OnInit, inject } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { AuthService, safeReturnUrl } from '@crm/core';
import { LoadingStateComponent } from '@crm/ui';

/** Refusals that mean "we know who you are and you still cannot come in" - a different screen. */
const NO_ACCESS_CODES = new Set(['no_access', 'identity_collision']);

/**
 * Where the provider round trip lands (spec US1).
 *
 * Nothing secret arrives in this URL - the credential is behind a cookie the browser holds and
 * script cannot read. This screen's whole job is to exchange that cookie for a session and send
 * the user onward to wherever they were originally going.
 */
@Component({
  selector: 'crm-auth-complete-page',
  imports: [LoadingStateComponent, TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- A polite live region: this screen has no heading and nothing to focus, so without one a
         screen-reader user is told nothing at all while the handshake finishes. -->
    <p class="crm-auth-complete__label" role="status">{{ 'auth.completing' | transloco }}</p>
    <crm-loading-state />
  `,
  styles: `
    .crm-auth-complete__label {
      margin-block: var(--crm-space-lg) 0;
      text-align: center;
    }
  `,
})
export class AuthCompletePage implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  async ngOnInit(): Promise<void> {
    const parameters = this.route.snapshot.queryParamMap;
    const returnUrl = safeReturnUrl(parameters.get('returnUrl'));
    const error = parameters.get('error');

    if (error) {
      await this.refuse(error, parameters.get('correlationId'));

      return;
    }

    // The one call that turns the cookie into a session. Its failure is indistinguishable from
    // never having signed in, and is treated as such.
    const restored = await this.auth.restore();

    await (restored
      ? this.router.navigateByUrl(returnUrl)
      : this.router.navigate(['/sign-in'], {
          queryParams: { error: 'sign_in_failed', returnUrl },
          replaceUrl: true,
        }));
  }

  private async refuse(code: string, correlationId: string | null): Promise<void> {
    const destination = NO_ACCESS_CODES.has(code) ? '/no-access' : '/sign-in';

    // replaceUrl throughout: this page is a waypoint, and Back should return the user to where
    // they started rather than replaying a completed handshake.
    await this.router.navigate([destination], {
      queryParams: { error: code, correlationId },
      replaceUrl: true,
    });
  }
}
