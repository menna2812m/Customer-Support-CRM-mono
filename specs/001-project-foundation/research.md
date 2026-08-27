# Phase 0 Research: Project Foundation

**Feature**: 001-project-foundation | **Date**: 2026-08-26

The specification and its five clarifications fixed the platform, hosting model, caller
populations, UI foundation, hardening scope, test database strategy, and permission storage. What
remained open were library-level choices and the mechanics of each convention. Each decision below
is recorded as Decision / Rationale / Alternatives considered.

Installed toolchain confirmed on the development machine: .NET SDK 10.0.400, Angular CLI 22.1.6,
Node 22.22.3, npm 10.9.8, Docker 29.7.2, `sqlcmd` present.

---

## 1. Toolchain pinning

**Decision**: Pin the SDK with `global.json` to `10.0.400` using `rollForward: latestFeature`.
Pin Node with an `.nvmrc` / `engines` entry at 22.x. Use central package management
(`Directory.Packages.props`) for all NuGet versions, and exact versions in `package.json` with a
committed lockfile.

**Rationale**: Two SDKs are already installed side by side (10.0.302 and 10.0.400); without a pin,
different machines silently build with different compilers. Central package management keeps one
version list for seven projects.

**Alternatives**: No pin (rejected - reproducibility is a prerequisite for SC-001); per-project
`PackageReference` versions (rejected - drifts as projects multiply).

---

## 2. API versioning

**Decision**: `Asp.Versioning.Mvc` + `Asp.Versioning.Mvc.ApiExplorer`, URL segment reader only,
route template `api/v{version:apiVersion}/[controller]`, default version 1.0, version reported in
responses. Unknown versions are translated into the shared error contract by the same exception
handler that covers everything else.

**Rationale**: URL segment versioning is what the constitution mandates (`/api/v1`). The
ApiExplorer integration is what lets OpenAPI emit a correct per-version document.

**Alternatives**: Header or query-string versioning (rejected - contradicts Constitution III);
hand-rolled route prefixes (rejected - no ApiExplorer integration, and every controller repeats
the literal).

---

## 3. Error contract

**Decision**: RFC 9457 `application/problem+json` as the wire format, extended with three members:
`code` (stable machine-readable string), `correlationId`, and `errors` (array of `{ field,
code, message }` for validation failures). Produced centrally by an `IExceptionHandler` plus
`AddProblemDetails` customization, never by individual controllers.

**Rationale**: A standard media type gives tooling, client libraries, and OpenAPI schemas for
free, while the three extensions carry exactly what the spec requires (FR-017). One central
producer is what makes SC-003 - 100% contract conformance - testable.

**Alternatives**: A bespoke `{ success, data, error }` envelope (rejected - reinvents a standard,
and wraps success payloads for no benefit); per-controller try/catch (rejected - guarantees
divergence, and Constitution X forbids leaking detail, which is easy to get wrong 40 times).

---

## 4. Validation

**Decision**: FluentValidation with validators registered by assembly scan in
`Crm.Application`, invoked by an MVC action filter that short-circuits before the action runs and
emits the error contract with one `errors` entry per invalid field. DataAnnotations remain
available for trivial shape constraints but are not the primary mechanism.

**Rationale**: Validators live next to the use case in Application, keeping controllers free of
rule logic (Constitution I). A filter guarantees FR-019 - nothing reaches business logic
unvalidated - rather than relying on each handler to remember.

**Alternatives**: DataAnnotations only (rejected - cannot express cross-field or conditional rules
the CRM will need); validation inside handlers (rejected - unenforceable, and duplicates the
failure-response mapping).

---

## 5. Pagination, filtering, sorting

**Decision**: `PageRequest { Page (1-based), PageSize, Sort, Filter }` bound from the query string,
and `PagedResult<T> { Items, Page, PageSize, TotalCount, TotalPages }`. `PageSize` defaults to 25
and is capped at 100. Sorting uses `sort=field` / `sort=-field` (leading minus = descending) and
only allow-listed fields per endpoint. Filtering uses explicit named query parameters per endpoint,
not a generic expression language.

