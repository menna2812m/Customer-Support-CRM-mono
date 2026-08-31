# Constitution Compliance and Definition of Done

**Feature**: 004-identity-administration | **Verified**: 2026-08-31

Re-verification of the Constitution Check table in [plan.md](./plan.md) against the delivered code,
plus the Definition of Done from Constitution section 17.

## Constitution Check, re-verified against the implementation

| # | Principle | Result | Evidence in the delivered code |
|---|---|---|---|
| I | Layering | **PASS** | `PeopleService` and `ClaimDecision` are Application types; `PeopleController` and `RolesController` bind, call, and map. The claim matrix is a pure function in `Application/Identity/Claiming/ClaimDecision.cs` that reads nothing and writes nothing, so the sign-in path carries the decision out rather than containing it. `IdentityProblems` turns a refusal into a status in the API layer, so Application names the reason without knowing HTTP. |
| II | EF Core + SQL Server, migrations | **PASS** | Two migrations: `IdentityAdministration` (the nullable subject, the `Provider` column, and both filtered indexes) and the permission seed. Reviewed by hand against T008's four checks - the column widened rather than being dropped and recreated, both indexes carrying their filters, `Provider` added with no default that would falsely attribute existing rows, and no data migration. |
| III | `/api/v1`, DTOs, shared contracts | **PASS** | Every endpoint under `/api/v1/identity`. Records only - `PersonSummary`, `PersonDetail`, `RoleView`; no entity is returned. The people list consumes `PageRequest`/`PagedResult` as feature 003 did. The drift test compares the live document against `contracts/identity-api.yaml` in both directions, and no `x-status: planned` marker remains. |
| IV | Backend-enforced authorization | **PASS** | `identity.view` on reads, `identity.manage` on every write, Staff population only. `IdentityAuthorizationTests` proves a view-only caller is refused every write and a portal caller is refused everything (AR-003). Every mutation records through `IAuditRecorder`, including the two that are refusals rather than changes. |
| V | Multi-level organization model | **PASS** | Placement is across all units and a person may sit in any. Scoping was considered and deliberately not applied: AR-004 makes administration global, because scoping the people list by the placement this feature exists to assign is circular. Delegated administration is named in Out of Scope rather than left unasked. |
| VI | Feature-first Angular | **PASS** | One `features/identity/` folder: `identity-api.service.ts` is the only `HttpClient` caller, two lazy-loaded pages, reactive forms throughout. The People entry was added to the shell in the same change as the routes (T034), which is the specific failure feature 003 shipped. |
| VII | Arabic and English | **PASS**, with an asymmetry worth naming | Unlike an organization unit, a person has one name from the provider rather than one per language, so the list orders identically in both languages (LR-002) - enforced in the query and commented at `PeopleStore.cs:37`. Unit names inside the feature still follow the reader's language (LR-003) through the shared pipe. 174 keys in each resource file, parity enforced by the gate. |
| VIII | Integrity and traceability | **PASS** | Two filtered unique indexes replace two plain ones, so that uniqueness means what the spec says: `(Provider, ProviderSubject) WHERE ProviderSubject IS NOT NULL` and `(Email) WHERE IsDeleted = 0`. Soft delete throughout. Role assignments are still hard-deleted on revoke, which is pre-existing and is why FR-025 puts the history in the audit entry instead. |
| IX | History never overwritten | **PASS** | Grants, revocations, placement changes, activation changes, deletions, claims, refused claims, and collisions each write an entry rather than mutating a prior one. A deletion carries the roles held immediately beforehand, because revoking them destroyed the only other trace. |
| X | Error handling and UI states | **PASS** | Seven codes, each translated in both languages. The two new sign-in refusals - `identity_email_not_verified` and `identity_email_ambiguous` - reach the browser through the existing callback redirect, so a refused claim says why rather than reading as a generic failure. Both screens use `crm-state-container`. |
| XI | Structured logging | **PASS**, and it needed the care the plan predicted | `ClaimAudit` is a separate type for exactly this reason: the entries most tempting to enrich with a token or a claim dump are the refusals. `ClaimAuditTests` asserts the **exact set** of metadata keys rather than the presence of the address, so adding a third one is a failing test rather than an unnoticed leak. |
| XII | File handling abstraction | **N/A** | No file handling in this feature. |
| XIII | Testing | **PASS** | 277 backend tests and 114 frontend, all passing. The claim matrix has a test per row at the unit level and a second pass end to end through the real handshake; both lockout guards, the placement invariant, delete atomicity, and immediate session ending each have one. |
| XIV | AI optional | **N/A** | No AI capability. |
| XV | Integrations behind adapters | **PASS** | The identity provider stays behind feature 002's `OpenIdConnectClient`. This feature reads one additional claim through it, whose name is configuration (`Authentication:Staff:ClaimNames:EmailVerified`) rather than a hard-coded Keycloak spelling. |

**Result: no violations.** Two N/A entries, both scope-based.

## The specification was wrong, and was corrected rather than implemented

FR-017 said that a sign-in matching one unclaimed record on an **unverified** address must "create an
ordinary new person". It cannot, and no implementation of it could.

