# Contract: Frontend Authentication Surfaces

**Feature**: 002-auth-login

What this feature adds to `@crm/core` and `@crm/ui`, and the two placeholders it replaces. Every
future feature depends on these, so they are contracts rather than implementation detail.

## Replaced placeholders

| File | Was | Becomes |
|---|---|---|
| `crm-web/src/app/core/guards/auth.guard.ts` | `() => true` | Redirects unauthenticated visitors to sign-in, preserving the intended destination |
| `@crm/core` `authTokenInterceptor` | `next(request)` | Attaches the access credential, renews once on expiry, retries the original request |

Feature 001 built both as named seams precisely so this feature changes two files rather than
threading authentication through every screen.

## New in `@crm/core`

| Export | Kind | Contract |
|---|---|---|
| `AuthService` | service | `user()`, `isAuthenticated()`, `permissions()` signals; `signIn(returnUrl)`, `signOut(options)`, `restore()`. The only place the access credential lives, and it lives in memory |
| `hasPermission(name)` | function | Reads the current session. **Presentation only** - the backend decides, always |
| `authGuard` inputs | - | The guard consumes `AuthService`; no feature writes its own guard |

`AuthService.restore()` runs at application start: it calls the session endpoint with the renewal
cookie, so a user who reloads the page or returns tomorrow is signed in again without a redirect,
provided their session is still alive.

## Renewal behaviour

The interceptor:

1. attaches the credential when one is held;
2. on a `401` with `session_expired`, triggers **one** renewal shared by every waiting request -
   concurrent calls do not each start their own, which would rotate the credential several times and
   trip reuse detection;
3. retries the original request once with the new credential;
4. on renewal failure, clears the session once and routes to sign-in with the destination preserved.

A request that fails twice is surfaced as an `AppError` like any other, through the existing
normalization - features do not learn a second error shape.

## New in `@crm/ui`

| Export | Contract |
|---|---|
| `UserMenuComponent` | Displays the signed-in name and offers sign out, sign out everywhere, and "also end access on this computer". Keyboard operable, accessible name on every control, per FR-057 of feature 001 |
| `AppShellComponent` (changed) | Gains a slot for the user menu. Still audience-neutral - the portal application will place its own menu in the same slot |

## New screens in `crm-web`

| Route | Purpose | States it must handle |
|---|---|---|
| `/sign-in` | Explains and starts the handshake | idle, redirecting, provider unavailable, rate limited |
| `/auth/complete` | Landing after the provider redirect; exchanges the cookie and routes onward | loading, success, failed |
| `/no-access` | Authenticated but granted nothing | its own message, with the correlation id for support |

None of these collect credentials. The provider owns credential entry, which is why this feature
adds no login form and no password field anywhere.

## Rules enforced by lint or test

The rules from feature 001 continue to apply unchanged: `HttpClient` only in `*-api.service.ts`, no
cross-feature imports, no deep library imports, no physical direction properties, and translation
key parity. This feature adds no new rule - it adds keys under `auth.*` and `errors.code.*` in both
languages.

## What the frontend must never do

- **Store the access credential.** Memory only. Not `localStorage`, not `sessionStorage`, not a
  cookie it sets itself.
- **Read or write the renewal cookie.** It is script-inaccessible by design; the browser sends it.
- **Decide access.** `hasPermission` hides navigation the user cannot use. It never guards data - the
  API refuses regardless, and a guard that returns true grants nothing.
- **Render a server error string.** Codes map to translated messages, as established in feature 001.
