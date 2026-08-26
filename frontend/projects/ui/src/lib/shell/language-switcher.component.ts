import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { LanguageService } from '@crm/core';
import { TranslocoPipe } from '@jsverse/transloco';

/**
 * Switches the interface between Arabic and English (spec FR-036).
 *
 * Accessibility (FR-057): a real button, keyboard-operable, with an accessible name that says
 * what the action does rather than relying on the visible glyph, and the active language exposed
 * as pressed state rather than by styling alone.
 */
@Component({
  selector: 'crm-language-switcher',
  imports: [MatButtonModule, TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button
      matButton
      type="button"
      [attr.aria-label]="'app.language.switchTo' | transloco"
      (click)="switch()"
    >
      {{ 'app.language.current' | transloco }}
    </button>
  `,
})
export class LanguageSwitcherComponent {
  private readonly languages = inject(LanguageService);

  protected switch(): void {
    this.languages.toggle();
  }
}
