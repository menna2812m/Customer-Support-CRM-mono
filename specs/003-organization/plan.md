# Implementation Plan: Organization Structure

**Branch**: `003-organization` | **Date**: 2026-08-30 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/003-organization/spec.md`

## Summary

Give departments, branches, and teams substance. They have existed since feature 001 as three
nullable columns on a user and a set of claim names, and they have been empty for every user ever
created, because nothing could write them. This feature makes them records an administrator
maintains, so feature 004 can place people in them.

**The shape**: branches stand alone as geography, teams belong to departments. A person's team
therefore implies their department, and that implication is the feature's one real invariant -
moving a team between departments has to carry its members with it, or the first reorganization
strands everyone in a department they left.

**The debt this closes**: feature 002 recorded an explicit exception against Constitution VIII -
placement columns with no foreign key - on the stated basis that "the organization feature adds the
constraints when it adds the tables." This is that feature. The migration adds the three tables and
the three foreign keys together, and no data migration is needed because every placement column is
null today.

**The part that is not additive**: FR-018 retires the identity provider's placement claims, which
edits code feature 002 shipped. Everything else in this feature is new code beside old code. The
distinction matters for review and for the task ordering: the retirement is a small, contained
change with its own tests, and it is sequenced last so it cannot destabilize the rest.

This is also the first feature in the product with a collection endpoint. Feature 002 recorded that
"no collection endpoint in this feature, so pagination does not arise"; here it arises, and the
`PageRequest`/`PagedResult` contract from feature 001 is used as built rather than extended.

## Technical Context

**Language/Version**: C# 14 on .NET 10 (SDK 10.0.400); TypeScript on Angular 22 - unchanged from
features 001 and 002
**Primary Dependencies**: None new, in either stack. EF Core 10, FluentValidation, and Angular
Material are already present and sufficient. A feature that adds no dependency is worth noting
explicitly, because it means every risk here is a risk in code this team owns
**Storage**: SQL Server. Three new tables - `Department`, `Branch`, `Team` - plus three foreign keys
added to the existing `User` placement columns, in one migration. Uniqueness is enforced by filtered
unique indexes that exclude soft-deleted rows (research decision 1)
**Testing**: xUnit v3 + Shouldly + NSubstitute; integration tests on Testcontainers.MsSql;
NetArchTest for layering; Vitest for the frontend. No new test infrastructure - unlike feature 002,
this feature needs no fake external service, because it has no external dependency
**Target Platform**: Windows Server + IIS in production; Kestrel + `ng serve` in development
**Project Type**: Web application - the established monorepo, adding one vertical slice
**Performance Goals**: None meaningful. The spec's assumption is tens of units maintained by hand;
the largest query in the feature returns one page of a list measured in tens of rows. Stated so that
no one later mistakes the absence of a performance section for an oversight
**Constraints**: Constitution v1.0.0. The team move must be atomic (FR-015). Uniqueness must survive
soft deletion and concurrent creation. Edits are last-write-wins by decision, so no concurrency
token exists to lean on
**Scale/Scope**: Tens of units in total across all three kinds. Single figures of administrators.
Nothing here approaches a limit of anything

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Gate (constitution principle) | Status |
|---|-------------------------------|--------|
| I | Business logic sits in Domain/Application, not controllers; no frontend feature reaches into another feature internals | PASS - the containment rule, the move-and-resync, and the dependent checks are Domain and Application; controllers bind, call, and map. The `organization` frontend feature imports only from `@crm/core` and `@crm/ui` |
| II | EF Core + SQL Server; every schema change ships as an EF Core migration | PASS - one migration adds three tables, three foreign keys, and the filtered unique indexes. The auditing interceptor and soft-delete conventions from feature 001 apply unchanged |
| III | Endpoints under `/api/v1`, explicit DTOs (no entities returned), shared pagination/filter/sort contract | PASS - all endpoints under `/api/v1/organization`; DTOs only; the first real use of `PageRequest`/`PagedResult`, consumed as feature 001 built it |
| IV | Every protected operation declares its required permission; authorization enforced server-side; audit records for security-sensitive actions | PASS - `organization.view` on reads and `organization.manage` on writes, Staff population only (AR-003); every mutation records through `IAuditRecorder` |
| V | No single-department or single-branch assumption; organizational visibility scoping considered | PASS - the feature exists precisely to support many of each. Scoping is considered and deliberately not applied: AR-004 makes structure global reference data, because scoping the tree by the tree is circular |
| VI | Angular standalone APIs; `core/ shared/ features/` placement; HTTP only in data-access services; Reactive Forms for non-trivial forms | PASS - one `features/organization/` folder, `organization-api.service.ts` the only place `HttpClient` appears, reactive forms throughout, following the `diagnostics` reference slice |
| VII | Arabic RTL and English LTR both handled; no hard-coded user-visible strings | PASS - and further than usual: the data itself is bilingual (FR-005), lists sort by the active language (LR-002), and both names sit on one form so a unit cannot be half-translated (LR-003) |
| VIII | Keys, FKs, unique constraints, indexes; audit columns; no hard delete of traceable business records | PASS, **and closes feature 002's recorded exception** - the three placement foreign keys it deferred are added here. Filtered unique indexes on codes and names; soft delete throughout; audit columns via the interceptor |
| IX | Status changes, assignments, escalations recorded; history never overwritten | PASS - activation changes, deletions, and team moves are recorded as audit entries; nothing overwrites a prior record. AR-006 keeps a moved team's origin traceable after the fact, which the team row alone no longer shows |
| X | Consistent error contract; no stack traces to clients; all six Angular UI states handled | PASS - duplicate code, duplicate name, delete-with-dependents, and move-into-inactive each map to a documented ProblemDetails code the client translates; list screens use the state components |
| XI | Structured logging with correlation id; no secrets or sensitive customer data logged | PASS - organizational structure is not sensitive customer data, and no new value needs redacting. Audit metadata carries unit identifiers and counts only |
| XII | Attachments go through the storage abstraction; allowed types, sizes, and authorization specified | N/A - no file handling in this feature |
| XIII | Tests cover business rules, authorization, and validation failures; critical Angular workflows tested | PASS - the move-and-resync, dependent refusal, uniqueness under soft delete, and the two permissions each have tests; the frontend covers the three list screens and the move dialog |
| XIV | Core workflows still function when the AI provider is unavailable; AI output labeled and user-accepted | N/A - no AI capability |
| XV | External vendors sit behind adapters; retry and idempotency defined; failures cannot corrupt CRM state | N/A - this feature has no external dependency. It removes one: the identity provider stops being consulted about placement |

**Initial gate result**: PASS. Three N/A entries, all scope-based.

**Post-design re-evaluation**: PASS. The design surfaced one thing worth re-checking against
Principle VIII. Filtered unique indexes exclude soft-deleted rows, which means a deleted unit's code
becomes available again - that is intended (FR-006 makes delete-and-recreate the remedy for a
mistyped code), but it does mean the audit trail can contain two distinct units that shared a code
at different times. AR-005 records unit identifiers rather than codes, so the trail stays
unambiguous. No gate moved.

The re-evaluation also confirmed Principle I is not strained by research decision 2: rejecting
`ExecuteUpdateAsync` for the member reassignment keeps the rule in the Domain and keeps one
definition of what "updated" means.

## Project Structure

### Documentation (this feature)

```text
specs/003-organization/
├── plan.md              # This file
├── spec.md              # Feature specification (19 FR, 9 clarifications)
├── research.md          # Phase 0 output - six technical decisions
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
└── contracts/           # Phase 1 output
    ├── README.md
    └── organization-api.yaml
