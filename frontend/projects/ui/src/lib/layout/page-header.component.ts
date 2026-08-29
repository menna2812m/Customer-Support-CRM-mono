import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

/**
 * The top of every screen: what this page is, optionally why, and the actions that belong to the
 * page as a whole.
 *
 * Every page previously hand-rolled an `<h1>` and its own margin, which is why six components
 * carried a `styles:` block to do the same job slightly differently. A page header is a component
 * because "what does the top of a page look like" is a decision the design system should own.
 *
 * Actions are projected rather than configured, so a page can put a primary button, a menu, or
 * nothing there without this component knowing about any of them.
 */
@Component({
  selector: 'crm-page-header',
  imports: [TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <header class="crm-page-header">
      <div class="crm-page-header__text">
        <h1 class="crm-page-header__title">{{ titleKey() | transloco }}</h1>

        @if (descriptionKey()) {
          <p class="crm-page-header__description">{{ descriptionKey()! | transloco }}</p>
        }
      </div>

      <div class="crm-page-header__actions">
        <ng-content />
      </div>
    </header>
  `,
  styles: `
    .crm-page-header {
      display: flex;
      flex-wrap: wrap;
      align-items: flex-start;
      justify-content: space-between;
      gap: var(--crm-space-4);

      /* A rule, not a shadow: the header is separated from the content by the same hairline that
         separates everything else in this system. */
      padding-block-end: var(--crm-space-4);
      margin-block-end: var(--crm-space-5);
      border-block-end: var(--crm-border-width) solid var(--crm-border);
    }

    .crm-page-header__text {
      display: flex;
      flex-direction: column;
      gap: var(--crm-space-1);
      min-inline-size: 0;
    }

    .crm-page-header__title {
      font-size: var(--crm-text-xl);
    }

    .crm-page-header__description {
      color: var(--crm-ink-secondary);
      font-size: var(--crm-text-md);
      max-inline-size: 60ch;
    }

    .crm-page-header__actions {
      display: flex;
      align-items: center;
      gap: var(--crm-space-2);
      flex-wrap: wrap;
    }
  `,
})
export class PageHeaderComponent {
  readonly titleKey = input.required<string>();
  readonly descriptionKey = input<string | null>(null);
}
