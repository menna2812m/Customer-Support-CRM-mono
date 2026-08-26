import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoPipe } from '@jsverse/transloco';

/**
 * Loading state. Announced as a live status so a screen-reader user knows the screen is working
 * rather than idle (spec FR-057). Text comes from translation keys (FR-035).
 */
@Component({
  selector: 'crm-loading-state',
  imports: [MatProgressSpinnerModule, TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="crm-state" role="status" aria-live="polite">
      <mat-spinner diameter="40" [attr.aria-label]="messageKey() | transloco"></mat-spinner>
      <p class="crm-state__message">{{ messageKey() | transloco }}</p>
    </div>
  `,
  styleUrl: './state.scss',
})
export class LoadingStateComponent {
  readonly messageKey = input('states.loading');
}
