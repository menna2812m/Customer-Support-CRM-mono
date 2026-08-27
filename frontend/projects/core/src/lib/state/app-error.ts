/**
 * The single client-side failure shape. Every failure a feature sees has already been normalized
 * into this by the HTTP interceptor chain, so no feature parses a server response by hand
 * (spec FR-030, FR-031).
 */
export interface AppError {
  kind: AppErrorKind;
  /** Machine-readable code from the API error contract; drives the translated message. */
  code: string;
  /** Identifier the user can quote to support. Empty when the request never reached the server. */
  correlationId: string;
  /** Per-field validation failures, keyed by the client-facing field path. */
  fieldErrors?: Record<string, FieldError[]>;
  /** HTTP status, when there was a response. */
  status?: number;
}

export interface FieldError {
  code: string;
  message: string;
}

export type AppErrorKind =
  'network' | 'validation' | 'unauthenticated' | 'forbidden' | 'notFound' | 'server';

/**
 * Translation key for a failure. Server-supplied text is never rendered (spec LR-003): the code
 * is mapped to a translated message, with a per-kind fallback when a code has no specific entry.
 */
export function errorMessageKey(error: AppError): string {
  return errorCodeKey(error.code);
}

/**
 * The same mapping for a bare code. Sign-in refusals arrive as a query parameter on a redirect
 * rather than as a response body, and they must read identically to every other failure.
 */
export function errorCodeKey(code: string): string {
  return `errors.code.${code}`;
}

export function errorFallbackKey(error: AppError): string {
  return `errors.kind.${error.kind}`;
}
