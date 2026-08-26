# Contract: Frontend Internal Interfaces

**Feature**: 001-project-foundation

These are the contracts the Angular workspace exposes to its own features. They matter as much as
the HTTP contracts, because every future feature is written against them and changing them later
touches every screen. Spec: FR-029 to FR-034, FR-035 to FR-039, FR-003.

## Library public surfaces

Only what a library's `public-api.ts` exports is importable. Anything else is internal, and the
ESLint boundary rule fails the build on a deep import.

### `@crm/core`

| Export | Kind | Contract |
|--------|------|----------|
| `AppConfig` | injection token | `{ apiBaseUrl, defaultLanguage, supportedLanguages }`, resolved from `assets/config.json` before the app renders. |
| `provideCrmCore()` | provider function | Registers config loading, interceptors, error handler, and language state in one call from `app.config.ts`. |
| `AppError` | type | `{ kind, code, correlationId, fieldErrors? }`. Every failure reaching a feature is already this shape. |
| `RequestState<T>` | type + helpers | `{ status: 'idle'\|'loading'\|'success'\|'empty'\|'error', data?, error? }`, signal-based. |
| `LanguageService` | service | `language()`, `direction()` signals; `setLanguage(lang)` switches text and direction together and persists the choice. |
| `applyServerErrors(form, error)` | function | Binds `AppError.fieldErrors` onto matching reactive form controls; unmatched fields surface as a form-level error rather than being dropped. |

### `@crm/ui`

| Export | Kind | Contract |
|--------|------|----------|
| `StateContainerComponent` | component | Takes a `RequestState<T>` and renders the correct one of the six mandated states, projecting content only in `success`. |
| `LoadingStateComponent`, `EmptyStateComponent`, `ValidationErrorComponent`, `ForbiddenStateComponent`, `ServerErrorComponent` | components | Individually usable; all text via translation keys; the error variants display the correlation id for support. |
| `AppShellComponent` | component | Layout with navigation, language switcher, and content outlet. Audience-neutral, so a portal app can reuse or replace it (FR-033). |
| theme | SCSS | One Material theme definition; features never define colors or spacing primitives. |

## Rules enforced by lint or test

1. **HTTP only in data-access services.** `HttpClient` may be injected only in files matching
   `*-api.service.ts`. Any other file importing it fails lint (FR-029).
2. **No cross-feature imports.** `features/a/**` may not import from `features/b/**`. Shared code
   moves to a library instead (Constitution I, FR-003).
3. **No deep library imports.** `@crm/core/src/lib/...` is forbidden; only the package entry point.
4. **No hard-coded user-visible strings.** Template text must come from a translation key; a lint
   rule flags literal text nodes in templates, with an allow-list for non-linguistic content.
5. **No physical direction properties.** `margin-left`, `padding-right`, `left:`, `right:`, and
   `text-align: left|right` are forbidden in component styles; logical properties are required so
   RTL needs no per-component override (FR-037).
6. **Key parity.** `ar.json` and `en.json` must contain identical key sets; a verification script
   fails on any difference (FR-039).

## Error mapping contract

The interceptor converts transport and HTTP failures into `AppError` before any feature sees them:

| Condition | `AppError.kind` |
|-----------|-----------------|
| Network failure, timeout, CORS rejection, or unparseable body | `network` |
| 400 with `code: validation_failed` | `validation` |
| 401 | `unauthenticated` |
| 403 | `forbidden` |
| 404 | `notFound` |
| Any other 4xx/5xx, or a response not matching the error contract | `server` |

The user-facing message is always resolved from `AppError.code` through the translation catalogue.
Server-supplied `title` text is never rendered (LR-003). The correlation id is displayed in the
error states so a user can quote it to support.

## Language and direction contract

- `LanguageService.setLanguage()` is the only way language changes. It updates the translation
  scope, sets `dir` and `lang` on the document element, updates the CDK `Directionality` value, and
  persists the choice - atomically, so text and direction can never disagree.
- Bootstrap reads the persisted choice, falling back to `AppConfig.defaultLanguage`.
- Locale-sensitive formatting (dates, numbers) resolves from the active language; no component
  formats dates by hand.
