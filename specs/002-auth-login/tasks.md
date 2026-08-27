---

description: "Task list for Authentication and Login (002-auth-login)"
---

# Tasks: Authentication and Login

**Input**: Design documents from `/specs/002-auth-login/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Required. Constitution Principle XIII applies as it did in feature 001: business rules,
authorization rules, and validation failures MUST have tests, and critical Angular workflows MUST
have frontend tests. Authorization is the subject matter here, so the authorization tests are the
feature, not an afterthought.

**Organization**: Grouped by user story so each can be implemented and verified independently.

**Building on feature 001**: this feature fills seams rather than creating structure. Two
placeholder files are replaced (`auth.guard.ts`, `authTokenInterceptor`), the `Staff` scheme is
enabled, and `ICurrentUser`, `IAuditRecorder`, the permission attributes, the error contract, and
the interceptor chain are used as-built.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1-US4, mapping to the spec's user stories
- Every task names the file or directory it touches

## Path Conventions

- **Backend**: `backend/src/Crm.Api/`, `Crm.Application/`, `Crm.Domain/`, `Crm.Infrastructure/`
- **Backend tests**: `backend/tests/Crm.UnitTests/`, `Crm.IntegrationTests/`, `Crm.ArchitectureTests/`
- **Frontend**: `frontend/projects/crm-web/`, `frontend/projects/core/`, `frontend/projects/ui/`

---

## Phase 1: Setup

**Purpose**: Configuration, packages, and the cross-cutting values the rest of the feature needs.

- [X] T001 Add `Microsoft.AspNetCore.Authentication.OpenIdConnect` and `Microsoft.IdentityModel.Tokens` to `backend/Directory.Packages.props` and reference them from `backend/src/Crm.Api/Crm.Api.csproj`
- [X] T002 Add `ProviderOptions` (authority, client id, client secret, configurable claim names for subject/name/email/department/branch/team) to `backend/src/Crm.Api/Configuration/CrmOptions.cs`, extending the existing `StaffAuthOptions`
- [X] T003 Add `SessionOptions` (access credential minutes, inactivity hours, absolute hours, cookie name) and `IdentityOptions` (bootstrap administrator, default role name - nullable) in `backend/src/Crm.Api/Configuration/CrmOptions.cs`
- [X] T004 Register the new options with validation in `backend/src/Crm.Api/Configuration/CrmConfiguration.cs`, and extend the startup fail-fast check so an enabled provider without an authority or client id names both problems at once
- [X] T005 [P] Add the six new error codes (`sign_in_failed`, `provider_unavailable`, `no_access`, `identity_collision`, `session_expired`, `rate_limited`) to `backend/src/Crm.Application/Common/ErrorCodes.cs`
- [X] T006 [P] Add `auth.*` and the six `errors.code.*` entries to `frontend/projects/crm-web/public/assets/i18n/en.json` and `ar.json`, keeping key parity
- [X] T007 [P] Add `sessionid`, `renewal`, and `refresh` to the redaction list in `backend/src/Crm.Api/Configuration/LoggingSetup.cs`, so a session or credential identifier can never reach a log file
- [X] T008 [P] Add non-secret provider, session, and identity defaults to `backend/src/Crm.Api/appsettings.json`, `appsettings.Development.json`, and `appsettings.Production.json`

**Checkpoint**: Configuration binds and the application still starts with authentication disabled.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The identity schema, the token and session machinery, and the test harness. Every
user story depends on these.

**⚠️ CRITICAL**: No user story phase can begin until this phase completes.

- [X] T009 [P] Add `User` entity in `backend/src/Crm.Domain/Identity/User.cs` implementing `IAuditableEntity`, `ISoftDeletable`, and `IHasOrganizationScope`, with provider subject, normalized email, display name, population, active state, and placement
- [X] T010 [P] Add `Role`, `RolePermission`, and `RoleAssignment` entities in `backend/src/Crm.Domain/Identity/`
- [X] T011 [P] Add `Session` and `RenewalCredential` entities in `backend/src/Crm.Domain/Identity/`, with revocation reason and rotation chain per data-model.md
- [X] T012 [P] Add `AuthenticationEvent` entity in `backend/src/Crm.Domain/Identity/AuthenticationEvent.cs`
- [X] T013 Add EF configurations for all seven entities in `backend/src/Crm.Infrastructure/Persistence/Configurations/`: unique index on provider subject and on email, composite keys for the join tables, bounded strings, restrict deletes, filtered index on session revocation
- [X] T014 Create the migration in `backend/src/Crm.Infrastructure/Persistence/Migrations/` and seed the `Administrator`, `Agent`, and `ReadOnly` roles, with `Administrator` resolved from the permission registry rather than a written list
- [X] T015 [P] Add `ITokenIssuer` and `ISessionStore` abstractions in `backend/src/Crm.Application/Abstractions/`, plus the `SignInResult` and `SessionSnapshot` shapes they exchange
- [X] T016 [P] Add `IIdentityProviderClient` abstraction in `backend/src/Crm.Application/Abstractions/` covering discovery, code exchange, identity-token validation, and the end-session address
- [X] T017 Implement `TokenIssuer` in `backend/src/Crm.Infrastructure/Identity/TokenIssuer.cs`: HMAC-SHA256 signing with the key from `ISecretsSource`, a key identifier in the header, and the claim set from `contracts/session-contract.md`
- [X] T018 Implement `SessionStore` in `backend/src/Crm.Infrastructure/Identity/SessionStore.cs`: create, renew with rotation, revoke, revoke-all, and hash-only storage of renewal credentials
- [X] T019 Implement `OpenIdConnectClient` in `backend/src/Crm.Infrastructure/Identity/OpenIdConnectClient.cs` against standard discovery, with authorization-code exchange, PKCE, nonce validation, and a bounded timeout so a slow provider fails cleanly
- [X] T020 Register the identity services in `backend/src/Crm.Infrastructure/DependencyInjection.cs`, keeping `Crm.Api` free of provider and token library types so the existing architecture test continues to pass
- [X] T021 Point the `Staff` scheme at CRM-issued credentials in `backend/src/Crm.Api/Configuration/AuthenticationSetup.cs` (own issuer, own signing key), leaving the `Portal` scheme registered and disabled exactly as it is
- [X] T022 Build the in-process fake OIDC provider in `backend/tests/Crm.IntegrationTests/Infrastructure/FakeOidc/`: discovery document, JWKS, authorization endpoint, and token endpoint signing identity tokens with a test key
- [X] T023 Rework `backend/tests/Crm.IntegrationTests/Infrastructure/TestTokens.cs` to mint CRM-shaped access credentials (session claim included) so the tests written in feature 001 keep exercising the real scheme
- [X] T024 Add sign-in helpers to `backend/tests/Crm.IntegrationTests/Infrastructure/` that complete a full handshake against the fake provider and return an authenticated client, so each later test does not repeat the flow

**Checkpoint**: A migrated database, a token that validates, a session that can be created and
revoked, and a test harness that can sign in. No endpoint exists yet.

---

## Phase 3: User Story 1 - A staff member signs in and reaches their work (Priority: P1) 🎯 MVP

**Goal**: A real person signs in through the provider and reaches the application with their
identity resolved.

**Independent Test**: With the fake provider (or a real one), complete sign-in and confirm the
application loads with that identity and that `GET /api/v1/diagnostics/items` succeeds where it
previously returned 401.

- [X] T025 [US1] Add the sign-in initiation endpoint in `backend/src/Crm.Api/Auth/AuthController.cs`: store PKCE verifier, nonce, return path and language in a short-lived cookie, then redirect to the provider
- [X] T026 [US1] Validate `returnUrl` in `backend/src/Crm.Api/Auth/` so only a relative path within the application is accepted - an absolute or foreign URL must be refused, closing the open-redirect hole this parameter would otherwise open
- [X] T027 [US1] Add the provider callback endpoint in `backend/src/Crm.Api/Auth/AuthController.cs`: exchange the code, validate the identity token, and redirect to the application carrying no credential in the URL
- [X] T028 [US1] Implement `ResolveOrProvisionUser` in `backend/src/Crm.Application/Identity/`: look up by provider subject, refresh name, email and placement, and create the user when unknown - all in one transaction
- [X] T029 [US1] Implement the identity-collision refusal in `backend/src/Crm.Application/Identity/`: an unknown subject whose email belongs to another user is refused, recorded, and neither linked nor duplicated
- [X] T030 [US1] Implement placement resolution in `backend/src/Crm.Application/Identity/`: verified claims when present, the stored value otherwise, and null when neither - written to the user record
- [X] T031 [US1] Refuse an inactive user in the sign-in use case even when the provider authenticates them, with the `no_access` code
- [X] T032 [US1] Add the session endpoint (`POST /api/v1/auth/session`) in `backend/src/Crm.Api/Auth/AuthController.cs`, returning the access credential and the current user, and setting the renewal cookie
- [X] T033 [US1] Add cookie handling in `backend/src/Crm.Api/Auth/`: HttpOnly, Secure, SameSite=Lax, path-scoped to the renewal and sign-out endpoints
- [X] T034 [US1] Add the CSRF defence for cookie-authenticated endpoints in `backend/src/Crm.Api/Auth/`: require the custom header and check `Origin` against the CORS allowlist
- [X] T035 [US1] Map provider failures to `provider_unavailable` and handshake failures to `sign_in_failed` in `backend/src/Crm.Api/Auth/`, never surfacing provider error text
- [X] T036 [P] [US1] Implement `AuthService` in `frontend/projects/core/src/lib/auth/auth.service.ts`: in-memory credential, user and permission signals, `signIn`, `restore`
- [X] T037 [US1] Replace the placeholder guard in `frontend/projects/crm-web/src/app/core/guards/auth.guard.ts` with a real redirect to sign-in preserving the intended destination
- [X] T038 [P] [US1] Add the sign-in, completion, and no-access screens in `frontend/projects/crm-web/src/app/features/auth/`, using the state components and translated strings
- [X] T039 [US1] Call `AuthService.restore()` during application start in `frontend/projects/crm-web/src/app/app.config.ts`, so a reload does not require a provider round trip
- [X] T040 [P] [US1] Integration test in `backend/tests/Crm.IntegrationTests/Auth/`: a full handshake against the fake provider yields a session, and a previously 401 endpoint now succeeds
- [X] T041 [P] [US1] Integration test in `backend/tests/Crm.IntegrationTests/Auth/`: an unknown subject whose email belongs to another user is refused with `identity_collision`, no user is created, and the event is recorded
- [X] T042 [P] [US1] Integration test in `backend/tests/Crm.IntegrationTests/Auth/`: an inactive user is refused, and a user with no permissions receives `no_access` rather than a blank success
- [X] T043 [P] [US1] Integration test in `backend/tests/Crm.IntegrationTests/Auth/`: a `returnUrl` pointing at another host is refused, so sign-in cannot be used as an open redirect
- [X] T044 [P] [US1] Integration test in `backend/tests/Crm.IntegrationTests/Auth/`: an unreachable provider produces `provider_unavailable` within the timeout, and no half-created user remains
- [X] T045 [P] [US1] Unit test in `backend/tests/Crm.UnitTests/Identity/`: provisioning refreshes name, email, and placement on a returning user without changing their identifier
- [X] T046 [P] [US1] Frontend test in `frontend/projects/crm-web/src/app/core/guards/`: the guard redirects an unauthenticated visitor and preserves the destination
- [X] T047 [P] [US1] Frontend test in `frontend/projects/crm-web/src/app/features/auth/`: the no-access screen renders with its correlation identifier, and the sign-in screen handles the provider-unavailable state

**Checkpoint**: Sign-in works end to end. This is the MVP - demonstrable to a stakeholder.

---

## Phase 4: User Story 2 - A session lasts a working day, and ending it means ending it (Priority: P1)

**Goal**: Renewal is invisible, and sign-out actually ends access - including at the provider when
the user asks.

**Independent Test**: Work past the access-credential lifetime and confirm no interruption; sign
out and confirm a previously working request is refused.

- [X] T048 [US2] Implement rotation in `backend/src/Crm.Infrastructure/Identity/SessionStore.cs`: spend the presented credential, issue a replacement, and record the chain
- [X] T049 [US2] Implement reuse detection in `backend/src/Crm.Infrastructure/Identity/SessionStore.cs`: a spent credential revokes the whole session and records `credential.reused`
- [X] T050 [US2] Enforce the inactivity limit and the absolute lifetime on renewal in `backend/src/Crm.Application/Identity/`, refusing with `session_expired`
- [X] T051 [US2] Add the sign-out endpoint in `backend/src/Crm.Api/Auth/AuthController.cs`: revoke this session, clear the cookie, and support "all sessions"
- [X] T052 [US2] Return the provider end-session address from sign-out when the caller asked to end access on this computer, in `backend/src/Crm.Api/Auth/AuthController.cs`
- [X] T053 [US2] Revoke live sessions when a user is deactivated, in `backend/src/Crm.Application/Identity/`
- [X] T054 [US2] Implement the credential attach and single-flight renewal in `frontend/projects/core/src/lib/http/interceptors.ts`, replacing the no-op `authTokenInterceptor`: one renewal shared by concurrent requests, one retry, then clear and route to sign-in
- [X] T055 [P] [US2] Add the user menu in `frontend/projects/ui/src/lib/shell/user-menu.component.ts` with sign out, sign out everywhere, and "also end access on this computer", keyboard operable with accessible names
- [X] T056 [US2] Place the user menu in `AppShellComponent` in `frontend/projects/ui/src/lib/shell/app-shell.component.html`, keeping the shell audience-neutral
- [X] T057 [US2] Handle session expiry in `frontend/projects/core/src/lib/auth/auth.service.ts`: inform the user, preserve their destination, and route to sign-in rather than showing a generic failure
- [X] T058 [P] [US2] Integration test in `backend/tests/Crm.IntegrationTests/Auth/`: renewal returns a new credential and rotates the cookie, and the previous credential is now spent
- [X] T059 [P] [US2] Integration test in `backend/tests/Crm.IntegrationTests/Auth/`: presenting a spent credential revokes the session, and a subsequent renewal with the newest credential also fails
- [X] T060 [P] [US2] Integration test in `backend/tests/Crm.IntegrationTests/Auth/`: sign-out revokes immediately - a request that worked a moment ago is refused
- [X] T061 [P] [US2] Integration test in `backend/tests/Crm.IntegrationTests/Auth/`: two sessions are independent, "sign out everywhere" ends both, and deactivating the user ends all of them
- [X] T062 [P] [US2] Integration test in `backend/tests/Crm.IntegrationTests/Auth/`: renewal is refused past the inactivity limit and past the absolute lifetime, with `session_expired`
- [X] T063 [P] [US2] Integration test in `backend/tests/Crm.IntegrationTests/Auth/`: the renewal endpoint refuses a request without the custom header or from a disallowed origin
- [X] T064 [P] [US2] Frontend test in `frontend/projects/core/src/lib/http/`: three concurrent requests meeting an expired credential trigger exactly one renewal and all three then succeed
- [X] T065 [P] [US2] Frontend test in `frontend/projects/core/src/lib/auth/`: a failed renewal clears the session once and routes to sign-in with the destination preserved

**Checkpoint**: Sessions behave correctly under renewal, revocation, and expiry.

---

## Phase 5: User Story 3 - Permissions arrive with the session (Priority: P2)

**Goal**: Sign-in grants real, correct permissions, and changes land within one renewal cycle.

**Independent Test**: Assign a role, sign in, confirm permitted and unpermitted calls behave
correctly; change the assignment and confirm the change lands within a cycle without signing out.

- [X] T066 [US3] Implement effective-permission resolution in `backend/src/Crm.Application/Identity/`: the union of the user's roles, computed at sign-in and at each renewal
- [X] T067 [US3] Emit permissions, population, and placement as claims from `TokenIssuer` in `backend/src/Crm.Infrastructure/Identity/TokenIssuer.cs`, matching `contracts/session-contract.md`
- [X] T068 [US3] Validate seeded role permissions against the catalog at startup in `backend/src/Crm.Api/Configuration/`, reporting an unknown permission rather than ignoring it
- [X] T069 [US3] Implement the default-role grant in `backend/src/Crm.Application/Identity/`: applied only when the user has no assignment, skipped when no default is configured, and audited
- [X] T070 [US3] Implement the bootstrap administrator in `backend/src/Crm.Application/Identity/`: match the configured subject or email, grant the administrator role, and audit it as a bootstrap grant
- [X] T071 [US3] Add `GET /api/v1/auth/me` in `backend/src/Crm.Api/Auth/AuthController.cs`, returning only the caller's own identity, permissions, and placement
- [X] T072 [US3] Show navigation by permission in `frontend/projects/crm-web/src/app/app.ts` and the shell, using `hasPermission` from `@crm/core` - presentation only
- [X] T073 [P] [US3] Unit test in `backend/tests/Crm.UnitTests/Identity/`: effective permissions are the union of roles, with duplicates collapsed and an unknown permission rejected
- [X] T074 [P] [US3] Integration test in `backend/tests/Crm.IntegrationTests/Auth/`: a user with a role reaches the endpoints it permits and is refused by the others
- [X] T075 [P] [US3] Integration test in `backend/tests/Crm.IntegrationTests/Auth/`: a role change is reflected after one renewal, without the user signing out
- [X] T076 [P] [US3] Integration test in `backend/tests/Crm.IntegrationTests/Auth/`: with a default role configured a new user can work; with none configured they receive `no_access`
- [X] T077 [P] [US3] Integration test in `backend/tests/Crm.IntegrationTests/Auth/`: on an empty user table the configured bootstrap administrator signs in with administrative permissions - the lock-out scenario cannot reach production
- [X] T078 [P] [US3] Integration test in `backend/tests/Crm.IntegrationTests/Auth/`: a caller who forges a permission claim in the request payload is refused, and a portal-population credential is refused by these staff-only endpoints
- [X] T079 [P] [US3] Frontend test in `frontend/projects/crm-web/src/app/`: navigation shows only permitted destinations, and hiding an item never substitutes for the backend refusing

**Checkpoint**: The CRM is usable by a real team with real roles.

---

## Phase 6: User Story 4 - Authentication is accountable and abuse is throttled (Priority: P3)

**Goal**: Every authentication decision is recorded, and the anonymous endpoints cannot be hammered.

**Independent Test**: Complete sign-in and sign-out, confirm both appear in the audit trail with the
correlation identifier and no secret; exceed the limit on an anonymous endpoint and confirm
throttling with an unrelated caller unaffected.

- [X] T080 [US4] Add the reusable rate-limiting capability in `backend/src/Crm.Api/Configuration/RateLimitingSetup.cs`: named policies from configuration, partitioned per source, applied by attribute so any later feature can annotate an endpoint
- [X] T081 [US4] Apply the policies to sign-in, callback, and renewal in `backend/src/Crm.Api/Auth/AuthController.cs`
- [X] T082 [US4] Map a rejected request to the shared error contract with `rate_limited` and a `Retry-After` header, in `backend/src/Crm.Api/Configuration/RateLimitingSetup.cs`
- [X] T083 [US4] Persist authentication events in `backend/src/Crm.Infrastructure/Identity/AuthenticationEventRecorder.cs`, alongside the existing `IAuditRecorder` call, so a security question is answerable without reading log files
- [X] T084 [US4] Record every event named in data-model.md from the sign-in, renewal, and sign-out use cases in `backend/src/Crm.Application/Identity/`
- [X] T085 [P] [US4] Integration test in `backend/tests/Crm.IntegrationTests/Auth/`: exceeding the limit returns `rate_limited` with `Retry-After`, and a different source is unaffected
- [X] T086 [P] [US4] Integration test in `backend/tests/Crm.IntegrationTests/Auth/`: a sign-in, a renewal, a refusal, and a sign-out each produce an event with actor, outcome, and correlation identifier
- [X] T087 [P] [US4] Integration test in `backend/tests/Crm.IntegrationTests/Auth/`: no authentication event, audit record, or log entry from a full sign-in cycle contains a token, cookie value, or credential hash
- [X] T088 [P] [US4] Update `docs/testing.md` with how to sign in inside a test, and how the fake provider stands in for a real one

**Checkpoint**: The feature is accountable and defensible.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [X] T089 [P] Add the five endpoints to `specs/002-auth-login/contracts/auth-api.yaml` verification by running the existing OpenAPI drift test, and fix whichever side is wrong
- [X] T090 [P] Document the same-domain deployment constraint in `docs/production-configuration.md`: the SPA and API must share a registrable domain or the renewal cookie becomes cross-site
- [X] T091 [P] Document the provider setup, required settings, and bootstrap administrator in `docs/production-configuration.md` and `docs/getting-started.md`
- [X] T092 [P] Add an authentication section to `docs/conventions.md`: how to declare permissions on a new endpoint, and why a frontend permission check is never a security boundary
- [X] T093 [P] Accessibility pass over `frontend/projects/ui/src/lib/shell/user-menu.component.ts` and the sign-in screens: keyboard operation, focus order, accessible names, and contrast in both directions
- [X] T094 RTL pass over the sign-in, completion, and no-access screens; confirm `npm run i18n:check` and `npm run css:check` both pass
- [X] T095 Run the full gates - `./scripts/verify-backend.ps1` and `./scripts/verify-frontend.ps1` - and record the new elapsed times in `docs/testing.md` against the ten-minute budget
- [ ] T096 Execute `specs/002-auth-login/quickstart.md` against a real provider container, including every row of its "easy to get wrong" table
- [X] T097 Re-verify the Constitution Check table in `specs/002-auth-login/plan.md` against the delivered code and record the result in `specs/002-auth-login/compliance.md`
- [X] T098 Confirm each item of Constitution section 17 (Definition of Done) and tick `specs/002-auth-login/checklists/requirements.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup. **Blocks every user story.**
- **US1 (Phase 3)**: Depends on Foundational. The MVP.
- **US2 (Phase 4)**: Depends on US1 - there is no session to renew or revoke until sign-in exists.
- **US3 (Phase 5)**: Depends on US1. Independent of US2.
- **US4 (Phase 6)**: Depends on US1. Independent of US2 and US3.
- **Polish (Phase 7)**: Depends on all stories.

