---

description: "Task list for Identity Administration (004-identity-administration)"
---

# Tasks: Identity Administration

**Input**: Design documents from `/specs/004-identity-administration/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Required. Constitution Principle XIII applies as it has since feature 001. The rules
worth testing here are concentrated in four places - the claim decision, the two lockout guards, the
placement invariant, and the indivisibility of deletion - and each is written before the code that
satisfies it. The claim decision gets a matrix rather than a handful of cases, because a wrongly
claimed record looks exactly like a correctly claimed one.

**Organization**: Grouped by user story so each can be implemented and verified independently.

**A deliberate departure from the plan's stage table**: the plan sequences all frontend work as
stage F and the sign-in claim change as stage G, after everything. The tasks below distribute the
frontend into each story, because a story is only independently testable if it includes the screen
that exercises it. The claim change stays within US2 where it belongs, with one caveat kept from the
plan: T042-T047 edit the path every user of the product traverses, and they are the last thing that
should land regardless of which order the stories are built in.

**Building on features 002 and 003**: no new dependency, no new table, no new convention. This
feature is the consumer feature 003 published its contract for, and it extends the sign-in path
feature 002 built rather than adding one beside it.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1-US3, mapping to the spec's user stories
- Every task names the file or directory it touches

## Path Conventions

- **Backend**: `backend/src/Crm.Api/`, `Crm.Application/`, `Crm.Domain/`, `Crm.Infrastructure/`
- **Backend tests**: `backend/tests/Crm.UnitTests/`, `Crm.IntegrationTests/`, `Crm.ArchitectureTests/`
- **Frontend**: `frontend/projects/crm-web/`, `frontend/projects/core/`, `frontend/projects/ui/`

---

## Phase 1: Setup

**Purpose**: The constants, permissions, and strings the rest of the feature refers to.

- [X] T001 [P] Add the `Identity` group with `View` (`identity.view`) and `Manage`
      (`identity.manage`) to `backend/src/Crm.Application/Authorization/Permissions.cs`; the existing
      reflection scan discovers them, so no registration is needed
- [X] T002 [P] Add `identity_email_in_use`, `identity_last_administrator`, `identity_self_demotion`,
      `identity_placement_mismatch`, `identity_subject_collision`, `identity_email_not_verified`, and
      `identity_email_ambiguous` to `backend/src/Crm.Application/Common/ErrorCodes.cs`
- [X] T003 [P] Add `identity.*` keys and the seven new `errors.code.*` entries to
      `frontend/projects/crm-web/public/assets/i18n/en.json` and `ar.json`, keeping key parity green
      under `npm run i18n:check`
- [X] T004 [P] Add a unit test in `backend/tests/Crm.UnitTests/Authorization/PermissionCatalogTests.cs`
      asserting both new permissions are discovered by `Permissions.All`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The schema change and the store every story reads through. Nothing below Phase 2 can
start until the identity column can be null and the guards exist.

- [ ] T005 Make `ProviderSubject` nullable and add `Provider` in
      `backend/src/Crm.Domain/Identity/User.cs`, with a `BindIdentity(provider, subject)` method that
      refuses a row already bound (INV-5) and a `Place(branchId, department, team)` method that
      derives the department from the team
- [ ] T006 Replace the two plain unique indexes in
      `backend/src/Crm.Infrastructure/Persistence/Configurations/IdentityConfigurations.cs`: subject
      becomes `UNIQUE (Provider, ProviderSubject) WHERE ProviderSubject IS NOT NULL`, email becomes
      `UNIQUE (Email) WHERE IsDeleted = 0` (research decisions 1 and 2)
- [ ] T007 Create the migration with `dotnet ef migrations add IdentityAdministration --project
      backend/src/Crm.Infrastructure --startup-project backend/src/Crm.Api`
- [ ] T008 Review the generated migration in `backend/src/Crm.Infrastructure/Migrations/` by hand and
      confirm four things: the column widens to nullable rather than being dropped and recreated,
      both new indexes carry their filters, the `Provider` column is added without a default that
      would falsely claim existing rows came from an unknown issuer, and no data migration was
      generated
- [ ] T009 Add a seed migration in `backend/src/Crm.Infrastructure/Migrations/` granting
      `identity.view` and `identity.manage` to the seeded `Administrator` role, following the pattern
      in `20260826210055_IdentitySeed.cs`; the `Agent` role receives neither
- [ ] T010 [P] Define `IPeopleStore` in `backend/src/Crm.Application/Abstractions/IPeopleStore.cs`
      covering the paged read, the single read, the writes, the administrator count the guards need,
      and the session revocation the lifecycle needs
- [ ] T011 Implement it in `backend/src/Crm.Infrastructure/Identity/PeopleStore.cs`, relying on the
      global soft-delete query filter so no call site writes `WHERE IsDeleted = 0` by hand
- [ ] T012 [P] Add an integration test in
      `backend/tests/Crm.IntegrationTests/Identity/SchemaTests.cs` proving two people can exist with
      no bound identity at once - the case a plain unique index on a nullable column would reject,
      and the reason the filter exists
- [ ] T013 [P] Add an integration test in
      `backend/tests/Crm.IntegrationTests/Identity/SchemaTests.cs` proving a deleted person's email
      can be reused while a live duplicate is still refused by the database rather than only by a
      prior read
- [ ] T014 Add the administrator guards in
      `backend/src/Crm.Application/Identity/People/AdministratorGuard.cs`: never zero active
      administrators, and no self-demotion, with the count and the mutation inside one serializable
      transaction (research decision 5)
- [ ] T015 [P] Unit tests in `backend/tests/Crm.UnitTests/Identity/AdministratorGuardTests.cs` for
      both guards, including the case that matters - the guard refusing when the caller is the last
      administrator, and permitting when another remains

**Checkpoint**: A person can exist without an identity, and the rules that stop the system locking
itself out are in place and tested.

---

## Phase 3: User Story 1 - Give a person their access and their place (Priority: P1) 🎯 MVP

**Goal**: An administrator finds somebody who has signed in, grants them roles, and records where
they belong.

**Independent test**: Sign in as an administrator, find an existing person, grant a role and set a
placement, and confirm both are reflected on that person's next request.

### Tests for User Story 1

- [ ] T016 [P] [US1] Unit tests in
      `backend/tests/Crm.UnitTests/Identity/PlacementRulesTests.cs`: selecting a team derives that
      team's department; a request naming a different department is refused rather than corrected; a
      placement with no team accepts a department directly; clearing all three is allowed
- [ ] T017 [P] [US1] Unit tests in `backend/tests/Crm.UnitTests/Identity/RoleGrantTests.cs`: a person
      may hold several roles, effective permissions are the union, granting a held role changes
      nothing and does not fail
- [ ] T018 [P] [US1] Unit tests in `backend/tests/Crm.UnitTests/Identity/RoleGuardTests.cs`: revoking
      the last administrator's role is refused, and revoking one's own administrator role is refused
      even when others remain

### Implementation for User Story 1

- [ ] T019 [P] [US1] Add the read use cases in
      `backend/src/Crm.Application/Identity/People/`: list with search and the branch, department,
      team, active, and never-signed-in filters, and get one person with roles and effective
      permissions
- [ ] T020 [P] [US1] Add the role use cases in
      `backend/src/Crm.Application/Identity/People/GrantRole.cs` and `RevokeRole.cs`, both routed
      through the guards from T014
- [ ] T021 [US1] Add the placement use case in
      `backend/src/Crm.Application/Identity/People/SetPlacement.cs`, deriving the department from the
      team and refusing a mismatch (FR-010, FR-011)
- [ ] T022 [US1] Add `PeopleController` in `backend/src/Crm.Api/Identity/PeopleController.cs` with
      `[RequirePermission(Permissions.Identity.View)]` on reads and `Manage` on writes, Staff
      population only, matching `contracts/identity-api.yaml` exactly
- [ ] T023 [US1] Add `RolesController` in `backend/src/Crm.Api/Identity/RolesController.cs` for the
      read-only role list carrying each role's permissions, so the client can show effective
      permissions as derived
- [ ] T024 [US1] Map the placement, guard, and not-found refusals to the shared `ProblemDetails`
      contract with their `errorCode`s in `backend/src/Crm.Api/Identity/IdentityProblems.cs`
- [ ] T025 [US1] Record audit entries through `IAuditRecorder` in the use cases under
      `backend/src/Crm.Application/Identity/People/` for every grant, revocation, and placement
      change, capturing actor, person, and what changed (AR-005, AR-006)
- [ ] T026 [P] [US1] Integration tests in
      `backend/tests/Crm.IntegrationTests/Identity/PeopleEndpointsTests.cs` covering each endpoint,
      the paged list shape, and every filter
- [ ] T027 [P] [US1] Integration tests in
      `backend/tests/Crm.IntegrationTests/Identity/AuthorizationTests.cs`: a caller without
      `identity.view` is refused a read, a caller with view but not `manage` is refused every write,
      and a portal-population caller is refused everything (AR-003)
- [ ] T028 [P] [US1] Integration tests in
      `backend/tests/Crm.IntegrationTests/Identity/GuardTests.cs` proving both guards refuse through
      the endpoint, not only in the use case
- [ ] T029 [P] [US1] Add `identity-api.service.ts` in
      `frontend/projects/crm-web/src/app/features/identity/` as the only place `HttpClient` appears
      in this feature
- [ ] T030 [US1] Add `frontend/projects/crm-web/src/app/features/identity/people.page.ts` and its
      template: list, search, the filters, and all six UI states
- [ ] T031 [US1] Add `frontend/projects/crm-web/src/app/features/identity/person.page.ts` with three
      separated blocks - identity read-only, a roles checklist, and effective permissions rendered
      as derived and visually distinct from the roles above them
- [ ] T032 [US1] Add
      `frontend/projects/crm-web/src/app/features/identity/placement-form.component.ts` where
      choosing a team fills the department and disables it, and clearing the team makes it selectable
      again; choosers request `activeOnly=true`
- [ ] T033 [US1] On the acting administrator's own row, disable the administrator checkbox and the
      deactivate and delete controls with a stated reason rather than hiding them, in
      `frontend/projects/crm-web/src/app/features/identity/person.page.ts`
- [ ] T034 [US1] Add the People entry to the shell navigation in
      `frontend/projects/crm-web/src/app/app.ts`, visible only to a session holding `identity.view`,
      in the same change as the routes - SC-007 exists because feature 003 shipped a screen nothing
      linked to
- [ ] T035 [P] [US1] Frontend specs
      `frontend/projects/crm-web/src/app/features/identity/people.page.spec.ts`,
      `person.page.spec.ts`, `placement-form.component.spec.ts`, and `identity-api.service.spec.ts`
- [ ] T036 [US1] Remove `x-status: planned` from the paths this story implements in
      `specs/004-identity-administration/contracts/identity-api.yaml`, so the contract drift test
      begins holding them

**Checkpoint**: The organization structure feature 003 built is in use. This is the MVP.

---

## Phase 4: User Story 2 - Prepare someone before their first day (Priority: P2)

**Goal**: An administrator adds somebody by email before they have ever signed in, and that person's
first sign-in claims the preparation.

**Independent test**: Pre-provision an address, sign in as that person with a verified email, and
confirm the prepared roles and placement survive with no duplicate record.

### Tests for User Story 2

- [ ] T037 [P] [US2] Unit tests in `backend/tests/Crm.UnitTests/Identity/ClaimDecisionTests.cs`
      covering every row of the claim matrix in data-model.md: subject match, single unclaimed match
      with a verified email, unverified, absent claim, ambiguous match, already-bound collision, and
      no match at all
- [ ] T038 [P] [US2] Unit tests in `backend/tests/Crm.UnitTests/Identity/PreProvisionTests.cs`: an
      address belonging to a live person is refused, an address belonging to a deleted person is
      accepted, and a prepared person may carry roles and placement immediately

### Implementation for User Story 2

- [ ] T039 [US2] Add the pre-provision use case in
      `backend/src/Crm.Application/Identity/People/PreProvisionPerson.cs`, refusing an address
      already in use (FR-014)
- [ ] T040 [US2] Add the create endpoint to `backend/src/Crm.Api/Identity/PeopleController.cs` and
      record the creation through `IAuditRecorder`
- [ ] T041 [P] [US2] Integration tests in
      `backend/tests/Crm.IntegrationTests/Identity/PreProvisionEndpointTests.cs` covering creation
      with and without roles and placement, and the duplicate-address refusal
- [ ] T042 [US2] Add `EmailVerified` to `ProviderClaimNames` in
      `backend/src/Crm.Api/Configuration/CrmOptions.cs` and to
      `backend/src/Crm.Api/appsettings.Development.json`, so the assertion's name is configuration
      rather than a hard-coded Keycloak spelling (FR-021)
- [ ] T043 [US2] Add the claim decision in
      `backend/src/Crm.Application/Identity/Claiming/ClaimDecision.cs` as one function over the
      matrix, so every branch is visible in one place rather than spread through the sign-in path
- [ ] T044 [US2] Change `backend/src/Crm.Infrastructure/Identity/StaffSignIn.cs` from
      create-if-absent to match-then-create, binding provider and subject exactly once and never
      re-binding an established account (FR-015, FR-019)
- [ ] T045 [US2] Record the refused claim and the subject collision through `IAuditRecorder` in
      `backend/src/Crm.Application/Identity/Claiming/`, carrying the address involved and nothing
      else - no tokens, no claim dumps (AR-008, Constitution XI)
- [ ] T046 [P] [US2] Integration tests in
      `backend/tests/Crm.IntegrationTests/Identity/ClaimingTests.cs` driving the matrix end to end
      through a real sign-in, including the collision refusing the sign-in outright
- [ ] T047 [US2] Update the affected feature 002 tests in
      `backend/tests/Crm.IntegrationTests/Auth/` so they assert the new behaviour rather than the
      old: sign-in now matches before it creates, and an unclaimed record is claimed only on a
      verified address
- [ ] T048 [US2] Add
      `frontend/projects/crm-web/src/app/features/identity/pre-provision-form.component.ts`, the
      Invited badge in the list, and the never-signed-in filter
- [ ] T049 [P] [US2] Frontend spec
      `frontend/projects/crm-web/src/app/features/identity/pre-provision-form.component.spec.ts`,
      including the duplicate-address refusal
- [ ] T050 [US2] Remove `x-status: planned` from the create path in
      `specs/004-identity-administration/contracts/identity-api.yaml`

**Checkpoint**: Somebody can be ready before they arrive, and arriving cannot take over an account.

---

## Phase 5: User Story 3 - Take access away, completely (Priority: P3)

**Goal**: Deactivation and deletion end access at once, and deletion is indivisible.

**Independent test**: Deactivate a person holding an active session and confirm their next request
is refused; delete a person holding roles and confirm the roles are gone, the sessions ended, and
the audit records what they held.

### Tests for User Story 3

- [ ] T051 [P] [US3] Unit tests in `backend/tests/Crm.UnitTests/Identity/DeletionRulesTests.cs`:
      deleting the last administrator is refused, deleting oneself is refused, and the audit payload
      carries the roles held immediately beforehand
- [ ] T052 [P] [US3] Unit tests in `backend/tests/Crm.UnitTests/Identity/ActivationRulesTests.cs`:
      deactivating revokes sessions, deactivating oneself is refused, and reactivating restores
      neither roles nor sessions

### Implementation for User Story 3

- [ ] T053 [US3] Add the activation use case in
      `backend/src/Crm.Application/Identity/People/SetActivation.cs`, revoking every active session
      as part of deactivating (FR-023)
- [ ] T054 [US3] Add the delete use case in
      `backend/src/Crm.Application/Identity/People/DeletePerson.cs`: revoke every role, revoke every
      session, soft-delete the person, and record the roles held - all inside one explicit
      transaction so the operation succeeds or fails as a whole (FR-024)
- [ ] T055 [US3] In `backend/src/Crm.Application/Identity/People/DeletePerson.cs` and
      `SetActivation.cs`, do **not** use `ExecuteUpdateAsync` for the role or session revocation; it
      bypasses `AuditingSaveChangesInterceptor`, so the operation that most needs a trail would be
      the one that stops writing one (research decision 4, and feature 003's identical finding)
- [ ] T056 [US3] Add the activation and delete endpoints to
      `backend/src/Crm.Api/Identity/PeopleController.cs`, mapping both guard refusals to their
      `errorCode`s
- [ ] T057 [P] [US3] Integration test in
      `backend/tests/Crm.IntegrationTests/Identity/DeletionTests.cs` proving atomicity: a delete that
      fails partway leaves the person, their roles, and their sessions all unchanged
- [ ] T058 [P] [US3] Integration test in
      `backend/tests/Crm.IntegrationTests/Identity/SessionEndingTests.cs` proving a deactivated
      person's existing credential is refused on the very next request rather than at its expiry
      (SC-005)
- [ ] T059 [US3] Add the deactivate and delete controls to
      `frontend/projects/crm-web/src/app/features/identity/person.page.ts`, with confirmations that
      state plainly that sessions end immediately
- [ ] T060 [P] [US3] Frontend spec coverage for the lifecycle controls in
      `frontend/projects/crm-web/src/app/features/identity/person.page.spec.ts`, including both
      guard refusals reaching the reader
- [ ] T061 [US3] Remove `x-status: planned` from the activation and delete paths in
      `specs/004-identity-administration/contracts/identity-api.yaml`

**Checkpoint**: Access can be removed and is genuinely gone. All three stories complete.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T062 [P] Confirm the contract drift test in `backend/tests/Crm.IntegrationTests/Contracts`
      passes with no `x-status: planned` markers remaining in
      `specs/004-identity-administration/contracts/identity-api.yaml`, meaning every published path
      is implemented and nothing implemented is unpublished
- [ ] T063 [P] Add an integration test in
      `backend/tests/Crm.IntegrationTests/Identity/InvariantTests.cs` scanning for people whose
      department disagrees with their team's department after a sequence of placements, a team move,
      and a deletion; it must return zero rows (SC-002, INV-2)
- [ ] T064 [P] Assert in `backend/tests/Crm.UnitTests/Identity/ClaimAuditTests.cs` that a refused
      claim and a collision log the address and nothing more - no tokens, no claim dumps
      (Constitution XI)
- [ ] T065 [P] Verify RTL layout on both screens under
      `frontend/projects/crm-web/src/app/features/identity/` and keep `npm run i18n:check` and
      `npm run css:check` green, confirming the people list orders identically in both languages
      (LR-002) while unit names follow the reader's language (LR-003)
- [ ] T066 [P] Update `docs/getting-started.md` with how to prepare and place a person locally,
      following `quickstart.md`
- [ ] T067 [P] Add the feature's compliance record at
      `specs/004-identity-administration/compliance.md`, matching the shape of features 002 and 003
- [ ] T068 Run `./scripts/verify-backend.ps1` and `./scripts/verify-frontend.ps1` and confirm both
      pass; before the frontend script, check that nothing holds a lock on `node_modules` - a running
      `ng serve` or a stray `esbuild.exe` leaves the tree broken part-way through `npm ci`
- [ ] T069 Walk `specs/004-identity-administration/quickstart.md` by hand, including the claim
      matrix with Keycloak's verified-email flag turned off, the session-ending check, and the SQL
      invariant scan

---

## Dependencies & Execution Order

```text
Phase 1: Setup
    │
    ▼
