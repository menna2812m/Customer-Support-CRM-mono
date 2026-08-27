# Constitution Compliance and Definition of Done

**Feature**: 002-auth-login | **Verified**: 2026-08-27

Re-verification of the Constitution Check table in [plan.md](./plan.md) against the delivered code
(tasks T097, T098), plus the Definition of Done from Constitution section 17.

## Constitution Check, re-verified against the implementation

| # | Principle | Result | Evidence in the delivered code |
|---|---|---|---|
| I | Layering | **PASS** | `StaffSignIn`, `EffectivePermissions`, and `DeactivateUser` are Application types; `AuthController` binds, calls, and maps. The token issuer, session store, and OIDC client are Infrastructure, behind `ITokenIssuer`, `ISessionStore`, `IIdentityStore`, and `IIdentityProviderClient`. The union-and-catalog-check rule moved out of the SQL query into `EffectivePermissions.Resolve` for exactly this reason. The architecture tests still pass unchanged: `Crm.Api` references no EF Core or token-library type. |
| II | EF Core + SQL Server, migrations | **PASS** | Two migrations - `Identity` for the seven tables, `IdentitySeed` for the three system roles and their grants. The auditing interceptor and soft-delete conventions from feature 001 apply unchanged. |
| III | `/api/v1`, DTOs, shared contracts | **PASS** | Five endpoints under `/api/v1/auth`. DTOs only - `SessionResponse`, `CurrentUserResponse`, `SignOutRequest`/`Response`; no entity is returned. No collection endpoint, so pagination does not arise. The drift test compares the live document against `contracts/auth-api.yaml`. |
| IV | Backend-enforced authorization | **PASS** | Three endpoints are anonymous, and only the three AR-001 names them: sign-in, callback, and session. `me` and `sign-out` require a credential; everything else inherits the deny-by-default fallback. Every authentication decision is written to `AuthenticationEvent` **and** mirrored to `IAuditRecorder`. The frontend permission check is documented as presentation-only in `docs/conventions.md`, and `app.spec.ts` asserts that hiding a link leaves the route reachable. |
| V | Multi-level organization model | **PASS** | Placement is resolved per FR-025 - verified claims when present, the stored value otherwise, null when neither - written to the user record and carried in the credential. Nothing assumes a single department or branch. |
| VI | Feature-first Angular | **PASS** | An `auth` feature folder with three screens, session state in `@crm/core` split across `AuthSession` (state), `AuthService` (behaviour), `AuthApiService` (the only `HttpClient` user), and `SessionRenewal` (single-flight). No new form: the provider owns credential entry. |
| VII | Arabic and English | **PASS** | 78 keys in each resource file, parity enforced by the gate. Sign-in, no-access, session-expiry, and rate-limit messages exist in both. The active language is passed to the provider (LR-003). Logical CSS enforced by script. |
| VIII | Integrity and traceability | **PASS with the stated exception** | Unique index on provider subject and on email; composite keys on the join tables; restrict deletes; bounded strings; filtered index on session revocation. Sessions and renewal credentials are revoked, never deleted. **Exception unchanged**: placement columns carry no foreign key because the organization feature does not exist yet - recorded in plan.md's Complexity Tracking. |
| IX | History never overwritten | **PASS** | Session state transitions are recorded as authentication events rather than by mutating history. A spent renewal credential records which credential replaced it, so a reuse can be traced back through the rotation chain. |
| X | Error handling and UI states | **PASS** | Six new codes, each translated in both languages: `sign_in_failed`, `provider_unavailable`, `no_access`, `identity_collision`, `session_expired`, `rate_limited`. Throttling answers with the same contract plus `Retry-After`. No provider error text ever reaches a client. |
| XI | Structured logging | **PASS** | `sessionid`, `renewal`, and `refresh` added to the redaction list. An integration test takes a full sign-in cycle and asserts that no authentication event, audit record, or detail field contains the access credential, either renewal cookie, or the stored hash of either. |
| XII | File handling abstraction | **N/A** | No file handling in this feature. |
| XIII | Testing | **PASS** | 145 backend tests (47 unit, 93 integration, 5 architecture) and 66 frontend tests, all passing. Authorization is the subject matter, so the authorization tests are the feature: rotation, reuse detection, both expiry limits, CSRF header and origin, session independence, sign-out everywhere, deactivation, role change landing on renewal, forged permission claims, and population separation. |
| XIV | AI optional | **N/A** | No AI capability. |
| XV | Integrations behind adapters | **PASS** | The provider is reached only through `OpenIdConnectClient` behind `IIdentityProviderClient`, with a bounded timeout. A provider outage produces `provider_unavailable` and leaves no half-created user, asserted by test. |

