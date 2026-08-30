import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { AuthService, errorCodeKey, safeReturnUrl } from '@crm/core';
import { NoticeComponent, PanelComponent } from '@crm/ui';

/**
 * Explains what is about to happen and starts the handshake (spec US1).
 *
 * There is no form here, and no password field anywhere in this application: the provider owns
 * credential entry. This screen exists so the departure is expected rather than abrupt, and so a
 * refusal that already happened has somewhere to be explained.
 */
@Component({
  selector: 'crm-sign-in-page',
  imports: [MatButtonModule, NoticeComponent, PanelComponent, TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!--
      A single-purpose screen reads better narrow and centred than stretched across the content
      container, which is what crm-measure is for.
    -->
    <div class="crm-measure crm-signin">
      <crm-panel titleKey="auth.signIn.title">
        @if (errorKey(); as key) {
          <crm-notice [messageKey]="key" />
        }

        <p class="crm-muted crm-signin__description">
          {{ 'auth.signIn.description' | transloco }}
        </p>

        <button
          matButton="filled"
          type="button"
          [disabled]="redirecting()"
          (click)="start()"
          data-testid="sign-in"
        >
          {{ (redirecting() ? 'auth.completing' : 'auth.signIn.action') | transloco }}
        </button>
      </crm-panel>
    </div>
  `,
  styles: `
    .crm-signin {
      margin-block-start: var(--crm-space-8);
    }

    .crm-signin__description {
      margin-block-end: var(--crm-space-5);
    }
  `,
})
export class SignInPage {
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly redirecting = signal(false);

  /** A code the API refused with, translated the same way as every other error (spec FR-018). */
  protected readonly errorKey = signal<string | null>(readErrorKey(this.route));

  private readonly returnUrl = safeReturnUrl(this.route.snapshot.queryParamMap.get('returnUrl'));

  constructor() {
    // Arriving here while already signed in means a stale link or a Back button, not a request to
    // sign in again.
    if (this.auth.isAuthenticated()) {
      void this.router.navigateByUrl(this.returnUrl);
    }
  }

  protected start(): void {
    this.redirecting.set(true);
    this.auth.signIn(this.returnUrl);
  }
}

function readErrorKey(route: ActivatedRoute): string | null {
  const code = route.snapshot.queryParamMap.get('error');

  return code ? errorCodeKey(code) : null;
}
