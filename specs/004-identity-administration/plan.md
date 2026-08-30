# Implementation Plan: Identity Administration

**Branch**: `004-identity-administration` | **Date**: 2026-08-30 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/004-identity-administration/spec.md`

## Summary

Make the organization structure usable by putting people in it, and let an administrator prepare
somebody before that person has ever signed in.

**The shape**: this is an administration surface over a model that already exists. `User` has
carried placement columns since feature 001 and role assignments since feature 002; feature 003
added the units those columns point at. Almost nothing here is a new concept. What is new is that a
person can exist before their identity does, and that one operation - the first sign-in - has to
decide whether an arriving identity is a returning person, a prepared person, or a stranger.

**The one real invariant**: a person's department follows their team. Feature 003 owns half of it
already, resyncing members when a team moves; this feature owns the other half, deriving the
department when a person is placed. Neither half is sufficient alone, which is why SC-002 scans for
disagreement rather than trusting either.

**The part that is not additive**: FR-015 to FR-020 change sign-in, which is the one code path every
user of the product traverses. Everything else is new code beside old code. That distinction drives
the sequencing - the claim change is stage G, after everything else works, so a failure there cannot
be confused with a failure anywhere else. It is the same reasoning feature 003 used to sequence its
retirement of provider placement last, and for the same reason.

**What this feature deliberately does not become**: the roles it grants are the roles the deployment
seeds. Defining authority is a different act from granting it, and it carries lockout risks of its
own; folding it in here would roughly double the feature and would put "which permissions exist" and
"who holds them" behind one review.

## Technical Context

**Language/Version**: C# 14 on .NET 10 (SDK 10.0.400); TypeScript on Angular 22 - unchanged from
features 001 through 003
**Primary Dependencies**: None new, in either stack. As with feature 003, worth stating explicitly:
every risk in this feature is a risk in code this team owns
**Storage**: SQL Server. No new tables. One migration alters `User` - `ProviderSubject` becomes
nullable and gains a `Provider` companion - and replaces two unique indexes with filtered ones
(research decisions 1 and 2). A second seed migration grants the two new permissions
**Testing**: xUnit v3 + Shouldly + NSubstitute; integration tests on Testcontainers.MsSql;
NetArchTest for layering; Vitest for the frontend. The claim rules need a matrix rather than a
handful of cases, because their failure mode is silent
**Target Platform**: Windows Server + IIS in production; Kestrel + `ng serve` in development
**Project Type**: Web application - the established monorepo, adding one vertical slice and editing
one existing path
**Performance Goals**: None meaningful. The people list is the largest query in the feature and is
one page of tens of rows. Stated so the absence of a performance section is not read as an oversight
**Constraints**: Constitution v1.0.0. Deletion must be indivisible (FR-024). The
never-zero-administrators rule must hold under concurrency (research decision 5). Access must end on
the next request, not at credential expiry (SC-005)
**Scale/Scope**: Hundreds of people at most, single figures of administrators, two seeded roles.
Nothing here approaches a limit of anything

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Gate (constitution principle) | Status |
|---|-------------------------------|--------|
| I | Business logic sits in Domain/Application, not controllers; no frontend feature reaches into another feature internals | PASS - the claim rules, the two lockout guards, and the placement derivation are Domain and Application; controllers bind, call, and map. The `identity` frontend feature imports only from `@crm/core` and `@crm/ui` |
| II | EF Core + SQL Server; every schema change ships as an EF Core migration | PASS - one migration for the column and index changes, one seed migration for the permissions. The auditing interceptor and soft-delete conventions apply unchanged |
| III | Endpoints under `/api/v1`, explicit DTOs (no entities returned), shared pagination/filter/sort contract | PASS - all endpoints under `/api/v1/identity`; DTOs only; the people list uses `PageRequest`/`PagedResult` as feature 003 consumed it |
| IV | Every protected operation declares its required permission; authorization enforced server-side; audit records for security-sensitive actions | PASS - `identity.view` on reads, `identity.manage` on writes, Staff population only (AR-003). This feature has more security-sensitive actions than any before it, and AR-005 to AR-008 record all of them, including the two that are refusals rather than changes |
| V | No single-department or single-branch assumption; organizational visibility scoping considered | PASS - placement is across all units and a person may sit in any. Scoping is considered and deliberately not applied: AR-004 makes administration global, because scoping the people list by the placement this feature exists to assign would be circular. Delegated administration is named in Out of Scope rather than left unasked |
| VI | Angular standalone APIs; `core/ shared/ features/` placement; HTTP only in data-access services; Reactive Forms for non-trivial forms | PASS - one `features/identity/` folder, `identity-api.service.ts` the only place `HttpClient` appears, reactive forms throughout |
| VII | Arabic RTL and English LTR both handled; no hard-coded user-visible strings | PASS - with an asymmetry worth naming: unlike an organization unit, a person has one name from the provider rather than one per language, so LR-002 fixes the list order across languages instead of varying it. Unit names inside this feature still follow the reader's language (LR-003) |
| VIII | Keys, FKs, unique constraints, indexes; audit columns; no hard delete of traceable business records | PASS - filtered unique indexes replace two plain ones so that uniqueness means what the spec says (research 1 and 2); soft delete throughout; audit columns via the interceptor. Role assignments are hard-deleted on revoke, which is pre-existing and is why FR-025 puts the history in the audit entry instead |
| IX | Status changes, assignments, escalations recorded; history never overwritten | PASS - grants, revocations, placement changes, activation changes, deletions, refused claims, and collisions are all recorded. FR-027 makes restoration deliberately non-restoring, so nothing is resurrected in a way that would contradict the trail |
| X | Consistent error contract; no stack traces to clients; all six Angular UI states handled | PASS - five new refusal codes, each mapping to a documented ProblemDetails code the client translates (LR-004); list and detail screens use the state components |
| XI | Structured logging with correlation id; no secrets or sensitive customer data logged | PASS, and it needs care here for the first time. AR-008 records refused claims and collisions, which are the entries most tempting to enrich with tokens or claim dumps; they carry the address involved and nothing more |
| XII | Attachments go through the storage abstraction; allowed types, sizes, and authorization specified | N/A - no file handling in this feature |
| XIII | Tests cover business rules, authorization, and validation failures; critical Angular workflows tested | PASS - the claim matrix, both lockout guards, the placement invariant, and delete atomicity each have tests; every screen this feature adds has a spec file, which SC-007 makes an outcome rather than a habit |
| XIV | Core workflows still function when the AI provider is unavailable; AI output labeled and user-accepted | N/A - no AI capability |
| XV | External vendors sit behind adapters; retry and idempotency defined; failures cannot corrupt CRM state | PASS - the identity provider is the only external party and it stays behind feature 002's existing client. This feature reads one additional claim through it and adds no new integration |

**Initial gate result**: PASS. Two N/A entries, both scope-based.

**Post-design re-evaluation**: PASS, with two things the design surfaced that the initial pass had
not.

Principle VIII reads differently once research decision 2 is on the page. Filtering the email index
on `IsDeleted = 0` means a deleted person's address can be held later by a different person, so the
audit trail can contain two people who shared an address at different times - the same ambiguity
feature 003 accepted for codes, and answered the same way: audit records carry person identifiers,
never addresses, as the identifying field. AR-008 is the single exception, recording an address for a
refused claim where no person identifier exists yet by definition.

Principle IV is strained in one place worth naming rather than smoothing over. The
never-zero-administrators rule is enforced in the Application layer over a count, not by a database
constraint, because no constraint can express "at least one row must remain". Research decision 5
closes the gap with isolation rather than pretending a check is sufficient. This is the only rule in
the feature whose correctness depends on a transaction rather than on the schema, and it is called
out here so a later reader does not relax it by accident.

No gate moved.

## Project Structure

### Documentation (this feature)

```text
specs/004-identity-administration/
├── plan.md              # This file
├── spec.md              # Feature specification (31 FR, 11 clarifications)
├── research.md          # Phase 0 output - seven technical decisions
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── checklists/
│   └── requirements.md  # Specification quality checklist
└── contracts/
    ├── README.md
    └── identity-api.yaml
