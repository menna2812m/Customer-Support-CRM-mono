import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

/**
 * Authorization failure. Says nothing about whether the resource exists - the API makes forbidden
 * and not-found indistinguishable, and the UI must not undo that (spec FR-026).
 */
@Component({
  selector: 'crm-forbidden-state',
  imports: [TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="crm-state crm-state--error" role="alert">
      <h2 class="crm-state__title">{{ titleKey() | transloco }}</h2>
      <p class="crm-state__message">{{ messageKey() | transloco }}</p>
    </div>
  `,
  styleUrl: './state.scss',
})
export class ForbiddenStateComponent {
  readonly titleKey = input('states.forbidden.title');
  readonly messageKey = input('states.forbidden.message');
}