**Rationale**: Offset paging matches the grid-and-page-number UX a support CRM needs. A hard cap
is a cheap denial-of-service guard that pairs with the request-limit requirement (FR-055). A
per-endpoint allow-list keeps sorting from becoming an arbitrary column-injection surface.

**Alternatives**: Cursor pagination (rejected - no jump-to-page, which ticket queues need; can be
added per endpoint later without changing this contract); OData or generic filter expressions
(rejected - unbounded query surface, hard to authorize and index).

---

## 6. Authentication: two populations, one pipeline

**Decision**: Two JWT bearer schemes registered side by side - `Staff` (validated against the
corporate identity provider's OIDC metadata) and `Portal` (validated against CRM-issued token
parameters) - selected by a policy scheme that inspects the token issuer, with the default scheme
being the policy scheme. Both schemes converge on a single `ICurrentUser` built from claims. This
feature registers the schemes with configuration-driven, disabled-by-default settings and full
test coverage using locally signed test tokens; it issues no real token and configures no real
provider.

**Rationale**: The clarified answer requires both populations from day one without a second
authorization path. A policy scheme keeps endpoint authoring uniform: an endpoint names its
permission and its admitted populations, never a scheme.

**Alternatives**: One scheme with a claim distinguishing population (rejected - a portal token
signed by the CRM would be validated with staff trust settings); cookie auth for the portal
(rejected - the portal is a separate origin SPA, so bearer keeps one model; can be revisited by
the portal feature without touching this seam).

---

## 7. Permission-based authorization

**Decision**: Permissions are `const string` members on a static `Permissions` catalog in
`Crm.Application`, grouped by area, exposed as an enumerable registry through reflection.
Endpoints declare `[RequirePermission(Permissions.Tickets.Assign)]`. An
`IAuthorizationPolicyProvider` materializes a policy per permission name on demand, backed by a
requirement and handler that read the caller's permission claims. A fallback policy requires an
authenticated user, so anything not explicitly `[AllowAnonymous]` is denied. Population admission
is a second, separately declared attribute checked in the same pipeline.

**Rationale**: Q5 fixed the catalog as code-declared; consts give compile-time safety and make a
typo a build error (FR-024). A dynamic policy provider avoids registering dozens of policies by
hand as the CRM grows.

**Alternatives**: Roles as policies (rejected - the constitution requires permission-based, and
roles harden into a permission mapping anyway); database-driven policy lookup at request time
(rejected by Q5, and it adds a query to every request).

---

## 8. Current user and organizational scope

**Decision**: `ICurrentUser` in `Crm.Application` exposing `UserId`, `Population`, `Permissions`,
and a nullable `OrganizationScope { DepartmentId, BranchId, TeamId }`. Implemented in `Crm.Api`
over `IHttpContextAccessor`, with a test double for unit tests. Portal callers have a null scope
by design.

**Rationale**: Constitution V requires scope to be expressible before any feature needs it, and
FR-027 requires that a caller without organizational scope be representable rather than a special
case bolted on later.

**Alternatives**: Passing claims down into handlers (rejected - couples Application to HTTP);
ambient static context (rejected - untestable, and hostile to background work later).

---

## 9. Auditing and traceability

**Decision**: `IAuditableEntity` (`CreatedAt/CreatedBy/UpdatedAt/UpdatedBy`) and `ISoftDeletable`
in `Crm.Domain`, stamped automatically by an EF Core `SaveChangesInterceptor` reading
`ICurrentUser` and `TimeProvider`. Soft-deleted rows are hidden by a global query filter.
`IAuditRecorder` in Application is the seam for security-sensitive action records; the
implementation shipped here writes a structured log entry, and the future audit-log feature
replaces it with persistence without touching call sites.

**Rationale**: Constitution VIII and IX demand these before the first entity exists, otherwise
every early entity is retrofitted. An interceptor makes stamping impossible to forget.