Note that US2, US3, and US4 are mutually independent but all follow US1. This differs from feature
001, where four stories could run in parallel after the foundation: here the first story creates the
thing the others operate on.

### Notable within-phase dependencies

- T009-T012 → T013 → T014 (entities, then configuration, then migration).
- T015, T016 → T017, T018, T019 (abstractions before implementations).
- T017 → T021 (the issuer must exist before the scheme is pointed at its output).
- T022 → T024 → every US1 test (fake provider, then sign-in helper, then tests).
- T023 must land with T021: the moment the `Staff` scheme validates CRM-issued credentials, feature
  001's test tokens stop matching, and its whole integration suite fails until the helper is
  updated. These two are effectively one change.
- T032, T033 → T054 (the session endpoint and its cookie must exist before the interceptor can renew).
- T048 → T049 (rotation before reuse detection).
- T066 → T067 (permissions resolved before they can be emitted as claims).
- T080 → T081, T082 (the capability before its application).

### Parallel Opportunities

- Setup: T005, T006, T007, T008 are independent files.
- Foundational: T009-T012 are four separate entity files; T015 and T016 are separate abstractions.
- US1: T040-T047 are eight independent test files - the largest parallel block.
- US2: T058-T065 are eight independent test files.
- US3: T073-T079 are seven independent test files.
- US4: T085-T088 are four independent files.
- Polish: T089-T093 are independent documents and passes.
- Across stories: after US1 is green, US2, US3, and US4 can be taken by three developers with no
  file conflicts - they touch different use cases, different endpoints, and different tests.

