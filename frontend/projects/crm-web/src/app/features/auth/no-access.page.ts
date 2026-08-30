import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { errorCodeKey } from '@crm/core';
import { CodeComponent, PanelComponent } from '@crm/ui';

/**
 * Authenticated, and granted nothing (spec US1).
 *
 * A distinct screen rather than a failed sign-in, because the two need different actions from the
 * user: one is "try again", this one is "ask an administrator". The correlation identifier is shown
 * because it is the only handle support can use to find the matching server-side record.
 */
@Component({
  selector: 'crm-no-access-page',
  imports: [CodeComponent, PanelComponent, TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="crm-measure crm-no-access">
      <crm-panel titleKey="auth.noAccess.title">
        <div role="alert">
          <p class="crm-muted">{{ messageKey | transloco }}</p>
        </div>

        @if (correlationId) {
          <p class="crm-no-access__reference">
            <span class="crm-hint">{{ 'states.server.reference' | transloco }}</span>
            <crm-code data-testid="correlation-id">{{ correlationId }}</crm-code>
          </p>
        }
      </crm-panel>
    </div>
  `,
  styles: `
    .crm-no-access {
      margin-block-start: var(--crm-space-8);
    }

    .crm-no-access__reference {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--crm-space-2);
      margin-block-start: var(--crm-space-5);
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
