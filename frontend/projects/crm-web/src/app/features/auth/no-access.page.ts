import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { ActivatedRoute } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { errorCodeKey } from '@crm/core';

/**
 * Authenticated, and granted nothing (spec US1).
 *
 * A distinct screen rather than a failed sign-in, because the two need different actions from the
 * user: one is "try again", this one is "ask an administrator". The correlation identifier is shown
 * because it is the only handle support can use to find the matching server-side record.
 */
@Component({
  selector: 'crm-no-access-page',
  imports: [MatCardModule, TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <mat-card class="crm-no-access" appearance="outlined">
      <mat-card-content>
        <div role="alert">
          <h1 class="crm-no-access__title">{{ 'auth.noAccess.title' | transloco }}</h1>
          <p class="crm-no-access__message">{{ messageKey | transloco }}</p>
        </div>

        @if (correlationId) {
          <p class="crm-no-access__reference">
            {{ 'states.server.reference' | transloco }}
            <code data-testid="correlation-id">{{ correlationId }}</code>
          </p>
        }
      </mat-card-content>
    </mat-card>
  `,
  styles: `
    .crm-no-access {
      margin-block: var(--crm-space-lg);
      margin-inline: auto;
      max-inline-size: 32rem;
    }

    .crm-no-access__title {
      font: var(--mat-sys-headline-small);
      margin-block-end: var(--crm-space-md);
    }

    .crm-no-access__reference {
      color: var(--mat-sys-on-surface-variant);
      margin-block-start: var(--crm-space-lg);
    }
  `,
})
export class NoAccessPage {
  private readonly parameters = inject(ActivatedRoute).snapshot.queryParamMap;

  protected readonly correlationId = this.parameters.get('correlationId');

  /**
   * A collision needs an administrator to resolve a conflict, which is not the same message as
   * "nobody has granted you anything yet" - so the code chooses the wording.
   */
  protected readonly messageKey = this.parameters.get('error')
    ? errorCodeKey(this.parameters.get('error')!)
    : 'auth.noAccess.message';
}
