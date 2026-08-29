# Constitution Compliance and Definition of Done

**Feature**: 003-organization | **Verified**: 2026-08-30

Re-verification of the Constitution Check table in [plan.md](./plan.md) against the delivered code,
plus the Definition of Done from Constitution section 17.

## Constitution Check, re-verified against the implementation

| # | Principle | Result | Evidence in the delivered code |
|---|---|---|---|
| I | Layering | **PASS** | `OrganizationUnitService` and `TeamService` are Application types; the three controllers bind, call, and map. The containment rule and the move-refusals live in `Team.MoveTo` and the services; `OrganizationStore` is Infrastructure behind `IOrganizationStore`. `OrganizationProblems` maps a refusal to a status in the API layer, so the Application layer names the reason without knowing HTTP. |
| II | EF Core + SQL Server, migrations | **PASS** | One migration, `Organization`: three tables, nine filtered unique indexes, three foreign keys, and the permission grants. Reviewed by hand against T014's four checks - three separate tables rather than a TPH hierarchy, every unique index carrying its `[IsDeleted] = 0` filter, the foreign keys present, and no data migration. |
| III | `/api/v1`, DTOs, shared contracts | **PASS** | Nineteen endpoints under `/api/v1/organization`. Records only - `OrganizationUnitRecord`, `TeamRecord`, `TeamMoveResult`; no entity is returned. The product's **first** real use of `PageRequest`/`PagedResult`, consumed as feature 001 built it. The drift test compares the live document against `contracts/organization-api.yaml` in both directions. |
| IV | Backend-enforced authorization | **PASS** | `organization.view` on the controllers, `organization.manage` on every write, Staff population only. `Every_write_endpoint_declares_the_manage_permission` enumerates the writes as a theory, so a missed attribute is a failing test rather than a silent hole. Every mutation records through `IAuditRecorder`; the move additionally records both departments and the affected count. |
| V | Multi-level organization model | **PASS** | The feature exists to support many of each. Scoping was considered and deliberately not applied: AR-004 makes structure global reference data, because scoping the tree by the tree is circular. Nothing assumes a single department or branch - branches stay orthogonal to departments precisely so one Billing department can serve every city. |
| VI | Feature-first Angular | **PASS** | One `features/organization/` folder: `organization-api.service.ts` is the only `HttpClient` caller, two lazy-loaded pages, one dialog, one pipe. Reactive forms throughout, following the `diagnostics` reference slice. |
| VII | Arabic and English | **PASS**, and further than usual | The **data** is bilingual, not only the labels: `NameAr` and `NameEn` are both `NOT NULL`, both sit on one form (LR-003), lists sort by the reader's language in the query rather than in memory so it composes with paging (LR-002), and the team hierarchy indents with `margin-inline-start`. 118 keys in each resource file, parity enforced by the gate. |
| VIII | Integrity and traceability | **PASS**, and **closes feature 002's exception** | The three placement foreign keys feature 002 deferred are added here, `ON DELETE NO ACTION` - the constraint catches a bug, while FR-012 implements the rule. Nine filtered unique indexes; bounded strings; soft delete throughout, asserted through the API where the interceptor actually lives. |
| IX | History never overwritten | **PASS** | Creation, rename, activation change, deletion, and move each write an audit entry rather than mutating a prior one. AR-006 keeps a moved team's origin traceable after the fact, which the team row alone no longer shows. |
| X | Error handling and UI states | **PASS** | Four new codes, each translated in both languages: `organization_code_conflict`, `organization_name_conflict`, `organization_has_dependents`, `organization_department_inactive`. A refused delete names what depends on the unit. Both list screens use `crm-state-container`; a refused move keeps the dialog open showing why. |
| XI | Structured logging | **PASS** | Organizational structure is not sensitive customer data and no new value needs redacting. Audit metadata carries unit identifiers and counts only - never a name. |
| XII | File handling abstraction | **N/A** | No file handling in this feature. |
| XIII | Testing | **PASS** | 192 backend tests and 77 frontend, all passing. The rules that carry risk each have one: uniqueness under soft deletion and under concurrent insert, code immutability, the per-department team-name rule and its asymmetry with codes, dependent refusal with the reason named, all four authorization rules, and the move - including an INV-2 scan after a double move (SC-003). |
| XIV | AI optional | **N/A** | No AI capability. |
| XV | Integrations behind adapters | **N/A** | This feature has no external dependency. It removes one: the identity provider is no longer consulted about placement. |