---

## Implementation Strategy

### MVP first (User Story 1)

Phase 1 → Phase 2 → Phase 3, then **stop and validate**: sign in through the fake provider, confirm
the identity resolves, and confirm an endpoint that returned 401 now succeeds. That is the moment
the CRM stops being a foundation and starts being an application people log into.

### Incremental delivery

1. Setup + Foundational → schema, tokens, sessions, test harness
2. US1 → sign-in works (MVP)
3. US2 → sessions behave under renewal and revocation
4. US3 → real permissions, usable by a team
5. US4 → accountable and throttled
6. Polish → documentation, accessibility, gates, constitutional sign-off

### Parallel team strategy

US1 is the critical path and cannot be parallelized meaningfully - it is one flow. Once it is green,
US2 (sessions), US3 (permissions), and US4 (audit and limits) are genuinely independent.

---

## Notes

- Tests are mandatory, and here they carry unusual weight: this feature decides who can reach what.
  An untested authorization rule is an assumption, and the failure mode is silent.
- **T023 is the riskiest task in the list.** Changing what the `Staff` scheme validates breaks every
  integration test feature 001 wrote until the token helper is updated. Expect a red suite between
  T021 and T023, and do not treat it as a regression.
- Two deliberate non-tests carried from feature 001 still apply: the language-switch timing target
  is observed rather than asserted, and test isolation is guaranteed by construction.
