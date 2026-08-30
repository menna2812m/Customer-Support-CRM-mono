import { ChangeDetectionStrategy, Component, input } from '@angular/core';

export type BadgeTone = 'neutral' | 'success' | 'warning' | 'danger' | 'info';

/**
 * A short status, rendered so it can be read at a glance without competing with the record it
 * describes.
 *
 * Status was previously conveyed by translated text plus `opacity: 0.6` on the whole row. That fails
 * twice: opacity is contrast-only signalling, which is exactly what accessibility guidance rules
 * out, and a faded row is hard to read rather than obviously inactive.
 *
 * The tone is carried by a tinted background and a matching text colour, never by colour alone - the
 * label always says what the badge means, so a person who cannot distinguish the tints loses
 * nothing.
 */
@Component({
  selector: 'crm-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="crm-badge" [class]="'crm-badge--' + tone()"><ng-content /></span>`,
  styles: `
    .crm-badge {
      display: inline-flex;
      align-items: center;
      gap: var(--crm-space-1);

      padding-inline: var(--crm-space-2);
      padding-block: 0.125rem;

      border: var(--crm-border-width) solid transparent;
      border-radius: var(--crm-radius-sm);

      font-size: var(--crm-text-xs);
      font-weight: var(--crm-weight-medium);
      line-height: 1.5;
      white-space: nowrap;
    }

    .crm-badge--neutral {
      background: var(--crm-surface-sunken);
      border-color: var(--crm-border);
      color: var(--crm-ink-secondary);
    }

    .crm-badge--success {
      background: var(--crm-success-soft);
      color: var(--crm-success);
    }

    .crm-badge--warning {
      background: var(--crm-warning-soft);
      color: var(--crm-warning);
    }

    .crm-badge--danger {
      background: var(--crm-danger-soft);
      color: var(--crm-danger);
    }

    .crm-badge--info {
      background: var(--crm-info-soft);
      color: var(--crm-info);
    }
  `,
})
export class BadgeComponent {
  readonly tone = input<BadgeTone>('neutral');
}
