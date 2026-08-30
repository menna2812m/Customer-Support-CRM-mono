import { ChangeDetectionStrategy, Component } from '@angular/core';

/**
 * An identifier: a unit code, a correlation id, a reference number.
 *
 * This is the one element the design spends its boldness on, and it comes from the product's own
 * vocabulary rather than from a style guide. This CRM is full of short immutable codes - `RUH`,
 * `TS-T1` - which people read aloud, compare by eye, and copy by hand. Setting them in a mono face
 * with tabular figures and a hairline border makes them scannable down a column and unmistakable
 * from a name, which is exactly the distinction the data model makes: a name is a label that can
 * change, a code is an identity that cannot.
 *
 * Everything else in this system is quiet so that this can carry meaning.
 */
@Component({
  selector: 'crm-code',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<code class="crm-code"><ng-content /></code>`,
  styles: `
    .crm-code {
      display: inline-block;

      padding-inline: var(--crm-space-2);
      padding-block: 0.0625rem;

      background: var(--crm-surface-sunken);
      border: var(--crm-border-width) solid var(--crm-border);
      border-radius: var(--crm-radius-sm);

      font-family: var(--crm-font-mono);
      font-size: var(--crm-text-xs);
      font-variant-numeric: tabular-nums;
      letter-spacing: var(--crm-tracking-wide);
      color: var(--crm-ink-secondary);
      white-space: nowrap;

      /* A code is a Latin identifier even inside an Arabic sentence, so it is never mirrored and
         never reordered by the bidirectional algorithm. Without this, "TS-T1" can render as "T1-TS"
         beside Arabic text. */
      direction: ltr;
      unicode-bidi: isolate;
    }
  `,
})
export class CodeComponent {}