**Result: no violations.** Three N/A entries, all scope-based, and feature 002's carried exception
is now closed rather than carried forward.

## What the tests found

Recorded because the value of the testing gate is easier to argue from evidence than from principle.
Three defects survived code review and were caught only by running the code:

- **The team move never worked.** `EnableRetryOnFailure` installs an execution strategy that refuses
  a user-initiated transaction, so every move returned a 500. The atomicity the whole feature turns
  on was not merely untested but absent.
- **`FindTeamAsync` filtered the projected record** rather than the entity, which EF cannot
  translate.
- **A test of ours asserted the opposite of the rule.** Soft deletion was checked through the
  fixture's own context, which has no interceptors registered, so `Remove()` there is a genuine hard
  delete. It would have passed while proving nothing.

The research assumption that the database compares case-insensitively is now an assertion rather
than a belief, since a case-sensitive deployment would silently weaken FR-006 rather than break it.

## Deviations from the plan, and why

| Planned | Delivered | Reason |
|---|---|---|
| The provider-claim retirement sequenced last, in Phase 6 | Done in Phase 2, with the schema | The foreign keys make a provider-asserted identifier a **constraint violation**, not merely an unhelpful value. Sign-in broke the moment the migration landed. The retirement is forced by the foreign keys rather than adjacent to them. |
| All frontend work grouped in one stage | Distributed into each user story | A story is only independently testable if it includes the screen that exercises it. |
| Dotted error codes (`organization.code_conflict`) | Underscored (`organization_code_conflict`) | The shipped `ErrorCodes` convention has no namespace separator. Consistency with delivered code won, and the contract was corrected rather than the code. |
| `ICorrelationAccessor` untouched | Moved from Infrastructure to `Application.Abstractions` | Application needed it for auditing and cannot reference Infrastructure. An abstraction two layers consume belongs beside `IAuditRecorder`. |

## Definition of Done

| Item | Status |
|---|---|
| Specification, plan, and task list committed | **Yes** - spec (19 FR, 9 clarifications), plan, research, data model, contracts, quickstart, tasks |
| Constitution Check passed and re-verified after implementation | **Yes** - the table above |
| All tests pass | **Yes** - 192 backend, 77 frontend |
| Format, lint, i18n parity, and logical-CSS gates pass | **Yes** - 118 shared keys; no physical direction properties |
| Contract published and drift-checked | **Yes** - nineteen endpoints in `organization-api.yaml`, verified in both directions by test |
| Relevant documentation updated | **Yes** - `docs/getting-started.md` (building an organization locally), `docs/production-configuration.md` unchanged (this feature adds no setting) |
| Migration reviewed rather than assumed | **Yes** - T014's four checks, by hand |
| Manual walkthrough completed | **Not yet** - see below |

## Outstanding

**The feature has not been exercised in a browser.** Every rule it enforces is covered by an
automated test, and the API is verified through HTTP by the integration suite, but `quickstart.md`'s
manual walkthrough - including the Keycloak claim-mapper check that proves the retirement, and the
SQL invariant scan after a move - has not been run. That is the one item between this record and a
complete Definition of Done.

`verify-frontend.ps1` has likewise not been run as a script, because its `npm ci` corrupts
`node_modules` while a development server holds file locks. Every gate it contains was run
individually and passed.
