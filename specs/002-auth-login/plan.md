# Implementation Plan: Authentication and Login

**Branch**: `002-auth-login` | **Date**: 2026-08-26 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/002-auth-login/spec.md`

## Summary

Fill the authentication seams feature 001 left, for staff only. A staff member signs in through an
OIDC provider, the CRM resolves them to its own user record, grants roles, issues its own
short-lived access credential plus a rotating renewal credential, and every endpoint that has been
denying access by default starts admitting the right people.

**The central technical decision, and a deliberate deviation from feature 001**: the CRM issues its
own access tokens rather than passing the provider's tokens through. The provider authenticates a
person once, at sign-in; from then on the API validates a credential it minted itself.

Feature 001's research assumed the Staff scheme would validate tokens issued by the corporate
identity provider. The clarifications make that untenable - a provider token cannot be revoked by
us (FR-013), cannot be rotated single-use (FR-012), cannot carry CRM permissions (FR-023), and
would put a corporate credential in a browser. Issuing our own token satisfies all four, and the
existing `Staff` scheme accepts it with no code change: `StaffAuthOptions` already carries
`Issuer` and `SigningKey`. The provider interaction moves from "every request" to "once per
sign-in", which is where it belongs.

Everything else composes what already exists: the permission attributes, the dynamic policy
provider, `ICurrentUser`, the audit recorder, the error contract, the correlation middleware, the
Angular interceptor chain, and the placeholder route guard.

## Technical Context

**Language/Version**: C# 14 on .NET 10 (SDK 10.0.400); TypeScript on Angular 22 - unchanged from
feature 001
**Primary Dependencies**: Microsoft.AspNetCore.Authentication.OpenIdConnect (sign-in handshake
only), Microsoft.AspNetCore.Authentication.JwtBearer (already present), Microsoft.IdentityModel.*
for token issuance, `Microsoft.AspNetCore.RateLimiting` (framework built-in), EF Core 10. No new
frontend dependency: the SPA needs no OIDC library because the handshake happens server-side
**Storage**: SQL Server. Five new tables - users, roles, role permissions, role assignments,
sessions - plus a renewal-credential table. First real business schema in the product
**Testing**: xUnit v3 + Shouldly + Testcontainers.MsSql (unchanged); an **in-process fake OIDC
provider** for integration tests, so the suite stays inside its ten-minute budget and needs no
second container
**Target Platform**: Windows Server + IIS in production; Kestrel + `ng serve` in development
**Project Type**: Web application - the same monorepo, filling seams rather than adding structure
**Performance Goals**: Sign-in complete within 15 seconds including both redirects (SC-001); a
failed provider reachable within 10 seconds (SC-011); renewal must not be perceptible to a working
user
**Constraints**: Constitution v1.0.0; the credential split from the clarifications (access in
memory, renewal in a script-inaccessible cookie); sessions server-side so revocation is immediate;
no password material anywhere - staff credentials never reach the CRM; permissions must land within
one renewal cycle
**Scale/Scope**: Tens to low hundreds of staff; a session row per active sign-in; role definitions
in single figures. Nothing here needs to scale beyond a single database

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Mark each gate PASS / FAIL / N/A with a one-line justification. Every FAIL must be recorded in
Complexity Tracking below, otherwise the plan does not proceed to implementation.

| # | Gate (constitution principle) | Status |
|---|-------------------------------|--------|
| I | Business logic sits in Domain/Application, not controllers; no frontend feature reaches into another feature internals | PASS - sign-in orchestration, provisioning, and role resolution are Application use cases; controllers bind, call, and map. The token issuer and provider client are Infrastructure |
| II | EF Core + SQL Server; every schema change ships as an EF Core migration | PASS - one migration adds the identity tables; the auditing interceptor and soft-delete conventions from feature 001 apply unchanged |
| III | Endpoints under `/api/v1`, explicit DTOs (no entities returned), shared pagination/filter/sort contract | PASS - all endpoints under `/api/v1/auth`; DTOs only; no collection endpoint in this feature, so pagination does not arise |
| IV | Every protected operation declares its required permission; authorization enforced server-side; audit records for security-sensitive actions | PASS - anonymous endpoints are the four named in AR-001 and no others; every authentication event is audited through `IAuditRecorder` |
| V | No single-department or single-branch assumption; organizational visibility scoping considered | PASS - placement resolved per FR-025 from claims with a stored fallback, carried in the session, and absent scope means "sees nothing extra" |
| VI | Angular standalone APIs; `core/ shared/ features/` placement; HTTP only in data-access services; Reactive Forms for non-trivial forms | PASS - an `auth` feature folder with `auth-api.service.ts`, session state in `@crm/core`, no new form (the provider owns credential entry) |
| VII | Arabic RTL and English LTR both handled; no hard-coded user-visible strings | PASS - sign-in, no-access, expiry, and rate-limit messages added to both resource files; the active language is passed to the provider (LR-003) |
| VIII | Keys, FKs, unique constraints, indexes; audit columns; no hard delete of traceable business records | PASS with a stated exception - unique on provider subject and on email; sessions and renewal credentials are revoked, never deleted; audit columns via the interceptor. **Exception**: the organizational placement columns carry no foreign key, because the department, branch, and team tables belong to a feature that does not exist yet. Recorded in Complexity Tracking |
| IX | Status changes, assignments, escalations recorded; history never overwritten | PASS - session and renewal state transitions are recorded as authentication events rather than by mutating history |
| X | Consistent error contract; no stack traces to clients; all six Angular UI states handled | PASS - sign-in failure, no-access, expired session, provider outage, and rate limiting each map to a code the client translates; the sign-in screens use the state components |
| XI | Structured logging with correlation id; no secrets or sensitive customer data logged | PASS - the redaction list already covers `token` and `authorization`; the plan adds the session and renewal identifiers to it |
| XII | Attachments go through the storage abstraction; allowed types, sizes, and authorization specified | N/A - no file handling in this feature |
| XIII | Tests cover business rules, authorization, and validation failures; critical Angular workflows tested | PASS - provisioning, collision refusal, default-role grant, rotation, reuse detection, revocation, and population separation all have tests; the frontend covers guard, interceptor refresh, and expiry |
| XIV | Core workflows still function when the AI provider is unavailable; AI output labeled and user-accepted | N/A - no AI capability |
| XV | External vendors sit behind adapters; retry and idempotency defined; failures cannot corrupt CRM state | PASS - the identity provider is reached through one adapter; a provider outage produces a clean failure and leaves no half-created user, because provisioning happens in a single transaction after the handshake succeeds |

**Initial gate result**: PASS. Two N/A entries, both scope-based.

**Post-design re-evaluation**: PASS. The design added one thing worth re-checking against Principle
IV: the renewal endpoint is anonymous by necessity (the access credential has expired by
definition), which makes it the most exposed surface in the feature. It is protected by three
independent things rather than one - the cookie is script-inaccessible and same-site, the endpoint
is rate limited per source, and a reused renewal credential revokes the whole session. No gate
moved.

## Project Structure

### Documentation (this feature)

```text
specs/002-auth-login/
├── plan.md              # This file
├── spec.md              # Feature specification (41 FR, 8 clarifications)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── README.md
│   ├── auth-api.yaml
│   ├── session-contract.md
│   └── frontend-contracts.md
└── checklists/
    └── requirements.md
