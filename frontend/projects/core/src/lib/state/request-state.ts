import { Signal, computed, signal } from '@angular/core';
import { AppError } from './app-error';

/**
 * The six states every screen must handle (Constitution X, spec FR-032). Modelling them as one
 * discriminated value is what stops a feature from silently forgetting the empty or forbidden
 * case.
 */
export type RequestStatus = 'idle' | 'loading' | 'success' | 'empty' | 'error';

export interface RequestState<T> {
  status: RequestStatus;
  data?: T;
  error?: AppError;
}

export const idleState = <T>(): RequestState<T> => ({ status: 'idle' });

export const loadingState = <T>(): RequestState<T> => ({ status: 'loading' });

export const errorState = <T>(error: AppError): RequestState<T> => ({ status: 'error', error });

/**
 * Resolves a successful payload to `success` or `empty`. The caller supplies the emptiness test
 * because "empty" means different things for a list, a search, and a single record.
 */
export function successState<T>(
  data: T,
  isEmpty: (value: T) => boolean = defaultIsEmpty,
): RequestState<T> {
  return { status: isEmpty(data) ? 'empty' : 'success', data };
}

function defaultIsEmpty(value: unknown): boolean {
  if (Array.isArray(value)) {
    return value.length === 0;
  }

  if (value && typeof value === 'object' && 'items' in value) {
    const items = (value as { items?: unknown }).items;
    return Array.isArray(items) && items.length === 0;
  }

  return value === null || value === undefined;
}

/** Signal-backed holder for a request lifecycle, for use in components. */
export class RequestSignal<T> {
  private readonly state = signal<RequestState<T>>(idleState<T>());

  readonly value: Signal<RequestState<T>> = this.state.asReadonly();
  readonly status = computed(() => this.state().status);
  readonly data = computed(() => this.state().data);
  readonly error = computed(() => this.state().error);

  setLoading(): void {
    this.state.set(loadingState<T>());
  }

  setSuccess(data: T, isEmpty?: (value: T) => boolean): void {
    this.state.set(successState(data, isEmpty));
  }

  setError(error: AppError): void {
    this.state.set(errorState<T>(error));
  }

  reset(): void {
    this.state.set(idleState<T>());
  }
}