**Alternatives**: Manual assignment in handlers (rejected - forgotten exactly once, then wrong
forever); database triggers (rejected - invisible to the application, untestable in the unit
suite, and outside migration review).

---

## 10. Logging and correlation

**Decision**: Serilog as the provider, with a compact JSON rolling file sink (daily roll, size
cap, retained file count from configuration) plus a human-readable console sink in development.
Correlation middleware runs first: it accepts an inbound `X-Correlation-Id` when present and
otherwise uses the current `Activity` trace id, pushes it into `LogContext`, writes it to the
response header, and hands it to the error handler. A destructuring policy plus an explicit
redaction list keeps known-sensitive members (`password`, `token`, `secret`, `authorization`,
`connectionstring`) out of the sink.

**Rationale**: The constitution names Serilog as the default, and IIS hosting means logs must land
on disk rather than relying on console capture (FR-040). Reusing the W3C trace id when the caller
supplies nothing keeps correlation aligned with any future distributed tracing.

**Alternatives**: Built-in `ILogger` with the default providers (rejected - no structured file
sink without extra work); logging to the Windows Event Log (rejected - poor for structured
queries, awkward retention); OpenTelemetry export now (deferred - no collector exists yet, and the
`Activity` id chosen here is what an exporter would use later).

---

## 11. Health checks

**Decision**: ASP.NET Core health checks with two anonymous endpoints: `/health/live` (process
only) and `/health/ready` (includes the SQL Server check with a short timeout). Responses are a
minimal JSON document listing overall status and per-check status and duration - never exception
text, connection strings, or server names. The database check result is cached for **5 seconds**
so the endpoint cannot be used to hammer the database, while staying well inside the 30-second
reporting window SC-006 requires.

**Rationale**: FR-044 and SC-006 require dependency-accurate status without leaking internals. The
live/ready split is what IIS Application Request Routing or any future load balancer expects.

**Alternatives**: A single `/health` (rejected - conflates process liveness with dependency
readiness, causing needless recycles); the full default health-check UI payload (rejected -
verbose and leaks exception detail by default).

---

## 12. Transport and request hardening

**Decision**: `UseHttpsRedirection` + `UseHsts` outside development; a security-header middleware
setting `Content-Security-Policy`, `X-Content-Type-Options`, `Referrer-Policy`,
`X-Frame-Options`/`frame-ancestors`, and a minimal `Permissions-Policy`; a named CORS policy whose
origins come from configuration per environment with no wildcard permitted in any environment; and
request limits comprising a Kestrel/IIS body size cap of **10 MB**, a
`JsonSerializerOptions.MaxDepth` of **32**, and a default maximum of **500 items** per request
collection enforced by a shared validator rule. All are applied centrally in the pipeline; an
endpoint may lower a limit for its own payload, and raising one is a reviewed exception.

**Rationale**: Q3 put these in the application rather than in IIS configuration so they survive a
server rebuild and are assertable in the integration suite (SC-011).

**Alternatives**: IIS `web.config` rewrite rules and headers (rejected by Q3 - environment-owned,
untestable, silently lost on reconfiguration); a third-party hardening package (rejected -
the built-in middleware surface is sufficient and one less dependency to track).

---

## 13. OpenAPI documentation

**Decision**: The built-in `Microsoft.AspNetCore.OpenApi` document generator with Scalar as the
development-only UI. The document endpoint and UI are registered only when the environment is
Development, satisfying AR-002.

**Rationale**: .NET 10 ships OpenAPI generation in the framework; adding Swashbuckle would
duplicate it. Scalar is a thin UI over the generated document with no runtime footprint in
production.

**Alternatives**: Swashbuckle (rejected - redundant with the framework generator, heavier);
exposing the document in production (rejected - AR-002 forbids anonymous exposure outside
development).

---

## 14. Configuration and secrets under IIS

