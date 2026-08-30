import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { LanguageService } from '@crm/core';
import { provideCrmTesting } from '@crm/core/testing';
import { UnitNamePipe } from './unit-name.pipe';

@Component({
  imports: [UnitNamePipe],
  template: `{{ unit | unitName }}`,
})
class NameHost {
  protected readonly unit = { nameAr: 'الفوترة', nameEn: 'Billing' };
}

/**
 * Spec FR-007: a unit shows its name in the reader's language.
 *
 * Switching language is the case that matters. The unit object does not change when the reader
 * switches - only the ambient language does - so a pipe that caches on its input alone keeps
 * showing the previous language until something else happens to redraw the row. That leaves an
 * Arabic reader looking at English names on a screen that is otherwise entirely Arabic.
 */
describe('UnitNamePipe', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideCrmTesting()] });
  });

  function render(): { text: () => string; setLanguage: (language: 'ar' | 'en') => void } {
    const fixture = TestBed.createComponent(NameHost);
    fixture.detectChanges();

    return {
      text: () => (fixture.nativeElement as HTMLElement).textContent?.trim() ?? '',
      setLanguage: (language) => {
        TestBed.inject(LanguageService).setLanguage(language);
        fixture.detectChanges();
      },
    };
  }

  it('shows the English name to an English reader', () => {
    const page = render();

    page.setLanguage('en');

    expect(page.text()).toBe('Billing');
  });

  it('shows the Arabic name once the reader switches to Arabic', () => {
    const page = render();

    page.setLanguage('ar');

    expect(page.text()).toBe('الفوترة');
  });

  it('goes back to the English name when the reader switches back', () => {
    const page = render();

    page.setLanguage('ar');
    page.setLanguage('en');

    expect(page.text()).toBe('Billing');
  });
});
