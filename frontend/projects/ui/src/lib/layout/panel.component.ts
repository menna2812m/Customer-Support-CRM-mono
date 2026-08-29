import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

/**
 * A bounded region of a page: a bordered surface with an optional heading.
 *
 * This is the system's answer to a card, and the difference is the point. A card lifts off the page
 * with a shadow; a panel is ruled onto it with a hairline. In a record-keeping interface the second
 * reads as structure and the first reads as decoration, and consistency about which one we use is
 * most of what makes an interface look designed rather than assembled.
 *
 * Replaces the `mat-card appearance="outlined"` repeated across four screens, each wrapped in a
 * `<section>` with its own hand-written bottom margin.
 */
@Component({
  selector: 'crm-panel',
  imports: [TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="crm-panel" [class.crm-panel--flush]="flush()">
      @if (titleKey()) {
        <div class="crm-panel__header">
          <h2 class="crm-panel__title">{{ titleKey()! | transloco }}</h2>
          <div class="crm-panel__actions">
            <ng-content select="[crmPanelActions]" />
          </div>
        </div>
      }

      <div class="crm-panel__body">
        <ng-content />
      </div>
    </section>
  `,
  styles: `
    .crm-panel {
      background: var(--crm-surface);
      border: var(--crm-border-width) solid var(--crm-border);
      border-radius: var(--crm-radius-lg);

      /* Deliberately no box-shadow. See _tokens.scss - elevation is for overlays only. */
    }

    .crm-panel__header {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: var(--crm-space-3);

      padding: var(--crm-space-3) var(--crm-space-4);
      border-block-end: var(--crm-border-width) solid var(--crm-border);

      /* The header is inset rather than raised, so the panel reads as one object with a ruled top
         section rather than two stacked ones. */
      background: var(--crm-surface-sunken);
      border-start-start-radius: var(--crm-radius-lg);
      border-start-end-radius: var(--crm-radius-lg);
    }

    .crm-panel__title {
      font-size: var(--crm-text-md);
      font-weight: var(--crm-weight-semibold);
    }

    .crm-panel__actions {
      display: flex;
      align-items: center;
      gap: var(--crm-space-2);
    }

    .crm-panel__body {
      padding: var(--crm-space-4);
    }

    /* For a panel whose content brings its own padding - a table, most often. */
    .crm-panel--flush .crm-panel__body {
      padding: 0;
    }
  `,
})
export class PanelComponent {
  readonly titleKey = input<string | null>(null);

  /** True when the content manages its own padding, as a table does. */
  readonly flush = input(false);
}
