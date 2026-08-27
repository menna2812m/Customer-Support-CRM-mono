# Implementation Plan: Project Foundation

**Branch**: `001-project-foundation` | **Date**: 2026-08-26 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/001-project-foundation/spec.md`

## Summary

Stand up the monorepo and the cross-cutting platform that every future CRM vertical feature will
sit on: a four-layer ASP.NET Core backend, an Angular workspace whose shared code lives in
libraries, SQL Server access through EF Core migrations, and the conventions the constitution
mandates - versioned REST under `/api/v1`, one error contract, one pagination contract,
permission-based authorization that denies by default, bilingual RTL/LTR UI, structured logging
with correlation identifiers, health reporting, transport hardening, and test and lint gates.

No business capability ships here. Correctness is demonstrated by a removable reference vertical
slice over non-business diagnostic data, plus tests that assert each convention.

Technical approach: standards-first and framework-native wherever possible - RFC 9457
ProblemDetails as the error contract, ASP.NET Core health checks, built-in OpenAPI, Serilog for
structured file logging under IIS, dual JWT bearer schemes behind a policy scheme selector for the
two caller populations, and a dynamic permission policy provider so an endpoint declares a
permission constant and enforcement follows automatically. Frontend uses Angular standalone APIs,
Angular Material with CDK bidi for direction, Transloco for runtime language switching, and
functional HTTP interceptors as the single cross-cutting request seam.

## Technical Context

**Language/Version**: C# 14 on .NET 10 (SDK 10.0.400 installed, pinned via `global.json`);
TypeScript 5.9 on Angular 22 (Angular CLI 22.1.6, Node 22.22.3, npm 10.9.8 installed)
**Primary Dependencies**: ASP.NET Core 10, EF Core 10 (SqlServer provider), Asp.Versioning.Mvc,
FluentValidation, Serilog (file + console sinks), ASP.NET Core HealthChecks + SqlServer check,
Microsoft.AspNetCore.OpenApi with Scalar UI (development only), Microsoft.AspNetCore.
Authentication.JwtBearer; Angular Material 22 + CDK, @jsverse/transloco, RxJS 7, Angular Signals
**Storage**: SQL Server (developer instance for running the app; disposable container for tests).
Baseline migration creates migration history only - no business tables (spec FR-011)
**Testing**: xUnit v3 + Shouldly + NSubstitute (unit), WebApplicationFactory + Testcontainers.MsSql
(integration), ArchUnitNET (architecture rules), Vitest via `@angular/build:unit-test` with Karma
fallback if the installed CLI lacks the builder (frontend)
**Target Platform**: Production - Windows Server + IIS (backend published folder in an IIS site,
frontend static assets with SPA fallback). Development - Kestrel + `ng serve` on separate origins
**Project Type**: Web application - monorepo with an ASP.NET Core modular monolith backend and an
Angular workspace frontend
**Performance Goals**: No traffic model exists yet (spec defers this). Foundation-level targets
only: full verification set under 10 minutes excluding the one-time image pull (SC-009), language
switch visible within 1 second (SC-004), health reflects database state within 30 seconds (SC-006)
**Constraints**: Constitution v1.0.0 is binding; Arabic RTL and English LTR from day one; no
secrets in source control or logs; deny-by-default authorization; no business logic in
controllers; no vendor SDK reachable from the Domain layer; rate limiting explicitly out of scope.
Fixed numeric limits: request body 10 MB, JSON depth 32, request collections 500 items, readiness
dependency-check cache 5 seconds, page size default 25 / maximum 100
**Scale/Scope**: 4 backend projects + 3 test projects, 1 Angular application + 2 workspace
libraries, ~15 permission constants registered as examples, 1 removable reference slice,
2 anonymous endpoints (health, and OpenAPI in development only)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Initial evaluation (pre-research) and post-design re-evaluation both recorded below.

| # | Gate (constitution principle) | Status |
|---|-------------------------------|--------|
| I | Business logic sits in Domain/Application, not controllers; no frontend feature reaches into another feature internals | PASS - four projects with enforced references; ArchUnitNET tests assert Domain has no outbound project references and controllers hold no rule logic; ESLint boundary rule blocks cross-feature imports |
| II | EF Core + SQL Server; every schema change ships as an EF Core migration | PASS - EF Core 10 SqlServer provider; baseline migration created and applied in the integration suite on every run; startup auto-migration off outside development |
| III | Endpoints under `/api/v1`, explicit DTOs (no entities returned), shared pagination/filter/sort contract | PASS with a stated exemption - URL-segment versioning via Asp.Versioning; ProblemDetails error contract; `PagedResult<T>` + `PageRequest` shared contracts; ArchUnitNET test forbids EF entity types in controller signatures. **Exemption**: health probes and the development-only OpenAPI document sit outside `/api/v1` because they are infrastructure endpoints consumed by hosting and tooling, not application APIs; versioning them would break the hosting contract on every version bump. Recorded in spec FR-015; applies to those endpoints only |
| IV | Every protected operation declares its required permission; authorization enforced server-side; audit records for security-sensitive actions | PASS - fallback policy requires an authenticated user; `[RequirePermission(Permissions.X)]` with a dynamic policy provider; `IAuditRecorder` seam in Application |
| V | No single-department or single-branch assumption; organizational visibility scoping considered | PASS - `ICurrentUser` carries population plus department/branch/team scope, and a portal caller is representable with no scope at all |
| VI | Angular standalone APIs; `core/ shared/ features/` placement; HTTP only in data-access services; Reactive Forms for non-trivial forms | PASS - standalone bootstrap, `@crm/core` and `@crm/ui` libraries, feature folders in the app; ESLint rule forbids `HttpClient` outside `*-api.service.ts`; typed reactive forms with a server-error binding helper |
| VII | Arabic RTL and English LTR both handled; no hard-coded user-visible strings | PASS - Transloco with `ar`/`en` resources, CDK `Directionality` driven from the active language, logical CSS properties, key-parity check in CI |
| VIII | Keys, FKs, unique constraints, indexes; audit columns; no hard delete of traceable business records | PASS - `IAuditableEntity` + `ISoftDeletable` with a `SaveChangesInterceptor` and a global query filter, available before the first entity exists |
| IX | Status changes, assignments, escalations recorded; history never overwritten | N/A - no business entity or state machine ships in this feature; the `IAuditRecorder` seam is delivered for the features that will |
| X | Consistent error contract; no stack traces to clients; all six Angular UI states handled | PASS - `IExceptionHandler` returning ProblemDetails with correlation id and no detail leakage; six state components in `@crm/ui` with a demo route |
| XI | Structured logging with correlation id; no secrets or sensitive customer data logged | PASS - Serilog with `LogContext` correlation enrichment, rolling file sink for IIS, destructuring policy that redacts known-sensitive members, and a test asserting redaction |
| XII | Attachments go through the storage abstraction; allowed types, sizes, and authorization specified | N/A - file handling is out of scope for this feature; nothing here blocks introducing the abstraction later |
| XIII | Tests cover business rules, authorization, and validation failures; critical Angular workflows tested | PASS - reference tests demonstrate all three backend kinds plus component and data-access tests on the frontend |
| XIV | Core workflows still function when the AI provider is unavailable; AI output labeled and user-accepted | N/A - no AI capability in this feature; the integration adapter pattern established here is what AI will later plug into |
| XV | External vendors sit behind adapters; retry and idempotency defined; failures cannot corrupt CRM state | PASS by construction - no external integration ships; Infrastructure is the only project permitted to reference vendor packages, enforced by an architecture test |

**Initial gate result**: PASS with four N/A entries, each because the subject matter is out of this
feature's scope rather than because a rule was waived.

**Post-design re-evaluation**: PASS, unchanged. The Phase 1 design introduced no new violation.
Two points were checked specifically: (a) the Angular library split does not weaken feature
isolation, because the boundary lint rule covers library-to-feature and feature-to-feature imports
alike; (b) the dual authentication scheme does not create a second authorization path, because
both schemes resolve to the same `ICurrentUser` and the same permission policy pipeline.

## Project Structure

### Documentation (this feature)

```text
specs/001-project-foundation/
├── plan.md              # This file (/speckit.plan command output)
├── spec.md              # Feature specification
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── README.md
│   ├── error-contract.md
│   ├── pagination-contract.md
│   ├── foundation-api.yaml
│   └── frontend-contracts.md
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (/speckit.tasks - NOT created here)
```

### Source Code (repository root)

```text
global.json                        # pins SDK 10.0.400
.editorconfig                      # shared formatting + analyzer severity
docs/                              # setup, conventions, production configuration
scripts/
├── verify-backend.ps1             # restore, build, test, format check
└── verify-frontend.ps1            # install, lint, test, format check