- Commit after each task or logical group; the story checkpoints are the natural review points.

---

## Completion note (2026-08-27)

97 of 98 tasks are done. Both gates are green: 145 backend tests (47 unit, 93 integration, 5
architecture) and 66 frontend tests.

**T096 is not done.** Executing `quickstart.md` against a real provider container needs a Keycloak
instance and a browser session, neither of which is part of the automated gate. The fake provider
exercises the real `OpenIdConnectClient` against real discovery, code exchange, and identity-token
validation, so what remains unverified is the provider-specific part - claim naming and the exact
redirect-URI match. Both are configuration, both are in the quickstart's "easy to get wrong" table,
and neither is exercised by a code path the tests skip. Recorded in `compliance.md` rather than
quietly ticked.

**Two tasks were completed differently from their wording, deliberately:**

- **T050** asks for the inactivity and absolute limits to be enforced "in
  `backend/src/Crm.Application/Identity/`". They are enforced in `Session.IsActive` on the Domain
  entity, which the store consults. The rule is further from the controller, not closer, and an
  invariant about a session's own lifetime belongs to the session.
- **T053** asks for live sessions to be revoked when a user is deactivated. `DeactivateUser` in the
  Application layer does exactly that, but nothing calls it yet: this feature ships no
  user-management endpoint. It exists now so that whatever adds one cannot get the ordering wrong,
  and it is covered by tests that invoke it directly.

**Several tasks were already implemented when this work resumed** - T048, T049, T066, T067, T069,
T070 among them. They were verified against the delivered code before being ticked, not
re-implemented.
