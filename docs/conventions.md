# Development Conventions

How a vertical feature is built here. The binding rules are in `.specify/memory/constitution.md`;
this document explains how to follow them in code, and where the enforcement lives.

## Layers and where code belongs

| Layer | Holds | Never holds |
|---|---|---|
| `Crm.Domain` | Entities, value objects, invariants, base contracts (`Entity`, `IAuditableEntity`, `ISoftDeletable`, `IHasOrganizationScope`) | References to any other project, EF Core, ASP.NET Core |
| `Crm.Application` | Use cases, DTOs, validators, permission catalog, abstractions (`ICurrentUser`, `IAuditRecorder`) | Persistence, HTTP types |
| `Crm.Infrastructure` | `CrmDbContext`, migrations, interceptors, vendor adapters, DI composition for all of it | Business rules |
| `Crm.Api` | Controllers, middleware, filters, configuration | Business rules, EF Core, database drivers |

These are executable rules, not guidance: `Crm.ArchitectureTests` fails the build when a layer
reaches somewhere it should not, when a controller touches persistence, or when a vendor package
escapes `Crm.Infrastructure`.

## Adding a vertical feature

The diagnostics slice (`Crm.Api/Diagnostics`, `Crm.Application/Diagnostics`,
`frontend/.../features/diagnostics`) is the worked example. Copy its shape:

1. **Domain** - entity inheriting `Entity`, implementing `IAuditableEntity` (and `ISoftDeletable`
   if history matters). Never assign the audit fields by hand; the interceptor does it.
2. **Application** - a DTO per request and response, a `AbstractValidator<T>` beside it, the use
   case, and any new permission constants in `Permissions`.
3. **Infrastructure** - `IEntityTypeConfiguration<T>` (discovered automatically) and a migration:
   `dotnet ef migrations add <Name> --project backend/src/Crm.Infrastructure --startup-project backend/src/Crm.Api`.
4. **Api** - a thin controller: route `api/v{version:apiVersion}/<resource>`,
   `[RequirePermission(...)]`, `[RequirePopulation(...)]`, and nothing else but mapping.
5. **Frontend** - a folder under `features/<feature>/` with one `*-api.service.ts` (the only file
   allowed to inject `HttpClient`), a page using `crm-state-container`, and `ar`/`en` keys added
   together.
6. **Tests** - a business rule test, an authorization test covering allowed *and* denied, a
   validation failure test, and a frontend component plus data-access test.

Registering a feature touches at most one shared file per side: `Program.cs` for a use-case
service, `app.routes.ts` for a route. If a change needs more, that is a signal the seam is wrong.

## Declaring permissions and caller populations

```csharp
[RequirePermission(Permissions.Tickets.Assign)]
[RequirePopulation(CallerPopulation.Staff)]
```

- Permission values come from the `Permissions` catalog. A typo is a build error, which is the
  entire point of code-declared permissions.
- Population is stamped by the authenticating scheme, never read from the token. A portal token
  cannot reach a staff-only endpoint even holding a permission of the same name.
- Access is denied by default. `[AllowAnonymous]` is rare, reviewable, and currently used only by
  the health probes and the development-only OpenAPI document.
- Frontend guards and `*ngIf` on permissions shape the experience only. The server is the
  authority, always.

## Error codes and the error contract

Every failure response is RFC 9457 problem details plus `code`, `correlationId`, and - for
validation - `errors[]`. See `specs/001-project-foundation/contracts/error-contract.md`.

- Add new codes to `ErrorCodes`, and a translation under `errors.code.*` in **both** language
  files.
- Never write an error body in a controller. The exception handler, the validation filter, and
  `ValidationProblems` are the only producers, which is what keeps the contract true everywhere.
- Never return server text to the user: the frontend maps `code` to a translated message.

## Pagination, filtering, sorting

Use `PageRequest` / `PagedResult<T>`; see `contracts/pagination-contract.md`. Per endpoint:

- publish a sortable-field allow-list and validate with `PageRequestRules.ValidateSort`;
- list accepted query parameters in `[RejectUnknownQuery(...)]`, so a misspelled filter is refused
  rather than silently ignored;
- apply authorization scoping *before* paging, so `totalCount` never lies.

Do not name a bound `PageRequest` parameter `page`: MVC treats a matching query key as a binding
prefix and the values silently fall back to defaults. Call it `paging`.

## Localization workflow

- Every user-visible string is a translation key. Add it to `en.json` and `ar.json` in the same
  commit - `npm run i18n:check` fails the build on a mismatch.
- Styles use logical properties only (`margin-inline-start`, `inset-inline-end`, `text-align: start`).
  `npm run css:check` fails on `left`/`right`. This is what makes RTL work with no per-screen
  exception.
- Direction is derived from the language by `LanguageService`. Never set `dir` anywhere else.

## Deferred by design

**Rate limiting is deliberately not implemented here** (spec FR-056). It belongs with the
authentication feature, where login brute-force and per-identity policy give it meaning. Do not
add a local throttle to an endpoint; add it once, there.

## What "one registration point" means in practice

Measured against the diagnostics slice (spec SC-002). Adding a feature touches:

| Side | Shared file | Change |
|---|---|---|
| Backend | `Program.cs` | One `AddScoped<T>()` line for the use case |
| Frontend | `app.routes.ts` | One lazy route entry |

Everything else is new files inside the feature folder. Two further shared files receive *content*
rather than structural change: `app.ts` gains a navigation entry, and `en.json` / `ar.json` gain
the feature's keys. No shared infrastructure, contract, or pipeline file is edited - which is the
property SC-002 is really about, and the reason a second feature does not become harder than the
first.

## Removing the reference diagnostics slice

Once a real feature exists, the slice can go in one commit:

```powershell
Remove-Item -Recurse backend/src/Crm.Api/Diagnostics/DiagnosticsController.cs
Remove-Item -Recurse backend/src/Crm.Application/Diagnostics
Remove-Item -Recurse backend/tests/Crm.IntegrationTests/Diagnostics
Remove-Item -Recurse frontend/projects/crm-web/src/app/features/diagnostics
```

This was executed on 2026-08-26: with the slice deleted the solution built clean, 22 of 23 backend tests passed, and the single failure was the OpenAPI drift test correctly reporting that the contract still published paths the application no longer served. Removing those paths from `foundation-api.yaml` made the suite green. The drift test is doing exactly what it exists for.

Then drop the `DiagnosticItemQuery` registration from `Program.cs`, the `diagnostics` route from
`app.routes.ts` and its nav entry, the `diagnostics.*` keys from both language files, and the
`/api/v1/diagnostics/*` paths from `contracts/foundation-api.yaml`. Keep
`backend/src/Crm.Api/Diagnostics/HealthEndpoints.cs` and `DatabaseHealthCheck.cs` - those are
operational, not part of the slice.
