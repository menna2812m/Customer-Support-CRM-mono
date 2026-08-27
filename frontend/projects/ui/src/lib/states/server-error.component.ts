import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { TranslocoPipe } from '@jsverse/transloco';

/**
 * Server or network failure. Shows the correlation identifier so a user can quote it to support -
 * that identifier is the only handle the API hands out (spec FR-018).
 */
@Component({
  selector: 'crm-server-error',
  imports: [MatButtonModule, TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="crm-state crm-state--error" role="alert">
      <h2 class="crm-state__title">{{ 'states.server.title' | transloco }}</h2>
      <p class="crm-state__message">{{ messageKey() | transloco }}</p>
      @if (correlationId()) {
        <p class="crm-state__reference">
          {{ 'states.server.reference' | transloco }} <code>{{ correlationId() }}</code>
        </p>
      }
      <button matButton="filled" type="button" (click)="retry.emit()">
        {{ 'states.server.retry' | transloco }}
      </button>
    </div>
  `,
  styleUrl: './state.scss',
})
export class ServerErrorComponent {
  readonly messageKey = input('states.server.message');
  readonly correlationId = input<string | null>(null);
  readonly retry = output<void>();
}
