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
| XIII | Testing | **PASS** | 281 backend tests and 115 frontend, all passing. The claim matrix has a test per row at the unit level and a second pass end to end through the real handshake; both lockout guards, the placement invariant, delete atomicity, and immediate session ending each have one. |
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

## What the browser found, and the automated suites could not

The walkthrough was run against the real Keycloak container on 2026-09-01. It found three defects,
all of which had passed every gate.

**1. Every person who signed in before this feature was locked out of their own account.** The
migration adds `Provider` without backfilling it - T008 confirmed the absence of a default
deliberately, reasoning that one would falsely claim existing rows came from an unknown issuer. The
reasoning was right about a literal default and wrong about the consequence: those rows *did* come
from the configured issuer, and leaving them NULL orphans them from the composite lookup this
feature introduced. Sign-in then falls through to the address lookup, finds the person's own bound
record, and reads it as somebody else's - refusing with `identity_collision`, which tells the person
an administrator must resolve a conflict. The administrator is among the locked out.

No test could have caught it. Testcontainers builds the schema from migrations into an empty
database, so `Provider` is always populated by `ProvisionAsync` and a pre-migration row never exists.

Fixed by making the lookup self-healing: the exact `(provider, subject)` pair is tried first and
always wins; failing that, a row carrying the subject and no provider is accepted, and the provider
that just authenticated its owner is recorded onto it. Each row heals once, on its owner's next
visit, and needs no deployment step that could be skipped. `User.AdoptProvider` refuses a row that
already names one, so this can never become a rebind. Covered by
`ClaimingTests.Somebody_bound_before_the_provider_was_recorded_still_signs_in`, which seeds the row
with SQL because the domain will not create one any more.

**2. Every status badge rendered its own translation key.** The screen showed
`identity.status.Active` rather than "نشط". `JsonStringEnumConverter` was registered without a
naming policy, so `PersonStatus` serialized as `Active` while both resource files - and the
published contract, which declares `enum: [invited, active, inactive]` - use lowercase. The client
builds a translation key from the value, and transloco prints the key it cannot find.

The test that should have caught it could not, for a reason worth recording: **Shouldly compares
strings case-insensitively by default.** `body.ShouldContain("\"status\":\"active\"")` passes
against `"status":"Active"`, and so does the same line with `Case.Insensitive` written out. Only
`Case.Sensitive`, passed explicitly, fails. Both status assertions now pass it, and the converter
takes `JsonNamingPolicy.CamelCase`.

**3. A successful creation left the form looking rejected.** Both required fields turned red the
instant a person was prepared. `form.reset()` resets the group but not the `FormGroupDirective`,
which stays marked submitted, and Material's default error matcher treats a submitted form as
touched. Fixed with `resetForm()` on the directive.

Its test needed two corrections before it was worth having. Asserting on `form.pristine` passed
either way, because `reset()` does clear pristine. Asserting on `mat-error` also passed either way,
because this template gates `mat-error` on `touched` alone - the red is Material's own field state,
not an error element. The assertion that fails without the fix is on `.mat-form-field-invalid`,
after submitting through the template rather than by calling the method.

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
| All tests pass | **Yes** - 281 backend, 115 frontend, both suites run in full |
| Format, lint, i18n parity, and logical-CSS gates pass | **Yes** - 174 shared keys; no physical direction properties |
| Contract published and drift-checked | **Yes** - no `x-status: planned` marker remains, verified in both directions by test |
| Relevant documentation updated | **Yes** - `docs/getting-started.md` gains "Put people in it"; `quickstart.md` corrected where it expected the old FR-017 |
| Migration reviewed rather than assumed | **Yes** - T008's four checks, by hand |
| Both verification scripts run as scripts | **Yes** - `verify-backend.ps1` and `verify-frontend.ps1` both pass end to end. Feature 003 could not run the frontend script; this one could, with nothing holding `node_modules` |
| Manual walkthrough completed | **Mostly** - run against the real provider and it found three defects, all fixed. Two parts need a second provider account; see below |

## Outstanding

**The walkthrough is mostly done.** Run on 2026-09-01 against the real Keycloak container, signed in
as the bootstrap administrator. Confirmed by hand:

- **Sign-in through the real provider**, including the repair of the legacy row above - the same
  account that was refused a minute earlier signed in, and the row now records its issuer.
- **RTL on both screens** (T065). The people list, the add-a-person form, the filters, the identity
  block, the roles checklist, the effective-permissions list and the placement row all read
  right-to-left, with addresses left-to-right inside them.
- **The list orders identically in both languages** (LR-002) and unit names follow the reader -
  "الفوترة" in Arabic, "Billing" in English (LR-003).
- **Placement derivation on screen**: with a team chosen, the department is filled from it and
  disabled.
- **Self-protection on screen** (T033): on the administrator's own record, delete and deactivate are
  disabled and the administrator checkbox is checked and disabled, each with the reason stated
  rather than the control hidden.
- **Preparing somebody**: created, listed as Invited with no department, and the list refreshed
  without a reload.
- **The SQL scans** from `quickstart.md`: zero INV-2 violations, zero duplicate live addresses, zero
  bound rows still missing a provider, and one active administrator.

Two parts are still outstanding, and both need a **second** account at the identity provider, which
means creating one and typing its password:

- **The claim matrix end to end** - a prepared address claimed by a verified sign-in, and refused
  when Keycloak's email-verified flag is off. The decision itself is covered by
  `ClaimDecisionTests` and `ClaimingTests` against the in-process provider; what is unproven is only
  that Keycloak spells the assertion `email_verified`, as the default configuration expects.
- **The session-ending check by hand**, watching a second signed-in window be refused on its next
  request. `SessionEndingTests` proves the 403-to-401 transition through real HTTP; what is
  unproven is only the same thing in a browser.

One warning is carried knowingly: the production bundle is 536 kB against a 500 kB budget. It
predates this feature's close-out and is a warning rather than a gate failure.