The address belongs to the unclaimed record, and T006 made email unique among people who are not
deleted (`UNIQUE (Email) WHERE IsDeleted = 0`). A second live row holding that address is not
undesirable - it is unwritable. An implementation faithful to the sentence would have raised a
`DbUpdateException` inside the sign-in path and returned a 500 to somebody whose only mistake was an
unverified address.

The same index makes the **ambiguous** row unreachable: at most one live person can hold an address,
so "more than one unclaimed match" cannot be produced by the current schema at all.

Resolved by refusing the sign-in in both cases, with `identity_email_not_verified` and
`identity_email_ambiguous` - the codes T002 had already added for exactly these situations. Refusing
is also the more informative outcome: the administrator who prepared the address learns their
preparation went unused, instead of finding a duplicate person standing beside it.

Amended in `spec.md` (FR-017 and the clarification behind it), `data-model.md` (the matrix and the
reasoning below it), and `quickstart.md` (the manual check, which expected the wrong thing).

The ambiguous branch stays in `ClaimDecision` and in its tests. The decision is a pure function over
what it is handed, the branch costs nothing, and it is what makes "pick the first match" impossible
to write by accident on the day that index filter changes.

## What the tests found

- **The people list test depended on being on page one.** `The_signed_in_administrator_appears_in_the_list`
  read the first fifty rows of a database the whole suite shares. It passed for as long as the suite
  created fewer than fifty people; this feature's tests pushed it over, and it began failing on a
  row belonging to another test. Rewritten to search for the address. The never-signed-in filter test
  had the identical fault and the identical fix.
- **A cleared placement reaches the client as an absent key, not a null one.** The API omits null
  properties, so an assertion on `ValueKind == Null` fails on the missing key rather than on the
  wrong value. Worth recording because both shapes look correct in a test and only one of them is
  what the client actually receives.

## Deviations from the plan, and why

| Planned | Delivered | Reason |
|---|---|---|
| T017/T018, T032, T048/T049 as separately named files | Folded into `UserIdentityTests`, `person.page.ts`, and `people.page.ts` | The placement form and the pre-provision form are each used in exactly one place. Extracting a two-field form into its own component to match a filename adds indirection with no reuse; the behaviour each task asked for is delivered and covered where it lives. |
| T041 `PreProvisionEndpointTests.cs` | Written, but narrower than planned | `PeopleEndpointsTests` already refused an address belonging to somebody who has signed in. The new file covers what it did not reach: an address held by a record that is only prepared, and an address spelled in a different case. Duplicating the existing case was removed rather than kept. |
| `identity_subject_collision` as the code returned for a collision | Returned as `identity_collision`; `identity_subject_collision` is the audit reason | Feature 002 already refuses a collision with `identity_collision`, and the browser already translates it. The matrix in data-model.md asks for `identity_subject_collision` in the **audit** entry, which is where it is. |
| `FindByEmailAsync` returning one record | `FindAllByEmailAsync` returning a list | The claim decision must distinguish "exactly one prepared record" from "several" (FR-016, FR-017). Handing it a first row would make the ambiguity untestable and the branch unreachable by construction rather than by schema. |

## Definition of Done

| Item | Status |
|---|---|
| Specification, plan, and task list committed | **Yes** - spec (31 FR, 12 clarifications), plan, research, data model, contracts, quickstart, tasks |
| Constitution Check passed and re-verified after implementation | **Yes** - the table above |
| All tests pass | **Yes** - 277 backend, 114 frontend, both suites run in full |
| Format, lint, i18n parity, and logical-CSS gates pass | **Yes** - 174 shared keys; no physical direction properties |
| Contract published and drift-checked | **Yes** - no `x-status: planned` marker remains, verified in both directions by test |
| Relevant documentation updated | **Yes** - `docs/getting-started.md` gains "Put people in it"; `quickstart.md` corrected where it expected the old FR-017 |
| Migration reviewed rather than assumed | **Yes** - T008's four checks, by hand |
| Both verification scripts run as scripts | **Yes** - `verify-backend.ps1` and `verify-frontend.ps1` both pass end to end. Feature 003 could not run the frontend script; this one could, with nothing holding `node_modules` |
| Manual walkthrough completed | **Not yet** - see below |

## Outstanding

**The feature has not been exercised in a browser.** Every rule it enforces has an automated test,
the API is verified through HTTP by the integration suite, and the claim matrix is driven end to end
through the real OIDC handshake against the in-process provider. What remains is `quickstart.md`'s
manual walkthrough (T069), and specifically the three parts an in-process provider cannot stand in
for:

- **Keycloak with its email-verified flag turned off.** The suite proves the CRM fails closed when
  the assertion is absent; only a real provider proves Keycloak spells it the way the default
  configuration expects.
- **RTL layout on both screens** (T065). The gates prove no physical direction property is used and
  that both languages carry the same keys. Neither proves the people list reads correctly in Arabic.
- **The session-ending check by hand**, watching a second signed-in window be refused on its next
  request rather than at credential expiry.

One warning is carried knowingly: the production bundle is 536 kB against a 500 kB budget. It
predates this feature's close-out and is a warning rather than a gate failure.