```

### Source Code (repository root)

Only the files this feature adds or changes. Everything else stays as feature 001 built it.

```text
backend/
├── src/
│   ├── Crm.Domain/Identity/               # User, Role, RolePermission, RoleAssignment, Session
│   ├── Crm.Application/
│   │   ├── Identity/                      # sign-in, provisioning, renewal, sign-out use cases
│   │   └── Abstractions/                  # IIdentityProvider, ITokenIssuer, ISessionStore
│   ├── Crm.Infrastructure/
│   │   ├── Identity/                      # OIDC adapter, token issuer, session store
│   │   └── Persistence/                   # entity configurations + one migration
│   └── Crm.Api/
│       ├── Auth/                          # AuthController, cookie handling, CSRF check
│       ├── Configuration/                 # provider + token + session options, rate limiting
│       └── Common/Security/               # CurrentUser unchanged; claims populated at issuance
└── tests/
    ├── Crm.UnitTests/Identity/
    ├── Crm.IntegrationTests/
    │   ├── Auth/                          # sign-in, renewal, revocation, collision, roles
    │   └── Infrastructure/FakeOidc/       # in-process provider: discovery, jwks, token
    └── Crm.ArchitectureTests/             # existing rules cover the new code unchanged

frontend/projects/
├── core/src/lib/auth/                     # session state, token store, refresh coordination
├── ui/src/lib/shell/                      # user menu with sign-out choices
└── crm-web/src/app/
    ├── core/guards/auth.guard.ts          # placeholder replaced
    └── features/auth/                     # sign-in, callback landing, no-access screens
