import { HttpErrorResponse } from '@angular/common/http';
import { toAppError } from './interceptors';

/**
 * Spec FR-031: every failure a feature sees has already been normalized, and the mapping from
 * HTTP status to error kind is the contract features rely on when choosing a screen state.
 */
describe('toAppError', () => {
  function httpError(status: number, body: unknown): HttpErrorResponse {
    return new HttpErrorResponse({ status, error: body, statusText: 'error' });
  }

  it('maps a transport failure to the network kind, with no correlation id', () => {
    const error = toAppError(httpError(0, null));

    expect(error.kind).toBe('network');
    expect(error.correlationId).toBe('');
  });

  it('maps 401 to unauthenticated and keeps the correlation id', () => {
    const error = toAppError(httpError(401, { code: 'unauthenticated', correlationId: 'abc' }));

    expect(error.kind).toBe('unauthenticated');
    expect(error.code).toBe('unauthenticated');
    expect(error.correlationId).toBe('abc');
  });

  it('maps 403 to forbidden', () => {
    expect(toAppError(httpError(403, { code: 'forbidden' })).kind).toBe('forbidden');
  });

  it('maps 404 to notFound', () => {
    expect(toAppError(httpError(404, { code: 'not_found' })).kind).toBe('notFound');
  });

  it('maps a validation failure and groups field errors by field', () => {
    const error = toAppError(
      httpError(400, {
        code: 'validation_failed',
        correlationId: 'xyz',
        errors: [
          { field: 'message', code: 'required', message: 'Message is required.' },
          { field: 'repeatCount', code: 'range', message: 'Out of range.' },
        ],
      }),
    );

    expect(error.kind).toBe('validation');
    expect(Object.keys(error.fieldErrors ?? {})).toEqual(['message', 'repeatCount']);
    expect(error.fieldErrors?.['message'][0].code).toBe('required');
  });

  it('maps an unexpected server failure to the server kind', () => {
    const error = toAppError(
      httpError(500, { code: 'unexpected_error', correlationId: 'trace-1' }),
    );

    expect(error.kind).toBe('server');
    expect(error.correlationId).toBe('trace-1');
  });

  it('degrades safely when the body does not follow the error contract', () => {
    // A proxy or gateway can return HTML. The feature must still receive a usable AppError.
    const error = toAppError(httpError(502, '<html>Bad gateway</html>'));

    expect(error.kind).toBe('server');
    expect(error.code).toBe('unexpected_error');
    expect(error.correlationId).toBe('');
  });
});
