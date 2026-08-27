# Phase 1 Data Model: Project Foundation

**Feature**: 001-project-foundation | **Date**: 2026-08-26

This feature persists **no business data**. Per spec FR-011 and the 2026-08-26 clarification, the
baseline migration creates no business tables - the resulting schema contains only EF Core's
migration history table. What this feature does define is the shape every future entity will
inherit and the in-memory contracts that carry identity, permissions, errors, and paging.

---

## 1. Persisted schema

| Object | Origin | Notes |
|--------|--------|-------|
| `__EFMigrationsHistory` | EF Core | Created by applying the baseline migration. Proves the migration workflow end to end (FR-011, FR-012). |

Nothing else. A test asserts that the migrated database contains no user tables, so the scope
boundary in SC-010 is enforced rather than reviewed.

---

## 2. Domain base contracts

Defined in `Crm.Domain`. These are the inheritance and interface surface every future entity uses;
they carry no dependency on EF Core, ASP.NET Core, or any vendor package.

### `Entity<TId>`

| Member | Type | Rules |
|--------|------|-------|
| `Id` | `TId` | Primary key. Default `Guid` created as a sequential value to keep clustered-index inserts cheap. Set once; never reassigned. |

Equality is identity-based: two entities of the same type with the same non-default `Id` are equal.

### `IAuditableEntity`

| Member | Type | Rules |
|--------|------|-------|
| `CreatedAt` | `DateTimeOffset` | Set once on insert from `TimeProvider`. Never modified afterwards. |
| `CreatedBy` | `Guid?` | The acting user at insert. Null only for system-initiated writes, which must name themselves in the audit record instead. |
| `UpdatedAt` | `DateTimeOffset?` | Set on every update. Null while the row has never been updated. |
| `UpdatedBy` | `Guid?` | The acting user at the most recent update. |

Stamped by `AuditingSaveChangesInterceptor`; handlers never assign these fields (Constitution VIII,
research decision 9).

### `ISoftDeletable`

| Member | Type | Rules |
|--------|------|-------|
| `IsDeleted` | `bool` | Defaults to false. |
| `DeletedAt` | `DateTimeOffset?` | Set when `IsDeleted` becomes true. |
| `DeletedBy` | `Guid?` | The acting user who deleted the row. |

A global query filter excludes soft-deleted rows by default; suppressing the filter is explicit and
reviewable. Entities requiring traceability must never be hard-deleted (Constitution VIII).

### `IHasOrganizationScope`

| Member | Type | Rules |
|--------|------|-------|
| `DepartmentId` | `Guid?` | Owning department, when the entity is department-scoped. |
| `BranchId` | `Guid?` | Owning branch. |
| `TeamId` | `Guid?` | Owning team. |

Declared now so visibility filtering can be applied uniformly once the organization feature exists
(Constitution V, AR-005). No entity implements it in this feature.

---

## 3. Persistence conventions

Applied by `CrmDbContext` in `Crm.Infrastructure` and asserted by tests, so that the first real
entity inherits them without a decision:

- **Naming**: tables singular PascalCase, columns PascalCase, one configuration class per entity
  via `IEntityTypeConfiguration<T>`, discovered by assembly scan.
- **Keys**: every entity has a single primary key; composite keys only for join entities.
- **Strings**: no unbounded `nvarchar(max)` by default - every string column declares a maximum
  length in its configuration.
- **Money/decimals**: explicit precision and scale; no provider default.
- **Timestamps**: `DateTimeOffset` throughout, stored UTC. No `DateTime` in the domain.
- **Enums**: stored as their underlying `int` unless a feature specification requires otherwise.
- **Deletes**: `DeleteBehavior.Restrict` by default; cascades are opt-in and justified in review.
- **Concurrency**: a `rowversion` concurrency token on entities that support concurrent edit; the
  base type provides the hook, no entity uses it yet.
- **Migrations**: generated into `Crm.Infrastructure/Persistence/Migrations`, reviewed as code,
  applied by command; automatic migration on startup is configuration-gated and off outside
  development (FR-013).

---

## 4. In-memory contracts

These are not persisted. They are the shapes the API and the frontend agree on, defined in
`Crm.Application` unless noted.

### `Permission` catalog

Code-declared, enumerable at runtime, the single source of truth (FR-024, clarification
2026-08-26).