Phase 2: Foundational  ◄── BLOCKS EVERYTHING BELOW
(nullable identity, filtered indexes, store, guards)
    │
    ▼
Phase 3: US1 ◄── MVP
(read, roles, placement, screens)
    │
    ├──────────────┬───────────────┐
    ▼              ▼               │
Phase 4: US2   Phase 5: US3        │  US2 and US3 are independent of each
(prepare and   (deactivate and     │  other; both need US1's screens and
 claim)         delete)            │  the guards from Phase 2
    │              │               │
    └──────┬───────┘               │
           ▼                       │
Phase 6: Polish ◄──────────────────┘
```

**Story independence**: US1 must come first - it builds the list and detail screens the other two
extend, and there is nothing to prepare somebody *for* until placement exists. US2 and US3 are
independent of each other and can be built by two people at once.

**One ordering constraint that crosses the graph**: T042-T047 change sign-in, the one path every
user traverses. Whatever order the stories are built in, those tasks should land last, so a
regression there cannot be confused with a regression anywhere else. This is the plan's stage G
reasoning, preserved.

**Within a phase**: tasks marked `[P]` touch different files and may run together. Unmarked tasks in
the same phase are sequential, usually because they edit a file an earlier task creates - every
task touching `PeopleController.cs` is unmarked for this reason.

## Parallel Execution Examples

**Phase 1**: all four tasks touch different files and run together.

**Phase 2**: T010 waits for nothing and T012, T013, and T015 are independent of one another. T006
waits for T005 because the configuration describes the entity, and T007 waits for T006 because the
migration is generated from it.

**Phase 3**: the three test tasks T016-T018 run together. T019 and T020 run together once the store
exists; T021 follows T019 only where they share the placement reader. T026-T028 run together at the
end, as do T029 and T035.

**Phases 4 and 5 together**: with two people, one takes US2 and the other US3 as soon as US1 lands.
They meet again at Phase 6, with the caveat above about landing the sign-in change last.

## Implementation Strategy

**MVP**: Phases 1, 2, and 3. That delivers the administration surface and puts people into the
structure feature 003 built - the point of both features, and demonstrable on its own.

**Increment 2**: Phase 5 (US3), not Phase 4. Deliberately out of priority order as a suggestion:
being able to remove access completely is worth more in production than preparing people in advance,
and it carries none of the sign-in risk. Build US2 second if pre-provisioning is the reason this
feature was scheduled.

**Increment 3**: Phase 4, ending with the claim change - the last thing built, whichever order the
rest arrives in.

**Close-out**: Phase 6. T043-T047 should be reviewed as one change rather than task by task, because
their correctness is a property of the whole: which sign-ins claim, which refuse, and which quietly
create somebody new.

## Notes

- **69 tasks**: 4 setup, 11 foundational, 21 for US1, 14 for US2, 11 for US3, 8 polish.
- **T055 is a prohibition rather than an action**, listed as a task because the efficient thing to
  write is the wrong one and nothing in the code will say so. Feature 003 carried the identical task
  for the identical reason.
- **T008 is a manual review**, not an automated check. A migration that widens a column can be
  generated as a drop-and-recreate, which on `User` would be a data-loss operation dressed as a
  schema change.
- **Every contract path starts marked `planned`** and is unmarked by the story that implements it -
  T036, T050, and T061. T062 confirms none remain.
- **Two tasks exist because feature 003 went wrong there**: T034 adds navigation in the same change
  as the routes, and T035 covers every screen, because 003 shipped a complete screen that nothing
  linked to and no test covered.
