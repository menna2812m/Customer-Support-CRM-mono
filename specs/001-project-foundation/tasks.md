---

description: "Task list for Project Foundation (001-project-foundation)"
---

# Tasks: Project Foundation

**Input**: Design documents from `/specs/001-project-foundation/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Required. Constitution Principle XIII overrides the usual "tests are optional" default:
business rules, authorization rules, and validation failures MUST have tests, and critical Angular
workflows MUST have frontend tests. Test tasks are therefore listed inside each story phase, not as
an optional add-on.

**Organization**: Tasks are grouped by user story so each story can be implemented and verified
independently. Story priorities come from spec.md.

**Revision 2026-08-26**: renumbered after the `/speckit.analyze` remediation pass. Seven tasks were
added (T052, T081, T113, T123, T124, T125, T131) closing coverage gaps in production packaging,
auditing, OpenAPI exposure, transport security, and the SC-002 measurement; eleven existing tasks
were tightened. Numbering is continuous, so IDs from the previous revision are not comparable.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1-US5, mapping to the spec's user stories
- Every task names the file or directory it touches

## Path Conventions

- **Backend**: `backend/src/Crm.Api/`, `Crm.Application/`, `Crm.Domain/`, `Crm.Infrastructure/`
- **Backend tests**: `backend/tests/Crm.UnitTests/`, `Crm.IntegrationTests/`, `Crm.ArchitectureTests/`
- **Frontend**: `frontend/projects/crm-web/`, `frontend/projects/core/` (`@crm/core`),
  `frontend/projects/ui/` (`@crm/ui`)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Repository, solution, workspace, and tooling exist and build empty.

- [ ] T001 [P] Add `.gitignore` (bin, obj, node_modules, dist, logs, `*.user`), `.gitattributes`, and `.editorconfig` with shared formatting and analyzer severities at repository root
- [ ] T002 [P] Add `global.json` at repository root pinning SDK `10.0.400` with `rollForward: latestFeature`
- [ ] T003 Create `backend/Crm.sln` with `Crm.Api` (webapi), `Crm.Application`, `Crm.Domain`, `Crm.Infrastructure` (classlib) and wire references: Api→Application+Infrastructure, Application→Domain, Infrastructure→Application
- [ ] T004 [P] Add `backend/Directory.Build.props` (nullable enable, `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`, latest analysis level) and `backend/Directory.Packages.props` (central package management)
- [ ] T005 Create `backend/tests/Crm.UnitTests`, `Crm.IntegrationTests`, `Crm.ArchitectureTests` (xUnit v3), add project references and add all to `Crm.sln`
- [ ] T006 [P] Register Shouldly, NSubstitute, Testcontainers.MsSql, and ArchUnitNET versions in `backend/Directory.Packages.props` and reference them from the matching test projects
- [ ] T007 Create the Angular workspace at `frontend/` with a single standalone application `crm-web` (SCSS, no SSR, routing enabled)
- [ ] T008 Generate workspace libraries `core` (`@crm/core`) and `ui` (`@crm/ui`) under `frontend/projects/`, with `public-api.ts` surfaces and tsconfig path mappings
- [ ] T009 [P] Install Angular Material and CDK and create the single theme definition in `frontend/projects/ui/src/lib/theme/`
- [ ] T010 [P] Configure ESLint (angular-eslint) and Prettier in `frontend/eslint.config.js` and `frontend/.prettierrc`
- [ ] T011 Verify the `@angular/build:unit-test` builder with the Vitest runner is available in CLI 22.1.6 and configure it in `frontend/angular.json`; if unavailable, configure Karma + Jasmine instead and record the outcome in `specs/001-project-foundation/research.md` open items
- [ ] T012 [P] Create argument-free `scripts/verify-backend.ps1` and `scripts/verify-frontend.ps1` stubs that exit non-zero on any failure
- [ ] T013 [P] Create `docs/` skeleton: `getting-started.md`, `conventions.md`, `production-configuration.md`, `testing.md`
- [ ] T014 [P] Add `frontend/.nvmrc` (22), an `engines` field, and npm scripts `start`, `build`, `test`, `lint`, `format:check` in `frontend/package.json`

**Checkpoint**: Both stacks build and their empty test suites run.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Cross-cutting platform every user story depends on: configuration, persistence, the
error contract, correlation, versioning, and the frontend core.

**⚠️ CRITICAL**: No user story phase can begin until this phase completes.

- [ ] T015 Add `appsettings.json` and `appsettings.Development.json` in `backend/src/Crm.Api/` with non-secret defaults (logging, CORS allowlist, database options, auth switches)
- [ ] T016 Add strongly-typed options with validation (`DatabaseOptions`, `CorsOptions`, `AuthOptions`, `LoggingOptions`) plus `ValidateOnStart` in `backend/src/Crm.Api/Configuration/`
- [ ] T017 Add the `ISecretsSource` seam with a DPAPI-protected-file default implementation in `backend/src/Crm.Infrastructure/Configuration/` and register it as a configuration source
- [ ] T018 Implement fail-fast startup in `backend/src/Crm.Api/Program.cs` that aggregates every missing or invalid required setting into one message naming each setting
- [ ] T019 [P] Add `Entity<TId>`, `IAuditableEntity`, `ISoftDeletable`, `IHasOrganizationScope` in `backend/src/Crm.Domain/Common/`
- [ ] T020 [P] Register `TimeProvider.System` and expose it to Application handlers via DI in `backend/src/Crm.Api/Program.cs`
- [ ] T021 Create `CrmDbContext` in `backend/src/Crm.Infrastructure/Persistence/` applying the conventions from data-model.md (naming, bounded strings, decimal precision, `DateTimeOffset`, `DeleteBehavior.Restrict`, configuration assembly scan)
- [ ] T022 Add `AuditingSaveChangesInterceptor` and the soft-delete global query filter in `backend/src/Crm.Infrastructure/Persistence/Interceptors/`
- [ ] T023 Register EF Core SQL Server in `backend/src/Crm.Api/Program.cs` using `DatabaseOptions`, with retry-on-failure and an explicit command timeout
- [ ] T024 Create the empty baseline migration in `backend/src/Crm.Infrastructure/Persistence/Migrations/` and add configuration-gated startup migration that is off outside Development
- [ ] T025 Add correlation middleware in `backend/src/Crm.Api/Common/Correlation/` that reuses an inbound `X-Correlation-Id`, otherwise uses the current `Activity` trace id, pushes it to `LogContext`, and writes it to the response header
- [ ] T026 [P] Add `ErrorCodes` constants and problem `type` URIs in `backend/src/Crm.Application/Common/`
- [ ] T027 Implement the global `IExceptionHandler` plus `AddProblemDetails` customization in `backend/src/Crm.Api/Common/Errors/`, emitting `code`, `correlationId`, and `errors[]` exactly as `contracts/error-contract.md` specifies
- [ ] T028 Configure API versioning (Asp.Versioning) with URL segment reader, default 1.0, and ApiExplorer in `backend/src/Crm.Api/Configuration/`, mapping unsupported versions to the error contract
- [ ] T029 [P] Add `PageRequest` and `PagedResult<T>` in `backend/src/Crm.Application/Common/` per `contracts/pagination-contract.md` (default page size 25, maximum 100)
- [ ] T030 Build the integration test fixture in `backend/tests/Crm.IntegrationTests/Infrastructure/` that starts one `Testcontainers.MsSql` container per run, creates a uniquely named database, applies migrations, disposes at the end, and fails with a message naming Docker when the runtime is unavailable
- [ ] T031 Add `CrmWebApplicationFactory` in `backend/tests/Crm.IntegrationTests/Infrastructure/` overriding configuration to target the container and supporting test-issued tokens for both schemes and a configurable hosting environment name
- [ ] T032 [P] Add the ArchUnitNET assembly-loading base class in `backend/tests/Crm.ArchitectureTests/`
- [ ] T033 Implement `AppConfig` token, `assets/config.json` loading during app initialization, and `provideCrmCore()` in `frontend/projects/core/src/lib/config/`
- [ ] T034 [P] Add `frontend/projects/crm-web/public/assets/config.json` and local development defaults
- [ ] T035 [P] Add `AppError` and `RequestState<T>` types with signal helpers in `frontend/projects/core/src/lib/state/`
- [ ] T036 Implement the functional interceptor chain in `frontend/projects/core/src/lib/http/` in fixed order: base URL, correlation id, auth token stub, error normalization to `AppError`
- [ ] T037 Add the global `ErrorHandler` in `frontend/projects/core/src/lib/errors/` that reports anything escaping a feature
- [ ] T038 Add `AppShellComponent` (audience-neutral navigation and content outlet) in `frontend/projects/ui/src/lib/shell/`, and in `frontend/projects/crm-web/src/app/app.routes.ts` add lazy feature routing plus the named route-guard extension point (`authGuard` placeholder returning true, wired into the route definitions so the authentication feature only replaces its body)
- [ ] T039 [P] Add the six state components and `StateContainerComponent` in `frontend/projects/ui/src/lib/states/`, built to FR-057 from the start: every interactive element keyboard-operable with a visible focus indicator and an accessible name, and loading and error state changes announced to assistive technology (T134 verifies, it does not retrofit)
- [ ] T040 [P] Add shared frontend test providers and harness helpers in `frontend/projects/core/src/testing/`
- [ ] T041 Add ESLint boundary rules in `frontend/eslint.config.js` forbidding cross-feature imports and deep library imports
- [ ] T042 Add the ESLint rule in `frontend/eslint.config.js` restricting `HttpClient` injection to files matching `*-api.service.ts`

**Checkpoint**: Platform ready. User stories can now proceed; US2-US5 may run in parallel after US1.

---

## Phase 3: User Story 1 - Run the whole platform locally (Priority: P1) 🎯 MVP

**Goal**: A developer clones, follows the documentation, and gets API, frontend, and database
working together, with health reflecting reality.

**Independent Test**: On a clean machine with only the documented prerequisites, follow
`docs/getting-started.md`; the API starts, migrations apply to an empty database, the frontend
loads and reaches the API, and `/health/ready` reports the database status correctly.

- [ ] T043 [US1] Register health checks and map anonymous `/health/live` and `/health/ready` (SQL Server check, short timeout, result cached for **5 seconds** so SC-006's 30-second window always holds) in `backend/src/Crm.Api/Diagnostics/HealthEndpoints.cs`
- [ ] T044 [US1] Implement the minimal health response writer in `backend/src/Crm.Api/Diagnostics/` emitting only status, check name, and duration - never exception text, server names, or connection strings
- [ ] T045 [P] [US1] Add the development frontend origin to the CORS allowlist in `backend/src/Crm.Api/appsettings.Development.json` and apply the named policy in `backend/src/Crm.Api/Configuration/CorsSetup.cs` (development entry only - per-environment enforcement and wildcard rejection arrive in T108)
- [ ] T046 [US1] Bootstrap the application with `provideCrmCore()` and `AppShellComponent`, and add the home page showing API reachability in `frontend/projects/crm-web/src/app/features/home/`
- [ ] T047 [P] [US1] Add `health-api.service.ts` in `frontend/projects/crm-web/src/app/features/home/` as the only HTTP caller for the home page
- [ ] T048 [P] [US1] Integration test in `backend/tests/Crm.IntegrationTests/Health/` asserting `/health/live` and `/health/ready` return Healthy against the container database
- [ ] T049 [P] [US1] Integration test in `backend/tests/Crm.IntegrationTests/Health/` asserting an unreachable database yields an unhealthy readiness response containing no connection details or exception text
- [ ] T050 [P] [US1] Unit test in `backend/tests/Crm.UnitTests/Configuration/` asserting options validation fails fast and names every missing required setting, and asserting the startup auto-migration switch resolves to disabled for every non-Development environment
- [ ] T051 [P] [US1] Integration test in `backend/tests/Crm.IntegrationTests/Persistence/` asserting the baseline migration applies to an empty database, creates no business tables, and can be applied a second time without error
- [ ] T052 [P] [US1] Integration test in `backend/tests/Crm.IntegrationTests/Persistence/` using a test-only auditable, soft-deletable entity: insert stamps `CreatedAt`/`CreatedBy` and leaves the update fields null, update stamps `UpdatedAt`/`UpdatedBy` without altering the created fields, and a soft delete removes the row from default queries while leaving it in the table
- [ ] T053 [P] [US1] Frontend test in `frontend/projects/crm-web/src/app/features/home/` asserting the home page renders the healthy state and the error state from the service
- [ ] T054 [US1] Write `docs/getting-started.md` from `specs/001-project-foundation/quickstart.md` and confirm each step by executing it
- [ ] T055 [US1] Verify the frontend reaches the API with no source edits (CORS or dev proxy), and record the resolved approach in `docs/getting-started.md`

**Checkpoint**: The platform runs end to end. This is the MVP - demonstrable on its own.

---

## Phase 4: User Story 2 - Add a new capability using established conventions (Priority: P2)

**Goal**: Every convention a future vertical feature needs exists, is enforced, and is demonstrated
by a removable reference slice.

**Independent Test**: Following only `docs/conventions.md` and the diagnostics slice, add a small
non-business endpoint plus screen; it inherits versioned routing, validation, the error contract,
pagination, permission enforcement, and the migration workflow with at most one shared registration
point touched per side.

- [ ] T056 [P] [US2] Add the `Permissions` catalog (grouped `const string` members, including the constitution's example names and `diagnostics.read`) with a reflection-based registry in `backend/src/Crm.Application/Authorization/Permissions.cs`
- [ ] T057 [P] [US2] Add `CallerPopulation`, `OrganizationScope`, and `ICurrentUser` in `backend/src/Crm.Application/Abstractions/`
- [ ] T058 [US2] Implement `CurrentUser` over `IHttpContextAccessor` claims in `backend/src/Crm.Api/Common/Security/`, resolving population from the authenticating scheme and never from a client-supplied claim
- [ ] T059 [US2] Register the `Staff` and `Portal` JWT bearer schemes with an issuer-based policy scheme selector, configuration-driven and disabled by default, in `backend/src/Crm.Api/Configuration/AuthenticationSetup.cs`
- [ ] T060 [US2] Implement `RequirePermissionAttribute`, `PermissionRequirement`, its handler, and the dynamic `IAuthorizationPolicyProvider` in `backend/src/Crm.Api/Common/Security/`
- [ ] T061 [US2] Implement the population admission attribute and handler in `backend/src/Crm.Api/Common/Security/`
- [ ] T062 [US2] Configure the fallback authorization policy (authenticated by default) in `backend/src/Crm.Api/Program.cs` and mark only health and the development OpenAPI endpoints `[AllowAnonymous]`
- [ ] T063 [P] [US2] Add `IAuditRecorder` and `AuditEntry` in `backend/src/Crm.Application/Abstractions/`
- [ ] T064 [US2] Register FluentValidation by assembly scan and add the validation filter emitting the error contract in `backend/src/Crm.Api/Common/Validation/`
- [ ] T065 [US2] Configure JSON options (camelCase, `MaxDepth` 32, enum handling, no reference loops) in `backend/src/Crm.Api/Configuration/`
- [ ] T066 [US2] Add the built-in OpenAPI document plus Scalar UI, registered only in Development, with both security schemes described, in `backend/src/Crm.Api/Configuration/OpenApiSetup.cs`
- [ ] T067 [P] [US2] Add `EchoRequest`/`EchoResponse` DTOs and the FluentValidation validator in `backend/src/Crm.Application/Diagnostics/`
- [ ] T068 [P] [US2] Add the `DiagnosticItem` DTO and the paged query handler returning `PagedResult<T>` over a generated in-memory sequence in `backend/src/Crm.Application/Diagnostics/`
- [ ] T069 [US2] Add `DiagnosticsController` in `backend/src/Crm.Api/Diagnostics/` with the versioned route, `[RequirePermission(Permissions.Diagnostics.Read)]`, and declared admitted populations, matching `contracts/foundation-api.yaml`
- [ ] T070 [P] [US2] Add `diagnostics-api.service.ts` in `frontend/projects/crm-web/src/app/features/diagnostics/`
- [ ] T071 [US2] Add the diagnostics page in `frontend/projects/crm-web/src/app/features/diagnostics/` using `StateContainerComponent` and paging controls bound to the pagination contract
- [ ] T072 [US2] Add `applyServerErrors(form, error)` in `frontend/projects/core/src/lib/forms/` and use it in the diagnostics echo typed reactive form
- [ ] T073 [P] [US2] Unit tests in `backend/tests/Crm.UnitTests/Diagnostics/` for the paging math and validator rules (business-rule test example)
- [ ] T074 [P] [US2] Integration test in `backend/tests/Crm.IntegrationTests/Authorization/`: anonymous returns 401, authenticated without permission returns 403, with permission returns 200 - all in the error contract - and a caller forbidden on an existing resource receives a response body indistinguishable from the one returned for a resource that does not exist
- [ ] T075 [P] [US2] Integration test in `backend/tests/Crm.IntegrationTests/Authorization/`: a Portal token is refused on a Staff-only endpoint even when it carries the same permission name
- [ ] T076 [P] [US2] Integration test in `backend/tests/Crm.IntegrationTests/Validation/`: invalid payload returns 400 with one `errors[]` entry per field and stable codes
- [ ] T077 [P] [US2] Integration test in `backend/tests/Crm.IntegrationTests/Diagnostics/`: default paging, explicit paging, out-of-range page, `pageSize` above maximum, descending sort, unsortable field, unknown filter parameter
- [ ] T078 [P] [US2] Integration test in `backend/tests/Crm.IntegrationTests/Versioning/`: an unknown API version returns the error contract with `unsupported_api_version`
- [ ] T079 [P] [US2] Integration test in `backend/tests/Crm.IntegrationTests/Errors/`: a deliberately thrown exception returns a generic 500 with `correlationId` and no stack trace, exception type, SQL, or connection string in the body
- [ ] T080 [P] [US2] Integration test in `backend/tests/Crm.IntegrationTests/Contracts/`: the live OpenAPI document's paths, status codes, and security requirements match `specs/001-project-foundation/contracts/foundation-api.yaml`
- [ ] T081 [P] [US2] Integration test in `backend/tests/Crm.IntegrationTests/Contracts/`: with the factory hosting environment set to Production, both the OpenAPI document path and the Scalar UI path return 404 to an anonymous caller (AR-002)
- [ ] T082 [P] [US2] Architecture test in `backend/tests/Crm.ArchitectureTests/`: `Crm.Domain` has no outbound project dependencies and `Crm.Application` does not depend on `Crm.Infrastructure`
- [ ] T083 [P] [US2] Architecture test in `backend/tests/Crm.ArchitectureTests/`: controllers contain no business logic - no `DbContext` usage, no direct persistence access
- [ ] T084 [P] [US2] Architecture test in `backend/tests/Crm.ArchitectureTests/`: no EF entity type appears in a controller signature, and vendor SDK packages are referenced only by `Crm.Infrastructure`
- [ ] T085 [P] [US2] Frontend test in `frontend/projects/crm-web/src/app/features/diagnostics/` asserting the data-access service maps each API failure to the correct `AppError.kind`
- [ ] T086 [P] [US2] Frontend test in `frontend/projects/crm-web/src/app/features/diagnostics/` asserting the page renders each of the six mandated states

**Checkpoint**: A new vertical feature can be built by copying the diagnostics slice.

---

## Phase 5: User Story 3 - Use the application in Arabic or English (Priority: P2)

**Goal**: Language and direction are first-class and switchable in place, with no hard-coded
strings and no per-screen RTL exceptions.

**Independent Test**: Open the shell, switch language; every visible string changes, the layout
mirrors, formatting follows the locale, and the selection survives a reload.

- [ ] T087 [US3] Configure Transloco in `frontend/projects/core/src/lib/i18n/` with `ar`/`en` loaders, documented fallback, and a missing-key handler that reports to developers
- [ ] T088 [P] [US3] Add `ar.json` and `en.json` in `frontend/projects/crm-web/public/i18n/` covering shell, six states, error codes, home, and diagnostics
- [ ] T089 [US3] Implement `LanguageService` in `frontend/projects/core/src/lib/i18n/` exposing `language()`/`direction()` signals, persisting the choice, and restoring it at bootstrap
- [ ] T090 [US3] Wire direction in `frontend/projects/core/src/lib/i18n/`: set `dir` and `lang` on the document element and provide the CDK `Directionality` value atomically with the language change
- [ ] T091 [P] [US3] Add the language switcher component in `frontend/projects/ui/src/lib/shell/` and place it in `AppShellComponent`, meeting FR-057 as built: keyboard-operable, an accessible name that does not rely on the flag or glyph alone, a visible focus indicator, and the active language exposed as state rather than by styling alone
- [ ] T092 [US3] Replace every hard-coded user-visible string in the shell, state components, home, and diagnostics with translation keys
- [ ] T093 [US3] Add the error-code to translated-message mapping in `frontend/projects/core/src/lib/errors/` so no server-supplied text is ever rendered
- [ ] T094 [P] [US3] Add locale-aware date and number formatting helpers in `frontend/projects/core/src/lib/i18n/` and use them in the diagnostics list
- [ ] T095 [P] [US3] Add the lint rule in `frontend/eslint.config.js` (or stylelint config) forbidding physical direction properties (`margin-left`, `padding-right`, `left:`, `right:`, `text-align: left|right`) in component styles
- [ ] T096 [P] [US3] Add the translation key-parity check script in `scripts/` and call it from `scripts/verify-frontend.ps1`
- [ ] T097 [P] [US3] Frontend test in `frontend/projects/core/src/lib/i18n/` asserting a language switch changes strings and direction together and persists across a reload
- [ ] T098 [P] [US3] Frontend test in `frontend/projects/core/src/lib/i18n/` asserting a missing key renders the documented fallback and is reported
- [ ] T099 [US3] Run an RTL pass over shell, home, and diagnostics; fix any physical-direction violation the lint rule surfaces, and confirm by observation that a language switch is visibly complete within a second (SC-004 design target - no timing assertion)

**Checkpoint**: The empty shell is genuinely bilingual and bidirectional.

---

## Phase 6: User Story 4 - Operate and diagnose the running system (Priority: P3)

**Goal**: A failure can be traced end to end from the identifier the user saw, logs contain no
secrets, and the edge is hardened.

**Independent Test**: Trigger a deliberate failure from the frontend, take the correlation id shown,
and retrieve the complete server-side trail from the log files.

- [ ] T100 [US4] Configure Serilog in `backend/src/Crm.Api/Configuration/LoggingSetup.cs`: compact JSON rolling file sink with configurable rotation and retention, plus a readable console sink in Development
- [ ] T101 [US4] Add request log enrichment (correlation id, request path, user id, population) in `backend/src/Crm.Api/Common/Correlation/`
- [ ] T102 [P] [US4] Add the destructuring and redaction policy for sensitive members (`password`, `token`, `secret`, `authorization`, `connectionString`) in `backend/src/Crm.Api/Configuration/LoggingSetup.cs`
- [ ] T103 [P] [US4] Make log verbosity configurable per environment through `appsettings.{Environment}.json` with no code change
- [ ] T104 [US4] Implement the logging `IAuditRecorder` in `backend/src/Crm.Infrastructure/Auditing/` writing structured audit entries with the correlation id
- [ ] T105 [US4] Log authentication and authorization failures with the correlation id and attempted operation - never the submitted credentials - in `backend/src/Crm.Api/Common/Security/`
- [ ] T106 [US4] Add HTTPS redirection and HSTS outside Development in `backend/src/Crm.Api/Program.cs`
- [ ] T107 [P] [US4] Add the security-header middleware in `backend/src/Crm.Api/Common/Security/SecurityHeadersMiddleware.cs` emitting exactly the FR-053 baseline set: `Content-Security-Policy`, `X-Content-Type-Options`, `Referrer-Policy`, `X-Frame-Options` with CSP `frame-ancestors`, and `Permissions-Policy`
- [ ] T108 [US4] Generalize the CORS policy in `backend/src/Crm.Api/Configuration/CorsSetup.cs` to read a per-environment allowlist from configuration, and fail startup when a wildcard origin is configured in any environment (extends the development-only entry from T045)
- [ ] T109 [P] [US4] Enforce the remaining request limits centrally: request body at most 10 MB (Kestrel and IIS) and a shared validator rule capping any request collection at 500 items, each producing the error contract rather than a framework failure. JSON nesting depth is owned by T065 - do not set `MaxDepth` again here; instead confirm the configured value is 32 and that exceeding it surfaces as `malformed_request` rather than an unhandled failure
- [ ] T110 [P] [US4] Integration test in `backend/tests/Crm.IntegrationTests/Correlation/`: an inbound correlation id is reused, an absent one is generated, and both appear in the response header and in error bodies
- [ ] T111 [P] [US4] Integration test in `backend/tests/Crm.IntegrationTests/Logging/`: a full request cycle carrying credentials produces log entries with those values redacted
- [ ] T112 [P] [US4] Integration test in `backend/tests/Crm.IntegrationTests/Security/`: every response carries the baseline security headers
- [ ] T113 [P] [US4] Integration test in `backend/tests/Crm.IntegrationTests/Security/`: outside Development an insecure request is redirected or rejected and responses carry `Strict-Transport-Security`, while Development is unaffected (FR-052)
- [ ] T114 [P] [US4] Integration test in `backend/tests/Crm.IntegrationTests/Security/`: an origin outside the allowlist is refused, and a wildcard configuration fails startup
- [ ] T115 [P] [US4] Integration test in `backend/tests/Crm.IntegrationTests/Security/`: oversized bodies, over-deep nesting, and over-length collections each return 400 in the error contract
- [ ] T116 [US4] Verify log rolling and retention against the configured policy and document the defaults in `docs/production-configuration.md`

**Checkpoint**: The system is diagnosable and hardened at the edge.

---

## Phase 7: User Story 5 - Verify quality with a single command (Priority: P3)

**Goal**: One documented command per stack builds, tests, lints, and format-checks, and fails
loudly on any violation - and produces the deployable artifacts.

**Independent Test**: From a clean checkout run both scripts and see success; introduce a
deliberate lint, format, or test violation and see a failure naming the offending file.

- [ ] T117 [US5] Complete `scripts/verify-backend.ps1`: restore, build with warnings as errors, unit + integration + architecture tests, `dotnet format --verify-no-changes`
- [ ] T118 [US5] Complete `scripts/verify-frontend.ps1`: clean install, lint, unit tests, Prettier check, translation key parity
- [ ] T119 [P] [US5] Configure test reporters and coverage thresholds for both stacks in `frontend/angular.json` and `backend/tests/*/`, recording in `docs/testing.md` that thresholds are a team convention rather than a product requirement
- [ ] T120 [P] [US5] Negative verification: deliberately break a lint rule, a format rule, and a test, confirm each produces a non-zero exit naming the file, then revert and record the procedure in `docs/testing.md`
- [ ] T121 [P] [US5] Write `docs/conventions.md`: layer rules, permission declaration, error codes, pagination usage, i18n workflow, how to add a vertical feature, and an explicit note that rate limiting is deferred to the authentication feature and must not be reinvented locally (FR-056)
- [ ] T122 [P] [US5] Write `docs/production-configuration.md`: IIS deployment layout, the full required-settings list marking secrets, the `ISecretsSource` seam, and log retention
- [ ] T123 [US5] Add `appsettings.Production.json` in `backend/src/Crm.Api/` containing non-secret production defaults only, with every secret-bearing key absent and resolved through the secrets source, and assert in `backend/tests/Crm.UnitTests/Configuration/` that it contains no secret-bearing key
- [ ] T124 [US5] Add the IIS deployment inputs: `frontend/deploy/web.config` with rewrite rules serving `index.html` for any non-file path while leaving `/api` untouched, copied into the frontend build output, and an IIS-targeted publish configuration for `backend/src/Crm.Api/`
- [ ] T125 [US5] Extend `scripts/verify-backend.ps1` and `scripts/verify-frontend.ps1` to produce and validate the deployment artifacts: `dotnet publish` output contains the entry assembly and `appsettings.Production.json` and no `.pdb`-only surprises, and the frontend build output contains `index.html`, hashed assets, `assets/config.json`, and `web.config`
- [ ] T126 [P] [US5] Write `docs/testing.md`: how to run each suite, the Docker requirement, how to add each mandated test kind, the coverage-threshold convention, and the concurrency guarantee that each run uses a uniquely named database so parallel runs on one machine cannot interfere
- [ ] T127 [US5] Confirm both verification scripts take no arguments and are callable unchanged by a future pipeline; document invocation in `README.md`
- [ ] T128 [P] [US5] Write the root `README.md` linking the constitution, docs, and the feature specs directory
- [ ] T129 [US5] Measure a full verification run from a clean checkout and record the elapsed time against the 10-minute budget in `docs/testing.md`

**Checkpoint**: Constitutional rules are executable gates, and the system is packageable for IIS.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [ ] T130 [P] Removability check: on a scratch branch delete `backend/src/Crm.Api/Diagnostics/`, `backend/src/Crm.Application/Diagnostics/`, and `frontend/projects/crm-web/src/app/features/diagnostics/`, confirm build and tests still pass, then document the procedure in `docs/conventions.md`
- [ ] T131 Measure SC-002: following only `docs/conventions.md`, add a throwaway endpoint plus screen on a scratch branch, count the shared files touched per side, confirm it is at most one registration point each, and record the count and the files in `docs/conventions.md`
- [ ] T132 [P] Update the open-items table in `specs/001-project-foundation/research.md` with actual outcomes (test builder, retention numbers, secret store)
- [ ] T133 [P] Update the Commands section of `CLAUDE.md` with the real, verified commands
- [ ] T134 [P] Accessibility pass over `frontend/projects/ui/src/lib/` shell, language switcher, and state components against FR-057: keyboard reachability and activation, sensible focus order, visible focus indicator, accessible names, announcement of loading and error state changes, and WCAG 2.1 AA contrast in both themes and both directions
- [ ] T135 Execute `specs/001-project-foundation/quickstart.md` verbatim on a clean clone and fix any gap found (SC-001)
- [ ] T136 [P] Scan the repository and log output for committed or logged secrets; confirm none exist (SC-007)
- [ ] T137 Re-verify every row of the Constitution Check table in `specs/001-project-foundation/plan.md` against the delivered code and record the result
- [ ] T138 Confirm each item of Constitution section 17 (Definition of Done) for this feature and tick `specs/001-project-foundation/checklists/requirements.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup. **Blocks every user story.**
- **US1 (Phase 3)**: Depends on Foundational. Blocks nothing, but is the MVP and should land first.
- **US2, US3, US4, US5 (Phases 4-7)**: Each depends only on Foundational and can proceed in
  parallel with the others once US1 has proven the stack runs.
- **Polish (Phase 8)**: Depends on all stories being complete.

### Notable within-phase dependencies

- T003 → T004, T005 (solution must exist first); T007 → T008 → T009 (workspace before libraries
  before Material).
- T016 → T018 (options exist before fail-fast aggregates them); T021 → T022 → T024 (context, then
  interceptor, then migration); T022 → T052 (interceptor before its test).
- T025 → T027 (correlation exists before the error handler embeds it). This is why correlation sits
  in Foundational while the rest of observability sits in US4.
- T030 → T031 (container fixture before the web factory) → every integration test. T031's
  configurable environment name is what T081 and T113 need.
- T033 → T036 (config before interceptors); T035 → T039 (state types before state components).
- T056, T057 → T058, T060 → T069 (catalog and abstractions before enforcement, then the controller).
- T066 → T080, T081 (OpenAPI registered before the contract-match and exposure tests).
- T087 → T089 → T090 → T092 (Transloco, service, direction, then string replacement).
- T100 → T101, T102 (Serilog configured before enrichment or redaction).
- T045 → T108 (development entry first, then per-environment enforcement in the same file - these
  two are the only tasks that touch `CorsSetup.cs`, and they must not run concurrently).
- T117, T118 → T125 (scripts exist before artifact validation is added to them); T123, T124 → T125.

### Parallel Opportunities

- Setup: T001, T002, T004, T006, T009, T010, T012, T013, T014 are independent files.
- Foundational: T019, T020, T026, T029, T032, T034, T035, T039, T040 touch disjoint files.
- US1: T048-T053 are six independent test files.
- US2: T073-T086 are fourteen independent test files - the largest parallel block in the feature.
- US4: T110-T115 are six independent test files.
- US5: T119, T120, T121, T122, T126, T128 are independent documentation and configuration files.
- Across stories: after Foundational, one developer can take US2 (backend conventions), a second
  US3 (localization), and a third US4 (observability and hardening) with no file conflicts.

---

## Implementation Strategy

### MVP first (User Story 1 only)

1. Phase 1 Setup → 2. Phase 2 Foundational → 3. Phase 3 US1 → **stop and validate**: clone on a
clean machine, run `docs/getting-started.md`, confirm the platform runs end to end. This is
demonstrable to a stakeholder as "the project exists and runs".

### Incremental delivery

1. Setup + Foundational → platform ready
2. US1 (T043-T055) → runnable stack (MVP)
3. US2 → conventions proven by the reference slice; the first business feature can now start
4. US3 → bilingual, bidirectional shell
5. US4 → diagnosable and hardened
6. US5 → gates enforced and artifacts packageable for IIS
7. Polish → removability, SC-002 measurement, accessibility, documentation, constitutional sign-off

### Parallel team strategy

After Foundational completes and US1 is green, US2, US3, US4, and US5 can be assigned to different
developers. US2 is roughly twice the size of the others and is the critical path for starting real
business features, so staff it first.

---

## Notes

- Tests are mandatory here, not optional: Constitution XIII requires business rule, authorization,
  and validation-failure coverage, and the story phases place those tests beside the code they
  cover.
- Every task names its file path. Tasks marked [P] touch disjoint files and have no incomplete
  dependency.
- Two deliberate non-tests, both recorded in the spec: the one-second language-switch target
  (SC-004) is confirmed by observation in T099 rather than a flaky timing assertion, and integration
  test isolation (FR-046) is guaranteed by the unique database name and documented in T126 rather
  than proven by a concurrency test.
- Commit after each task or logical group; the story checkpoints are the natural review points.
- Do not skip T120 (deliberate-violation check). A gate nobody has watched fail is not a gate.