**Decision**: Layered configuration - `appsettings.json`, then `appsettings.{Environment}.json`,
then machine-level environment variables (`CRM_` prefix), then an `ISecretsSource` seam whose
default implementation reads a DPAPI-protected file outside the published folder. A startup
options validator (`ValidateOnStart`) fails fast in non-development environments listing every
missing or invalid setting by name. Development uses `dotnet user-secrets`, never a committed
file.

**Packaging**: the backend deployment artifact is `dotnet publish` output for an IIS site, and the
frontend artifact is the static `ng build` output plus a `web.config` whose rewrite rules serve
`index.html` for any non-file path (SPA fallback) and leave `/api` untouched. Both artifacts are
produced by the documented verification scripts so the packaging path is exercised before a
release depends on it. `docs/production-configuration.md` carries the required-settings inventory,
marking which are secret.

**Rationale**: Q1 fixed IIS hosting and a host-side protected store, but left the specific store to
operations - the seam is what lets that decision land later without code changes (FR-008).
`ValidateOnStart` is what turns FR-007 into observable behavior. Naming the artifacts here closes
the gap the analysis pass found: the strategy was documented but nothing produced or verified the
deployable output.

**Alternatives**: Azure Key Vault or another managed vault (rejected for now - no cloud dependency
was accepted, though the seam admits one); IIS application settings in `web.config` (rejected -
secrets end up in a file inside the published folder, which FR-008 forbids).

---

## 15. Frontend runtime configuration

**Decision**: The Angular app fetches `assets/config.json` during app initialization and exposes
the result through an injectable `AppConfig`. Build-time `environment.ts` holds only defaults for
local development. One built artifact is therefore promotable across environments.

**Rationale**: IIS serves static assets, so baking the API base URL into the bundle would require
one build per environment - a poor fit for the published-folder deployment model in FR-008, and it
satisfies FR-009 without a rebuild.

**Alternatives**: `environment.ts` per environment with `fileReplacements` (rejected - N builds, N
chances to ship the wrong one); injecting configuration into `index.html` at deploy time (rejected
- fragile string substitution during deployment).

---

## 16. Frontend HTTP infrastructure

**Decision**: Functional `HttpInterceptor`s composed in `@crm/core`, in fixed order: base URL
resolution, correlation id attachment, auth token attachment (a no-op seam until the auth feature
lands), then error normalization that converts any failure - including a network failure or a
non-conforming body - into one `AppError` shape. A global `ErrorHandler` reports anything that
escapes. Components never touch `HttpClient`; only `*-api.service.ts` files may, enforced by an
ESLint `no-restricted-imports` rule scoped by file pattern.

**Rationale**: FR-029 to FR-031 require exactly one cross-cutting seam and one client-side error
shape. Normalizing at the interceptor means every feature's error handling is written against one
type.

**Alternatives**: A base API service class other services extend (rejected - inheritance is opt-in,
so a new service can silently skip it); per-service error handling (rejected - guarantees six
different toasts by feature four).

---

## 17. Localization and direction

**Decision**: `@jsverse/transloco` with `ar` and `en` JSON resources loaded at runtime, the active
language persisted in local storage and re-applied at bootstrap. Direction is derived from the
active language and applied by setting `dir` on the document element and providing the CDK
`Directionality` value, so Angular Material components mirror automatically. Layout styles use
logical CSS properties (`margin-inline-start`, `padding-inline`, `text-align: start`), never
`left`/`right`. Backend messages are never displayed directly: the frontend maps the error
contract's `code` to a translated string, with the server `title` used only as a developer-facing
fallback. A key-parity script compares `ar` and `en` files and fails the build on a mismatch.

**Rationale**: Q2 chose Angular Material precisely for CDK bidi support. Runtime resources are
required by FR-036 - the user switches language in place - and LR-003 requires language-neutral
server errors.

**Alternatives**: Angular's built-in `@angular/localize` (rejected - compile-time locale bundles
cannot switch in place, contradicting FR-036); `ngx-translate` (viable and similar; Transloco
chosen for its typed, standalone-friendly API and built-in key-parity tooling).

---

## 18. Frontend testing