```

### Source Code (repository root)

Only the files this feature adds or changes. Everything else stays as features 001 and 002 built it.

```text
backend/
├── src/
│   ├── Crm.Domain/Organization/           # Branch, Department, Team + the containment rule
│   ├── Crm.Application/
│   │   ├── Organization/                  # create, rename, activate, delete, move use cases
│   │   ├── Abstractions/                  # IOrganizationStore
│   │   └── Authorization/Permissions.cs   # + organization.view, organization.manage
│   ├── Crm.Infrastructure/
│   │   ├── Organization/                  # store implementation
│   │   ├── Identity/                      # CHANGED - provider placement reading removed
│   │   └── Persistence/                   # 3 entity configurations + one migration
│   └── Crm.Api/
│       ├── Organization/                  # DepartmentsController, BranchesController, TeamsController
│       └── Configuration/CrmOptions.cs    # CHANGED - ProviderClaimNames loses three members
└── tests/
    ├── Crm.UnitTests/Organization/        # containment, move-and-resync, dependent refusal
    └── Crm.IntegrationTests/Organization/ # endpoints, permissions, uniqueness under soft delete

frontend/projects/crm-web/src/app/features/organization/
├── organization-api.service.ts            # the only HttpClient in the feature
├── departments.page.*                     # list + form, teams managed within
├── branches.page.*                        # list + form
└── move-team.dialog.*                     # User Story 3
```

**Structure Decision**: No new projects, no new libraries, no new dependencies. The feature adds an
`Organization` area to each backend layer and one frontend feature folder, mirroring the shape
feature 002 established for `Identity`. Two existing files change rather than grow: `CrmOptions.cs`
loses three claim-name members, and `OpenIdConnectClient` stops reading placement.

## Implementation Phases

| Stage | Delivers | Spec stories |
|-------|----------|--------------|
| A. Schema and domain | Branch, Department, Team entities; containment rule; entity configurations; one migration with the three tables, three foreign keys, and filtered unique indexes | US1, US2 |
| B. Permissions | `organization.view` and `organization.manage` in the catalog; seed migration granting both to Administrator | US1, US2 |
| C. Departments and teams | Create, rename, activate, deactivate, delete, list; dependent refusal; uniqueness including the per-department team name rule | US1 |
| D. Branches | The same surface for branches, which have no containment rule and so are a smaller repeat of stage C | US2 |
| E. Team move | Move with member resync in one transaction; refusal on inactive destination and on name collision in the destination; the audit entry carrying both departments and the affected count | US3 |
| F. Frontend | Departments list with teams inside, branches list, forms with both names, the move dialog, all six UI states, ar/en strings | US1, US2, US3 |
| G. Retire provider placement | Remove placement from `ProviderIdentity`, `OpenIdConnectClient`, `ProviderClaimNames`, `RefreshFromProvider`, `StaffSignIn`, and `appsettings`; update feature 002's affected tests | - (FR-018, FR-019) |

Stage G is sequenced last deliberately. It is the only stage that edits shipped code, and putting it
after the additive work means a problem there cannot be confused with a problem in the new feature.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No constitutional violations. Two choices cost more than the minimum and are recorded here.

| Choice | Why Needed | Simpler Alternative Rejected Because |
|--------|------------|--------------------------------------|
| Loading team members to reassign them, rather than one set-based `ExecuteUpdateAsync` | The audit columns are maintained by `AuditingSaveChangesInterceptor`, which a set-based update bypasses entirely - so the operation that most needs a trail would be the one that stops writing one | The set-based update is one statement and obviously faster. Rejected because the spec's own scale makes the speed irrelevant (a team's membership is tens of rows) while the cost is a second, hand-maintained definition of what "updated" means, in the one place it matters most |
| Filtered unique indexes rather than plain ones | Soft-deleted rows stay in the table forever, so a plain unique index would retire a code permanently the first time a unit carrying it was deleted - contradicting FR-006, which makes delete-and-recreate the remedy for a mistyped code | A plain unique index is simpler and needs no `WHERE` clause. Rejected because it makes the spec's own stated remedy impossible, and the failure would only surface the first time somebody mistyped a code and tried to fix it |
