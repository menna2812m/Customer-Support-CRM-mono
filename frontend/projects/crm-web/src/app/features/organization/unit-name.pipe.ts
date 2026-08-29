import { Pipe, PipeTransform, inject } from '@angular/core';
import { LanguageService } from '@crm/core';
import { OrganizationUnit } from './organization-api.service';

/**
 * Shows a unit's name in the reader's language (spec FR-007).
 *
 * Falls back to the other language only if the expected one is somehow absent - which the server
 * forbids, since both names are required, but a client that renders an empty string because of a
 * data problem is worse than one that shows the name it does have.
 */
@Pipe({ name: 'unitName' })
export class UnitNamePipe implements PipeTransform {
  private readonly languages = inject(LanguageService);

  transform(unit: Pick<OrganizationUnit, 'nameAr' | 'nameEn'> | null | undefined): string {
    if (!unit) {
      return '';
    }

    return this.languages.language() === 'ar'
      ? unit.nameAr || unit.nameEn
      : unit.nameEn || unit.nameAr;
  }
}