**Decision**: Unit tests through the Angular CLI's `@angular/build:unit-test` builder with the
Vitest runner, Angular Testing Library helpers for component behavior, and `HttpTestingController`
for data-access services. The first scaffolding task verifies the builder is available in CLI
22.1.6; if it is not, the fallback is Karma + Jasmine with the same test structure, and only the
runner configuration differs.

**Rationale**: Karma is deprecated, and the Angular build system's runner is the forward path.
Naming an explicit fallback keeps the plan honest rather than assuming a builder name.

**Alternatives**: Jest with a transform preset (rejected - extra transform maintenance, and it is
not the CLI's supported path); no frontend tests (rejected - Constitution XIII).

---

## 19. Backend testing

**Decision**: xUnit v3 with Shouldly assertions and NSubstitute for fakes. Unit tests target
Application and Domain in isolation. Integration tests use `WebApplicationFactory` over a
`Testcontainers.MsSql` container started once per test run by a collection fixture, with a
uniquely named database, migrations applied at startup, and disposal at the end; when the
container runtime is absent the fixture fails with a message naming Docker rather than reporting
misleading test failures. Architecture rules are asserted with ArchUnitNET.

**Rationale**: Q4 chose a self-provisioned disposable container. A collection fixture keeps one
container per run so the suite stays inside the 10-minute budget while giving each run a clean
database.

**Alternatives**: FluentAssertions (rejected - version 8 carries a commercial licence for
non-open-source use; Shouldly is Apache-2.0 and avoids the question entirely); Respawn against a
shared database (rejected by Q4); EF Core in-memory or SQLite (rejected - proves nothing about
SQL Server constraints, indexes, or migrations).

**Amended 2026-08-26 during implementation (T006)**: ArchUnitNET publishes no stable version -
`dotnet add package` resolves only `2.4.0-alpha.1`. A pre-release package that fails the build is
a poor foundation for a permanent quality gate, so the architecture tests use **NetArchTest.Rules
1.3.2** instead. The rules expressed are the same; only the fluent API differs. If ArchUnitNET
ships a stable release, switching is a contained change inside `Crm.ArchitectureTests`.

---

## 20. Linting, formatting, and verification entry points

**Decision**: Backend uses `.editorconfig` with analyzer severities, `Directory.Build.props`
setting `Nullable=enable`, `TreatWarningsAsErrors=true`, and `EnforceCodeStyleInBuild=true`, and
`dotnet format --verify-no-changes` in the verification script. Frontend uses `angular-eslint`
with the boundary and HttpClient rules described above, plus Prettier for formatting.
`scripts/verify-backend.ps1` and `scripts/verify-frontend.ps1` are the documented single entry
points and take no arguments, so a future pipeline calls them unchanged.

**Rationale**: FR-049, FR-050, and SC-009 require one documented command per stack that a pipeline
can adopt later without rewriting.

**Alternatives**: Committing a CI workflow now (rejected - no provider is chosen, and a broken
workflow file is worse than none); husky pre-commit hooks (deferred - a team convention decision,
not a foundation requirement).

---

## Open items carried into implementation

| Item | Handling |
|------|----------|
| Frontend unit-test builder name in CLI 22.1.6 | **Resolved 2026-08-26 (T011)**: `@angular/build:unit-test` with the Vitest runner is the default for every project the CLI generates (vitest 4.0.8 and jsdom 28 arrive in devDependencies). No Karma fallback was needed. |
| Which host-side secret store operations will use | **Still open for operations.** The `ISecretsSource` seam shipped with a DPAPI-protected-file default reading `CRM_SECRETS_FILE`; swapping it is one class plus registration. |
| Log retention numbers | **Resolved**: 30 daily files, 64 MB per file, configurable via `Observability:RetainedFileCount`. Verified 2026-08-26 - a dated compact-JSON file is written and carries the correlation identifier. |
| Corporate identity provider metadata | **Still open for the identity owners.** Both schemes ship registered, lazily configured, and disabled; the authentication feature supplies real values. Tests exercise them with locally signed tokens. |
