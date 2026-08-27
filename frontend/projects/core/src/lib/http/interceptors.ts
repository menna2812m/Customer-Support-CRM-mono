import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { AuthSession } from '../auth/auth-session.store';
import { SessionRenewal } from '../auth/session-renewal.service';
import { APP_CONFIG } from '../config/app-config';
import { AppError, AppErrorKind, FieldError } from '../state/app-error';

/**
 * The cross-cutting request seam (spec FR-030). Order matters and is fixed in provideCrmCore:
 *
 *   baseUrl -> correlation -> auth token -> error normalization
 *
 * Components never touch HttpClient; only `*-api.service.ts` files do, and a lint rule enforces it.
 */

const CORRELATION_HEADER = 'X-Correlation-Id';

/** Turns a relative API path into an absolute one using the runtime configuration. */
export const baseUrlInterceptor: HttpInterceptorFn = (request, next) => {
  const config = inject(APP_CONFIG);

  const isAbsolute = /^https?:\/\//i.test(request.url);
  const isAsset = request.url.startsWith('assets/') || request.url.startsWith('/assets/');

  if (isAbsolute || isAsset || !config.apiBaseUrl) {
    return next(request);
  }

  const path = request.url.startsWith('/') ? request.url : `/${request.url}`;
  return next(request.clone({ url: `${config.apiBaseUrl}${path}` }));
};

/**
 * Sends a correlation identifier with every API call so a user-visible failure can be traced to
 * its server-side log entries (spec FR-041). The server reuses this value rather than replacing it.
 */
export const correlationInterceptor: HttpInterceptorFn = (request, next) => {
  if (request.headers.has(CORRELATION_HEADER)) {
    return next(request);
  }

  return next(request.clone({ setHeaders: { [CORRELATION_HEADER]: newCorrelationId() } }));
};

/**
 * Attaches the access credential the session holds, and the header that proves this request came
 * from the application rather than from a cross-site form.
 *
 * The credential lives in memory only (see {@link AuthSession}). Requests that already carry an
 * Authorization header - the session and sign-out calls, authenticated by the renewal cookie - are
 * left alone, as are calls to anything other than this application's API.
 *
 * When the credential has expired, the interceptor renews once - shared with every other request
 * that met the same expiry - and retries exactly once. A renewal that fails means the session
 * ended on the server; `AuthService` has already cleared it and routed to sign-in by then, so the
 * original refusal is surfaced unchanged rather than swallowed.
 *
 * The authentication endpoints are skipped entirely: they are authenticated by the renewal cookie
 * rather than by a credential, and renewing on their behalf would recurse.
 */
export const authTokenInterceptor: HttpInterceptorFn = (request, next) => {
  const session = inject(AuthSession);
  const renewal = inject(SessionRenewal);

  if (isAuthEndpoint(request.url) || request.headers.has('Authorization')) {
    return next(request);
  }

  const token = session.accessToken();

  if (!token) {
    return next(request);
  }

  return next(withCredential(request, token)).pipe(
    catchError((error: unknown) => {
      if (!isUnauthenticated(error)) {
        return throwError(() => error);
      }

      return from(renewal.renew()).pipe(
        switchMap((renewed) => {
          const refreshed = session.accessToken();

          // One retry, and only with a credential that is actually newer: retrying without one
          // would simply reproduce the same refusal.
          return renewed && refreshed
            ? next(withCredential(request, refreshed))
            : throwError(() => error);
        }),
      );
    }),
  );
};

function withCredential(request: HttpRequest<unknown>, token: string): HttpRequest<unknown> {
  return request.clone({
    setHeaders: { Authorization: `Bearer ${token}`, 'X-Requested-With': 'CrmWeb' },
  });
}

/** The endpoints the renewal cookie authenticates. Never renewed on behalf of. */
function isAuthEndpoint(url: string): boolean {
  return /\/api\/v\d+\/auth\//i.test(url);
}

/**
 * Recognises a refusal before and after normalization. The error interceptor sits downstream of
 * this one, so in the running application an {@link AppError} arrives here - but a test that
 * exercises this interceptor on its own sees the raw response, and both must read the same.
 */
function isUnauthenticated(error: unknown): boolean {
  if (error instanceof HttpErrorResponse) {
    return error.status === 401;
  }

  return (
    typeof error === 'object' && error !== null && (error as AppError).kind === 'unauthenticated'
  );
}

/**
 * Converts every failure - transport, HTTP, or a body that does not match the error contract -
 * into a single {@link AppError}. Features never see an HttpErrorResponse (spec FR-031).
 */
export const errorNormalizationInterceptor: HttpInterceptorFn = (request, next) =>
  next(request).pipe(catchError((error: unknown) => throwError(() => toAppError(error))));

export function toAppError(error: unknown): AppError {
  if (!(error instanceof HttpErrorResponse)) {
    return { kind: 'server', code: 'unexpected_error', correlationId: '' };
  }

  // Status 0 means the request never completed: offline, DNS, timeout, or a CORS rejection.
  if (error.status === 0) {
    return { kind: 'network', code: 'network_unavailable', correlationId: '', status: 0 };
  }

  const body = (error.error ?? {}) as ProblemDetailsBody;
  const code = typeof body.code === 'string' ? body.code : fallbackCode(error.status);

  return {
    kind: kindFor(error.status, code),
    code,
    correlationId: typeof body.correlationId === 'string' ? body.correlationId : '',
    status: error.status,
    fieldErrors: toFieldErrors(body.errors),
  };
}

interface ProblemDetailsBody {
  code?: unknown;
  correlationId?: unknown;
  errors?: unknown;
}

function kindFor(status: number, code: string): AppErrorKind {
  if (status === 401) {
    return 'unauthenticated';
  }

  if (status === 403) {
    return 'forbidden';
  }

  if (status === 404) {
    return 'notFound';
  }

  if (status === 400 && code === 'validation_failed') {
    return 'validation';
  }

  return 'server';
}

function fallbackCode(status: number): string {
  switch (status) {
    case 400:
      return 'malformed_request';
    case 401:
      return 'unauthenticated';
    case 403:
      return 'forbidden';
    case 404:
      return 'not_found';
    case 409:
      return 'conflict';
    default:
      return 'unexpected_error';
  }
}

function toFieldErrors(raw: unknown): Record<string, FieldError[]> | undefined {
  if (!Array.isArray(raw)) {
    return undefined;
  }

  const grouped: Record<string, FieldError[]> = {};

  for (const entry of raw) {
    if (!entry || typeof entry !== 'object') {
      continue;
    }

    const { field, code, message } = entry as Record<string, unknown>;
    if (typeof field !== 'string' || typeof code !== 'string') {
      continue;
    }

    grouped[field] ??= [];
    grouped[field].push({ code, message: typeof message === 'string' ? message : '' });
  }

  return Object.keys(grouped).length > 0 ? grouped : undefined;
}

function newCorrelationId(): string {
  return crypto.randomUUID().replace(/-/g, '');
}