**Result: no violations.** Two N/A entries, both scope-based, and one exception carried forward from
the plan unchanged.

### What the re-check actually changed

Re-verification is worth doing only if it can fail, so the two places where it did:

1. **Principle I.** The union of role permissions and the catalog check were being done inside the
   EF query in `IdentityStore`. That is a rule living in the store. It moved to
   `EffectivePermissions.Resolve` in the Application layer, which also made it unit-testable away
   from a database - six tests that did not exist before.
2. **Principle IV.** The CSRF defence was the custom header alone. A header can be set by a
   cross-origin XHR, and CORS blocks only the *response* - by which time the renewal credential has
   already rotated. An `Origin` check against the CORS allowlist was added to both
   cookie-authenticated endpoints, with a test for each direction.

## Definition of Done (Constitution section 17)

| Item | Status |
|---|---|
| Specification requirements implemented | **Yes** for every requirement this feature owns. The one unexecuted item is verification against a real provider, not implementation - see Outstanding. |
| Backend validation exists | **Yes** - `returnUrl` is refused unless it is a relative path inside the application; the flow cookie is authenticated by data protection and treated as untrusted when it fails to unprotect; rate-limit policy names are validated at startup against the endpoints that reference them. |
| Backend authorization exists where required | **Yes** - deny by default; three deliberate anonymous endpoints; population and permission enforced from the signed credential and from nothing a caller supplies. |
| Database migration exists where required | **Yes** - `Identity` and `IdentitySeed`. Startup reads the seeded grants back and refuses to start if any names a permission the catalog no longer declares. |
| Angular states handled | **Yes** - the sign-in, completion, and no-access screens cover loading, error, and authorization-failure; the completion screen is a live region so a screen reader is told what is happening. |
| Arabic and English considered | **Yes** - 78 keys each, parity and logical-CSS checks in the gate, direction handled globally as feature 001 established. |
| Tests for critical rules pass | **Yes** - 211 tests across both stacks, all passing. |
| Errors follow application conventions | **Yes** - one contract everywhere, including the throttling path, which is written by `ErrorContractSetup` rather than by the rate limiter. |
| Logging follows security requirements | **Yes** - redaction extended, and asserted against a real sign-in cycle rather than assumed. |
| Relevant documentation updated | **Yes** - `docs/testing.md` (how to sign in inside a test, and what the fake provider does and does not stand in for), `docs/production-configuration.md` (the same-domain constraint, the full settings table, provider setup, the bootstrap administrator), `docs/getting-started.md` (signing in locally), `docs/conventions.md` (declaring a permission, and why a frontend check is never a boundary). |

## Outstanding, and why

| Item | Status | Why |
|---|---|---|
| T096 - execute `quickstart.md` against a real provider container | **Not executed** | Needs a Keycloak container and a browser session, neither of which is part of the automated gate. The fake provider exercises the real `OpenIdConnectClient` against real discovery, code exchange, and token validation, so what remains unverified is the provider-specific part: claim naming, and the exact redirect-URI match. Both are configuration, both are called out in the quickstart's "easy to get wrong" table, and neither is exercised by any code path the tests skip. This is the one place where the feature's residual risk lives, and it is stated rather than smoothed over. |
| Corporate identity provider metadata | **Open for the identity owners** | The staff scheme is disabled by default and every provider-facing value is configuration. `docs/production-configuration.md` lists exactly what is needed. |
| Placement foreign keys | **Deferred to the organization feature** | Unchanged from the plan. The columns are nullable and written only from a verified claim or an existing value. |

## Notes worth carrying forward

- **The tasks list was behind the code.** Several tasks marked pending in `tasks.md` were already
  implemented when this work resumed - rotation, reuse detection, permission resolution, the
  default-role grant, and the bootstrap administrator among them. They were verified against the
  delivered code before being ticked rather than re-implemented.
- **One assertion was wrong, not the code.** A first draft of the rotation test asserted that two
  renewals produce different *access* credentials. They do not: a credential is a signed statement
  about a session at a second, so two issued in the same second are byte-identical and equally
  valid. Rotation is a property of the renewal credential. The test now says so explicitly, because
  the next person to read it would otherwise wonder.
