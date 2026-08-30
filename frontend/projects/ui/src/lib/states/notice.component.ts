import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { BadgeTone } from '../data/badge.component';

/**
 * A short message about the page as a whole: an action was refused, a change was saved.
 *
 * Distinct from `crm-validation-error`, which lists per-field failures and belongs to a form. The
 * organization screens needed this and did not have it, so a refused delete - "this department still
 * has 2 teams" - was rendered as a bare paragraph with no styling and no consistent placement.
 *
 * The message is always a translation key, never server text. The API contract is explicit that its
 * human-readable text is for developers and logs; the client owns the wording, which is what keeps
 * the interface's voice its own (spec LR-003).
 */
@Component({
  selector: 'crm-notice',
  imports: [TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="crm-notice" [class]="'crm-notice--' + tone()" role="alert">
      <p class="crm-notice__message">{{ messageKey() | transloco: params() }}</p>
    </div>
  `,
  styles: `
    .crm-notice {
      display: flex;
      flex-direction: column;
      gap: var(--crm-space-1);

      padding-inline: var(--crm-space-4);
      padding-block: var(--crm-space-3);
      margin-block-end: var(--crm-space-5);

      border: var(--crm-border-width) solid transparent;
      border-radius: var(--crm-radius);

      /* The tone is carried by a rule on the leading edge as well as by the tint, so it survives
         both a monochrome display and a viewer who cannot separate the hues. */
      border-inline-start-width: 3px;
      font-size: var(--crm-text-md);
    }

    .crm-notice__message {
      font-weight: var(--crm-weight-medium);
    }

    .crm-notice--danger {
      background: var(--crm-danger-soft);
      border-color: var(--crm-danger);
      color: var(--crm-danger);
    }

    .crm-notice--warning {
      background: var(--crm-warning-soft);
      border-color: var(--crm-warning);
      color: var(--crm-warning);
    }

    .crm-notice--success {
      background: var(--crm-success-soft);
      border-color: var(--crm-success);
      color: var(--crm-success);
    }

    .crm-notice--info,
    .crm-notice--neutral {
      background: var(--crm-info-soft);
      border-color: var(--crm-info);
      color: var(--crm-info);
    }
  `,
})
export class NoticeComponent {
  readonly messageKey = input.required<string>();
  readonly tone = input<BadgeTone>('danger');

  /**
   * Values the message interpolates - a count, a name. Still the client's own wording: the numbers
   * come from the server, the sentence around them does not, which is what lets a refusal say how
   * many teams are in the way without quoting server prose (spec LR-003).
   */
  readonly params = input<Record<string, unknown>>({});
}