```

### Source Code (repository root)

Only the files this feature adds or changes. Everything else stays as features 001 to 003 built it.

```text
backend/
├── src/
│   ├── Crm.Domain/Identity/
│   │   ├── User.cs                        # CHANGED - nullable subject, provider, claim + placement rules
│   │   └── Role.cs                        # unchanged; RoleAssignment already many-to-many
│   ├── Crm.Application/
│   │   ├── Identity/
│   │   │   ├── People/                    # list, get, pre-provision, place, activate, delete
│   │   │   └── Claiming/                  # the first-sign-in decision and its refusals
│   │   ├── Abstractions/IPeopleStore.cs   # reads, writes, and the counts the guards need
│   │   ├── Authorization/Permissions.cs   # + identity.view, identity.manage
│   │   └── Common/ErrorCodes.cs           # + five refusal codes
│   ├── Crm.Infrastructure/
│   │   ├── Identity/
│   │   │   ├── PeopleStore.cs             # store implementation
│   │   │   └── StaffSignIn.cs             # CHANGED - create-if-absent becomes match-then-create
│   │   └── Persistence/                   # User configuration + two migrations
│   └── Crm.Api/
│       ├── Identity/PeopleController.cs   # and RolesController for the checklist source
│       └── Configuration/CrmOptions.cs    # CHANGED - ProviderClaimNames gains EmailVerified
└── tests/
    ├── Crm.UnitTests/Identity/            # claim matrix, lockout guards, placement derivation
    └── Crm.IntegrationTests/Identity/     # endpoints, permissions, delete atomicity, session ending