backend/
├── Crm.sln
├── Directory.Build.props          # nullable, warnings-as-errors, analysis level
├── Directory.Packages.props       # central package version management
├── src/
│   ├── Crm.Api/                   # controllers, middleware, filters, DI composition
│   │   ├── Common/                # error handling, correlation, security headers
│   │   ├── Diagnostics/           # health + reference slice controller
│   │   └── Program.cs
│   ├── Crm.Application/           # abstractions, DTOs, validators, permission catalog
│   │   ├── Abstractions/          # ICurrentUser, IAuditRecorder, IDateTimeProvider
│   │   ├── Authorization/         # Permissions catalog, population enum
│   │   ├── Common/                # PagedResult, PageRequest, ErrorCodes
│   │   └── Diagnostics/           # reference slice use case + validator
│   ├── Crm.Domain/                # entity base types, audit/soft-delete contracts
│   └── Crm.Infrastructure/        # CrmDbContext, migrations, interceptors, adapters
└── tests/
    ├── Crm.UnitTests/
    ├── Crm.IntegrationTests/      # WebApplicationFactory + Testcontainers.MsSql
    └── Crm.ArchitectureTests/     # ArchUnitNET layer + contract rules

frontend/
├── angular.json                   # one application, two libraries
├── package.json
├── deploy/
│   └── web.config                 # IIS SPA fallback for the static build
└── projects/
    ├── crm-web/src/app/
    │   ├── core/                  # app-level providers, guards placeholder, layout
    │   ├── features/diagnostics/  # reference slice: page + data-access service
    │   └── app.config.ts
    ├── core/                      # @crm/core: http interceptors, config, i18n, errors
    └── ui/                        # @crm/ui: six state components, layout, theme
