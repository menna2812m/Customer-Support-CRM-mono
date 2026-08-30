/*
 * Public API surface of @crm/core.
 *
 * Cross-cutting application services: runtime configuration, HTTP infrastructure, error
 * normalization, request state, and (from US3) language and direction.
 * Only what is exported here is importable by features (Constitution VI).
 */

export { APP_CONFIG, CONFIG_READY, loadAppConfig, provideAppConfig } from './lib/config/app-config';
export type { AppConfig, SupportedLanguage } from './lib/config/app-config';

export { provideCrmCore } from './lib/provide-crm-core';

export { provideCrmI18n } from './lib/i18n/provide-crm-i18n';
export { LanguageService } from './lib/i18n/language.service';
export type { LayoutDirection } from './lib/i18n/language.service';
export { TranslationLoader } from './lib/i18n/translation-loader';
export { CrmMissingHandler } from './lib/i18n/missing-handler';

export {
  authTokenInterceptor,
  baseUrlInterceptor,
  correlationInterceptor,
  languageInterceptor,
  errorNormalizationInterceptor,
  toAppError,
} from './lib/http/interceptors';

export { AuthService, safeReturnUrl } from './lib/auth/auth.service';
export { AuthSession } from './lib/auth/auth-session.store';
export { AuthApiService } from './lib/auth/auth-api.service';
export { SessionRenewal } from './lib/auth/session-renewal.service';
export type {
  AuthUser,
  OrganizationScope,
  SessionResponse,
  SignOutOptions,
  SignOutResponse,
} from './lib/auth/auth.models';

export { errorCodeKey, errorFallbackKey, errorMessageKey } from './lib/state/app-error';
export type { AppError, AppErrorKind, FieldError } from './lib/state/app-error';

export {
  RequestSignal,
  errorState,
  idleState,
  loadingState,
  successState,
} from './lib/state/request-state';
export type { RequestState, RequestStatus } from './lib/state/request-state';

export type { PagedResult } from './lib/http/paged-result';

export { GlobalErrorHandler } from './lib/errors/global-error-handler';

export { applyServerErrors, serverErrorCodes } from './lib/forms/apply-server-errors';
