import { ErrorHandler, Injectable } from '@angular/core';
import { AppError } from '../state/app-error';

/**
 * Last line of defence for anything a feature did not handle (spec FR-031).
 *
 * It never renders raw technical detail: the console entry is for developers, and the user sees
 * whatever error state the screen is showing. Correlation identifiers are preserved so a report
 * can be traced to the server logs.
 */
@Injectable()
export class GlobalErrorHandler implements ErrorHandler {
  handleError(error: unknown): void {
    const appError = asAppError(error);

    if (appError) {
      console.error(
        `[crm] Unhandled ${appError.kind} failure (${appError.code})` +
          (appError.correlationId ? ` correlation=${appError.correlationId}` : ''),
      );
      return;
    }

    console.error('[crm] Unhandled application error', error);
  }
}

function asAppError(error: unknown): AppError | null {
  if (!error || typeof error !== 'object') {
    return null;
  }

  const candidate = error as Partial<AppError>;
  return typeof candidate.kind === 'string' && typeof candidate.code === 'string'
    ? (candidate as AppError)
    : null;
}
