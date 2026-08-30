import { Pipe, PipeTransform, inject } from '@angular/core';
import { LanguageService } from '@crm/core';

/**
 * Shows a unit's name in the reader's language (spec FR-007).
 *
 * Falls back to the other language only if the expected one is somehow absent - which the server
 * forbids, since both names are required, but a client that renders an empty string because of a
 * data problem is worse than one that shows the name it does have.
 *
 * Deliberately impure. The name depends on two things - the unit and the ambient language - but
 * only the unit is an input, so a pure pipe caches on the half that does not change when the
 * reader switches language and keeps showing the previous one until something unrelated redraws
 * the row. The cost is a call per change detection cycle on a string comparison, which these lists
 * can afford; the alternative, threading the language through every call site as an argument,
 * makes every template carry a detail none of them care about.
 */
@Pipe({ name: 'unitName', pure: false })
export class UnitNamePipe implements PipeTransform {
  private readonly languages = inject(LanguageService);

  transform(unit: BilingualName | null | undefined): string {
    if (!unit) {
      return '';
    }

    return this.languages.language() === 'ar'
      ? unit.nameAr || unit.nameEn
      : unit.nameEn || unit.nameAr;
  }
}

/**
 * Anything carrying a name in both languages.
 *
 * Structural rather than tied to one feature's type: an organization unit satisfies it, and so does
 * a placement's branch, department, or team. That is why this pipe lives in `@crm/ui` - two
 * features now need the same rule about which language wins, and a second copy of that rule is a
 * second thing that can drift.
 */
export interface BilingualName {
  nameAr: string;
  nameEn: string;
}
