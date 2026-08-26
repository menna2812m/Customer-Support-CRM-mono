import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { TranslocoPipe } from '@jsverse/transloco';

/** Empty state: the request succeeded and there is nothing to show. Not an error. */
@Component({
  selector: 'crm-empty-state',
  imports: [MatButtonModule, TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="crm-state" role="status">
      <h2 class="crm-state__title">{{ titleKey() | transloco }}</h2>
      <p class="crm-state__message">{{ messageKey() | transloco }}</p>
      @if (actionKey()) {
        <button matButton="filled" type="button" (click)="action.emit()">
          {{ actionKey()! | transloco }}
        </button>
      }
    </div>
  `,
  styleUrl: './state.scss',
})
export class EmptyStateComponent {
  readonly titleKey = input('states.empty.title');
  readonly messageKey = input('states.empty.message');
  readonly actionKey = input<string | null>(null);
  readonly action = output<void>();
}
