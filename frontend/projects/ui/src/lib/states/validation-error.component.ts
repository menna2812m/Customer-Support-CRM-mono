import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { AppError } from '@crm/core';
import { TranslocoPipe } from '@jsverse/transloco';

/**
 * Validation failure. Field messages are translated from the stable per-field codes, never from
 * server-supplied text (spec LR-003).
 */
@Component({
  selector: 'crm-validation-error',
  imports: [TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="crm-state crm-state--error" role="alert">
      <h2 class="crm-state__title">{{ titleKey() | transloco }}</h2>
      <ul class="crm-state__list">
        @for (entry of entries(); track entry.field) {
          <li>{{ entry.field }}: {{ 'errors.field.' + entry.code | transloco }}</li>
        }
      </ul>
    </div>
  `,
  styleUrl: './state.scss',
})
export class ValidationErrorComponent {
  readonly titleKey = input('states.validation.title');
  readonly error = input.required<AppError>();

  protected entries(): { field: string; code: string }[] {
    const fieldErrors = this.error().fieldErrors ?? {};
    return Object.entries(fieldErrors).flatMap(([field, errors]) =>
      errors.map((e) => ({ field, code: e.code })),
    );
  }
}