```

**Structure Decision**: Monorepo with a `backend/` .NET solution and a `frontend/` Angular
workspace, per Constitution I and VI. The backend is split into the four mandated projects plus
three test projects. The frontend workspace holds exactly one application (`crm-web`) with
cross-cutting and presentation code extracted into the `@crm/core` and `@crm/ui` libraries, so the
future external customer portal can be added as a second application without relocating code
(spec FR-003, clarification 2026-08-26). The reference vertical slice lives in
`Crm.Api/Diagnostics` and `features/diagnostics` so it can be deleted in one commit.

## Implementation Phases

Phase 0 and Phase 1 outputs are complete. The build order below is what `/speckit.tasks` will
expand; it follows the user story priorities in the spec.

| Stage | Delivers | Spec stories |
|-------|----------|--------------|
| A. Skeleton | Repo tooling, solution, four projects, Angular workspace with both libraries, database connection, baseline migration, environment configuration, dev run | US1 |
| B. API conventions | Versioning, ProblemDetails error contract, validation pipeline, pagination contract, DTO rules, OpenAPI, architecture tests | US2 |
| C. Authorization seams | Dual bearer schemes, permission catalog and policy provider, deny-by-default, `ICurrentUser`, audit seam | US2 |
| D. Frontend infrastructure | Runtime config, interceptors, global error handling, six state components, routing, reactive form helper, Transloco + direction switching | US3 |
| E. Observability + hardening | Serilog file logging, correlation propagation, redaction, health checks, HTTPS/HSTS, headers, CORS allowlist, request limits | US4 |
| F. Quality gates | Unit/integration/architecture suites, frontend tests, lint and format for both stacks, verification scripts, documentation | US5 |
| G. Production packaging | Production settings profile, backend publish output for an IIS site, frontend static build with SPA-fallback `web.config`, required-settings inventory | US5 (FR-006, FR-008) |
| H. Accessibility | Keyboard operability, accessible names, focus visibility, state announcements, and AA contrast across the shared shell and state components | Polish (FR-057) |

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No constitutional violations. Two structural choices cost more than the minimum and are recorded
here for transparency, both traceable to accepted clarifications rather than to preference:

| Choice | Why Needed | Simpler Alternative Rejected Because |
|--------|------------|--------------------------------------|
| Two Angular libraries (`@crm/core`, `@crm/ui`) rather than folders in the app | The clarified decision requires a second application to be addable with no code relocation, and libraries give an explicit public API surface that a lint rule can enforce | Plain folders with tsconfig path aliases would work today but leak internals (no `public-api.ts`), and the boundary would rest entirely on lint configuration |
| A separate `Crm.ArchitectureTests` project | Several constitutional rules (no logic in controllers, no entities in contracts, vendor packages confined to Infrastructure) are only enforceable as executable assertions | Code review alone was rejected: these rules are violated silently and cheaply, and the constitution requires authorization and layering to be verifiable, not aspirational |