frontend/projects/crm-web/src/app/features/identity/
├── identity-api.service.ts                # the only HttpClient in the feature
├── people.page.*                          # list, search, filters
├── person.page.*                          # identity, roles + effective permissions, placement
├── pre-provision-form.component.*         # add by email
└── placement-form.component.*             # branch; department derived from team
```

**Structure Decision**: No new projects, no new libraries, no new dependencies, and no new tables.
The feature adds an `Identity/People` area to each backend layer beside the authentication code
feature 002 put in `Identity`, and one frontend feature folder. Three existing files change rather
than grow: `User` gains the nullable subject and provider, `StaffSignIn` gains the claim decision,
and `CrmOptions` gains one claim name. The frontend adds a `People` navigation entry in the same
commit as its routes, because feature 003 shipped a complete screen that nothing linked to and
SC-007 exists to stop that happening twice.

## Implementation Phases

| Stage | Delivers | Spec stories |
|-------|----------|--------------|
| A. Schema | `ProviderSubject` nullable, `Provider` added, the two plain unique indexes replaced with filtered ones; one migration | US1, US2 |
| B. Permissions | `identity.view` and `identity.manage` in the catalog; seed migration granting both to Administrator, neither to Agent | US1 |
| C. Reads | People list with search and the four filters, person detail with roles and effective permissions; the store behind them | US1 |
| D. Roles and placement | Grant and revoke; the placement operation deriving department from team and refusing a mismatch; both lockout guards, guard and mutation in one transaction | US1 |
| E. Lifecycle | Pre-provision by email; activation; deletion as one transaction revoking roles, ending sessions, and recording what was held | US2, US3 |
| F. Frontend | People list, person detail with roles and derived permissions, pre-provision form, placement form with derived department, guarded controls that state their reason, all six UI states, ar/en strings, navigation entry | US1, US2, US3 |
| G. Claiming | Sign-in becomes match-then-create; the verified-email claim name; the ambiguity and collision refusals; feature 002's sign-in tests updated to assert the new behaviour | US2 |

Stage G is sequenced last deliberately, and it is the only stage that edits a path every user
traverses. Stages A to F leave the product working exactly as it does today for anyone signing in,
which means a regression in G is unambiguous.

Stage D and stage E both depend on the guards, so the guards are built once in D and used in E
rather than being written twice against two different call sites.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No constitutional violations. Three choices cost more than the minimum and are recorded here.

| Choice | Why Needed | Simpler Alternative Rejected Because |
|--------|------------|--------------------------------------|
| A serializable transaction around the never-zero-administrators guard | The rule is a read-then-write over a count, and two administrators acting at the same instant each read a safe count and together produce an unsafe result. No index can express "at least one row must remain", so the guarantee has to come from isolation | A plain check inside the use case, which is correct in every test and wrong in production exactly once - and the state it produces is a system nobody can administer without a database edit |
| Recording the roles a deleted person held inside the audit entry | `RoleAssignment` has no revocation history, so revoking deletes the only row that recorded the grant. Without this, FR-025's history exists nowhere | Adding `RevokedAt` to `RoleAssignment`, which is the better model and belongs to the feature that administers roles. Doing it here widens this feature into that one for a benefit this feature does not need |
| A composite filtered unique index rather than a plain unique index on the subject | Two requirements meet on this index: identity is provider-plus-subject, and invitations have no subject at all. SQL Server permits only one `NULL` in a unique index, so nullability alone would reject the second invitation ever created | A plain unique index on a nullable column, which looks correct, passes a single-invitation test, and fails the moment two people are prepared at once |