| Aspect | Definition |
|--------|------------|
| Shape | `const string` members grouped in nested static classes by area, e.g. `Permissions.Tickets.Assign` = `"tickets.assign"` |
| Naming | `<area>.<action>`, lowercase, dot-separated - matching the constitution's examples |
| Registry | Reflection over the catalog yields every value; duplicates fail a test |
| Seeded examples | The constitution's names (`customers.view/create/update`, `tickets.view/create/assign/escalate`, `users.manage`, `reports.view`) plus `diagnostics.read` for the reference slice |
| Storage | None. Role-to-permission assignment is persisted later by the users-and-permissions feature, which seeds itself from this registry |

### `CallerPopulation`

Enum with two members: `Staff`, `Portal`. Every endpoint declares which populations it admits
(AR-004). A caller may belong to exactly one.

### `ICurrentUser`

| Member | Type | Rules |
|--------|------|-------|
| `IsAuthenticated` | `bool` | False for anonymous endpoints. |
| `UserId` | `Guid?` | Null when unauthenticated. |
| `Population` | `CallerPopulation?` | Resolved from the authenticating scheme, not from a client-supplied claim. |
| `Permissions` | `IReadOnlySet<string>` | Empty when unauthenticated. Values must exist in the catalog. |
| `Scope` | `OrganizationScope?` | Null for portal callers by design (FR-027). |

`OrganizationScope` is a record of `DepartmentId`, `BranchId`, `TeamId`, all nullable.

### `PageRequest` / `PagedResult<T>`

| Member | Type | Rules |
|--------|------|-------|
| `Page` | `int` | 1-based. Below 1 is a validation failure. |
| `PageSize` | `int` | Default 25, maximum 100. Above the maximum is a validation failure, not a silent clamp. |
| `Sort` | `string?` | `field` or `-field`; the field must be on the endpoint's allow-list. |
| `Items` | `IReadOnlyList<T>` | Never null; empty on an out-of-range page. |
| `TotalCount` | `long` | Total matching rows before paging. |
| `TotalPages` | `int` | Derived; zero when `TotalCount` is zero. |

### `ProblemDetails` extensions (error contract)

Wire shape is documented in [contracts/error-contract.md](./contracts/error-contract.md). The
application-side members are `code`, `correlationId`, and `errors[] { field, code, message }`.

### `AuditEntry` (seam input)

| Member | Type | Rules |
|--------|------|-------|
| `Action` | `string` | Stable identifier, e.g. `auth.login.failed`. |
| `ActorId` | `Guid?` | From `ICurrentUser`; null for anonymous attempts. |
| `TargetType` / `TargetId` | `string?` / `string?` | What was acted on, when applicable. |
| `OccurredAt` | `DateTimeOffset` | From `TimeProvider`. |
| `CorrelationId` | `string` | Ties the record to the request logs. |
| `Metadata` | `IReadOnlyDictionary<string,string>?` | Must contain no secret or sensitive value - the redaction rules apply here too. |

Consumed by `IAuditRecorder`. This feature's implementation writes a structured log entry; the
future audit-log feature persists it without changing any call site.

---

## 5. Frontend state shapes

Defined in `@crm/core`, mirrored from the API contracts so features never re-derive them.

| Shape | Members | Notes |
|-------|---------|-------|
| `AppError` | `kind` (`network`\|`validation`\|`unauthenticated`\|`forbidden`\|`notFound`\|`server`), `code`, `correlationId`, `fieldErrors?` | Every failure reaching a feature is already this shape (FR-030, FR-031). |
| `RequestState<T>` | `status` (`idle`\|`loading`\|`success`\|`empty`\|`error`), `data?`, `error?` | Signal-based; drives the six mandated UI states (FR-032). |
| `AppConfig` | `apiBaseUrl`, `defaultLanguage`, `supportedLanguages` | Loaded from `assets/config.json` at bootstrap (research decision 15). |
| `LanguageState` | `language` (`ar`\|`en`), `direction` (`rtl`\|`ltr`) | Persisted to local storage; direction is derived, never set independently. |

---

## 6. Reference slice data (removable)

The reference vertical slice uses no database table. Its list endpoint pages over an in-memory
sequence generated per request, purely to exercise the pagination contract, validation, permission
enforcement, and the six frontend states. Deleting `Crm.Api/Diagnostics`,
`Crm.Application/Diagnostics`, and `frontend/projects/crm-web/src/app/features/diagnostics` removes
it entirely, leaving no schema or configuration behind (FR-051).
