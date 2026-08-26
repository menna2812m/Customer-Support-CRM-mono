import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { RequestState } from '@crm/core';
import { EmptyStateComponent } from './empty-state.component';
import { ForbiddenStateComponent } from './forbidden-state.component';
import { LoadingStateComponent } from './loading-state.component';
import { ServerErrorComponent } from './server-error.component';
import { ValidationErrorComponent } from './validation-error.component';

/**
 * Renders exactly one of the six mandated screen states from a {@link RequestState}, projecting
 * the caller's content only on success (Constitution X, spec FR-032).
 *
 * Features bind a request signal to this component instead of writing their own `@if` ladders,
 * which is what makes "every screen handles every state" true by construction rather than by
 * discipline.
 */
@Component({
  selector: 'crm-state-container',
  imports: [
    EmptyStateComponent,
    ForbiddenStateComponent,
    LoadingStateComponent,
    ServerErrorComponent,
    ValidationErrorComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @switch (state().status) {
      @case ('idle') {
        <ng-content select="[crmIdle]" />
      }
      @case ('loading') {
        <crm-loading-state />
      }
      @case ('empty') {
        <crm-empty-state />
      }
      @case ('success') {
        <ng-content />
      }
      @case ('error') {
        @switch (state().error?.kind) {
          @case ('validation') {
            <crm-validation-error [error]="state().error!" />
          }
          @case ('unauthenticated') {
            <crm-forbidden-state
              titleKey="states.unauthenticated.title"
              messageKey="states.unauthenticated.message"
            />
          }
          @case ('forbidden') {
            <crm-forbidden-state />
          }
          @case ('notFound') {
            <crm-forbidden-state
              titleKey="states.notFound.title"
              messageKey="states.notFound.message"
            />
          }
          @default {
            <crm-server-error
              [correlationId]="state().error?.correlationId ?? null"
              (retry)="retry.emit()"
            />
          }
        }
      }
    }
  `,
})
export class StateContainerComponent<T> {
  readonly state = input.required<RequestState<T>>();
  readonly retry = output<void>();
}
