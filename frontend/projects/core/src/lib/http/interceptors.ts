import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
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
 * Attachment point for caller credentials. Deliberately inert in this feature: the authentication
 * feature replaces the body here, and no other file changes (spec FR-023, FR-030).
 */
export const authTokenInterceptor: HttpInterceptorFn = (request, next) => next(request);

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