```

**Structure Decision**: No new projects and no new libraries. The feature adds an `Identity` area to
each backend layer and an `auth` feature to the frontend, and replaces two placeholder files
(`auth.guard.ts`, `authTokenInterceptor`). That is the shape feature 001 predicted for a vertical
feature, and it is worth noting that adding real authentication touches exactly two shared
registration points, as SC-002 of that feature claimed.

## Implementation Phases

| Stage | Delivers | Spec stories |
|-------|----------|--------------|
| A. Identity schema | User, Role, RolePermission, RoleAssignment, Session, RenewalCredential; one migration; seeded roles | US1, US3 |
| B. Provider handshake | OIDC adapter, sign-in initiation and callback, provisioning, collision refusal, default role | US1, US3 |
| C. Token issuance and session | CRM token issuer, session store, access + renewal credentials, cookie handling, CSRF check | US1, US2 |
| D. Renewal, revocation, sign-out | Rotation with reuse detection, sign-out, sign out everywhere, provider sign-out choice | US2 |
| E. Rate limiting | Reusable limiter with named policies, applied to the anonymous endpoints, error contract mapping | US4 |
| F. Frontend | Guard, session state, single-flight refresh, sign-in and no-access screens, user menu, translations | US1, US2, US3 |
| G. Audit and hardening | Authentication events, redaction additions, permission refresh within a cycle, documentation | US3, US4 |

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No constitutional violations. Three choices cost more than the minimum and are recorded here.

| Choice | Why Needed | Simpler Alternative Rejected Because |
|--------|------------|--------------------------------------|
| The CRM issues its own access tokens instead of forwarding the provider's | Immediate revocation (FR-013), single-use rotation (FR-012), CRM permissions in the session (FR-023), and keeping a corporate credential out of the browser (FR-014) are each impossible with a provider-issued token | Validating provider tokens per request is less code and was feature 001's assumption. It fails four clarified requirements, and the failure is silent: sign-out would appear to work while the token kept validating until the provider expired it |
| Server-side session storage | Revocation must be immediate, and rotation needs somewhere to record that a renewal credential was used | A self-contained token with no server state cannot be revoked before it expires. "Short expiry instead of revocation" was rejected: it makes sign-out a lie for up to the token lifetime, on shared workstations |
| An in-process fake OIDC provider for tests | The suite must exercise the real handshake - discovery, code exchange, token validation - without a second container in every run | A real provider container (Keycloak) was rejected for the default suite: it roughly triples suite startup and pushes SC-009 of feature 001 toward its limit. It remains worthwhile as an optional, separately invoked check before a release |
| Organizational placement columns without a foreign key (Constitution VIII) | Placement must be carried now - `ICurrentUser` exposes it and Constitution V forbids assuming one department - but the tables it would reference belong to the organization feature | Inventing department, branch, and team tables here was rejected: it puts another feature's schema in this feature and forces that feature to migrate away from a guess. The columns are nullable, written only from a verified claim or an existing value, and the organization feature adds the constraints when it adds the tables |
