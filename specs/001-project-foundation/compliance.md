# Constitution Compliance and Definition of Done

**Feature**: 001-project-foundation | **Verified**: 2026-08-26

Re-verification of the Constitution Check table in [plan.md](./plan.md) against the delivered code
(tasks T137, T138), plus the Definition of Done from Constitution section 17.

## Constitution Check, re-verified against the implementation

| # | Principle | Result | Evidence in the delivered code |
|---|---|---|---|
| I | Layering | **PASS** | Four projects with enforced references. `LayeringRules` fails the build if `Crm.Domain` gains any outbound dependency or `Crm.Application` reaches into Infrastructure. Persistence composition moved into `Crm.Infrastructure.DependencyInjection` so `Crm.Api` references no EF Core type - a rule the third architecture test enforces. Frontend: ESLint blocks cross-feature and deep-library imports. |
| II | EF Core + SQL Server, migrations | **PASS** | EF Core 10 with the SQL Server provider; baseline migration applied on every integration run; `AutoMigrateOnStartup` refused outside Development by startup validation, and covered by a unit test. |
| III | `/api/v1`, DTOs, shared contracts | **PASS with a stated exemption** | URL-segment versioning; `ProblemDetails` error contract; `PageRequest`/`PagedResult<T>`. Health probes and the development-only OpenAPI document sit outside the version segment, recorded in FR-015 and the plan. |
| IV | Backend-enforced authorization | **PASS** | Deny-by-default fallback policy; `[RequirePermission]` + `[RequirePopulation]`; refusals audited through `IAuditRecorder`. Six integration tests cover anonymous, missing permission, correct permission, wrong population, and non-disclosure. |
| V | Multi-level organization model | **PASS** | `ICurrentUser` carries population and `OrganizationScope`; a portal caller is representable with no scope at all; `IHasOrganizationScope` exists for the first entity that needs it. |
| VI | Feature-first Angular | **PASS** | Standalone APIs, one application plus `@crm/core` and `@crm/ui` libraries, `HttpClient` restricted to `*-api.service.ts` by lint (verified failing on purpose), typed reactive forms with server-error binding. |
| VII | Arabic and English | **PASS** | Transloco with runtime switching, both resource files complete (59 keys each, parity enforced), direction derived from language and applied through the CDK, logical CSS enforced by script (verified failing on purpose). |
| VIII | Integrity and traceability | **PASS** | Audit stamps and soft delete applied by a `SaveChangesInterceptor`, proven by three integration tests; restrict-by-default deletes; bounded strings; decimal precision. |
| IX | Customer and ticket history | **N/A** | No business entity ships. The auditing and soft-delete conventions the future features need are in place and tested. |
| X | Error handling and UI states | **PASS** | One error producer; no leakage (asserted against a deliberately secret-bearing exception); six state components with a container that renders exactly one, covered by six tests. |
| XI | Structured logging | **PASS** | Serilog to compact JSON files with rotation and retention; correlation identifier on every entry; redaction at the pipeline boundary, covered by unit and integration tests; verified by running the app and reading the file. |
| XII | File handling abstraction | **N/A** | Out of scope for this feature; nothing here blocks introducing the abstraction. |
| XIII | Testing | **PASS** | 87 backend tests (unit, integration, architecture) and 25 frontend tests. All four mandated kinds are demonstrated by the reference slice. |
| XIV | AI optional | **N/A** | No AI capability in this feature. |
| XV | Integrations behind adapters | **PASS** | No external integration ships; vendor packages are confined to `Crm.Infrastructure`, enforced by an architecture test. |

**Result: no violations.** The four N/A entries are scope-based, unchanged from the plan.

## Definition of Done (Constitution section 17)

| Item | Status |
|---|---|
| Specification requirements implemented | **Yes** for 56 of 57 functional requirements. FR-006/FR-008 are implemented; two verification steps remain (below). |
| Backend validation exists | **Yes** - FluentValidation with a filter that runs before any business logic, plus model-binding failures on the same contract. |
| Backend authorization exists where required | **Yes** - deny by default, permission and population declared per endpoint, enforced server-side. |
| Database migration exists where required | **Yes** - baseline migration; applying it to an empty database creates no business tables, asserted by test. |
| Angular states handled | **Yes** - all six, via `crm-state-container`, demonstrated on both screens and covered by tests. |
| Arabic and English considered | **Yes** - both languages complete, direction switching verified by test, parity and logical-CSS checks in the gate. |
| Tests for critical rules pass | **Yes** - 112 tests across both stacks, all passing. |
| Errors follow application conventions | **Yes** - single contract, verified for validation, unauthenticated, forbidden, not found, unsupported version, and unhandled failure. |
| Logging follows security requirements | **Yes** - redaction enforced centrally and verified against a real log file. |
| Relevant documentation updated | **Yes** - README, getting started, conventions, production configuration, testing, plus the feature specs. |

## Outstanding, and why

| Item | Status | Why |
|---|---|---|
| T054 - execute every step of `docs/getting-started.md` | **Partly done** | Steps 2, 3 and the browser check need a local SQL Server instance, which was not installed on the machine used to build the foundation. Everything else was executed. The document states this explicitly rather than implying otherwise. |
| Host-side secret store | **Open for operations** | The `ISecretsSource` seam ships with a DPAPI-file default; choosing the real store is an operations decision, and swapping it is one class plus registration. |
| Corporate identity provider metadata | **Open for the identity owners** | Both schemes are registered, lazily configured, and disabled. The authentication feature supplies real values; tests exercise the pipeline with locally signed tokens. |
| Log directory permissions and next-day rotation | **After first deployment** | Verifiable only on the real host. |

## Defects found by running the system, and fixed

These are recorded because each one was invisible to review and only appeared under execution:

1. **Singleton consuming scoped services** (twice - the exception handler, then the authorization
   failure logger). The application refused to start. Both now resolve per-request services from
   the request scope.
2. **Startup auto-migration crashed the process** when the database was unreachable, violating
   FR-010. It now logs and starts, so readiness can report the outage.
3. **Configuration read during service registration** missed anything layered in afterwards - a
   deployment override or a test host. Authentication now binds lazily through `IOptions`.
4. **Query-binding prefix collision**: a `PageRequest` parameter named `page` swallowed `?page=`,
   silently returning page 1. Renamed, and the trap is documented in conventions.
5. **Validation responses bypassed the central customizer**, shipping without `correlationId`. All
   validation paths now go through one stamped builder.
6. **Deny-by-default turned unmatched paths into 401s**, and `MapFallback` skips file-like paths -
   so `/openapi/v1.json` answered 401 in production instead of 404. Fixed with an explicit
   anonymous catch-all.
