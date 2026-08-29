---

description: "Task list for Organization Structure (003-organization)"
---

# Tasks: Organization Structure

**Input**: Design documents from `/specs/003-organization/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Required. Constitution Principle XIII applies as it did in features 001 and 002:
business rules, authorization rules, and validation failures MUST have tests, and critical Angular
workflows MUST have frontend tests. The rules worth testing here are concentrated in three places -
uniqueness under soft deletion, refusing a delete that has dependents, and the team move that has to
carry its members - and each is written before the code that satisfies it.

**Organization**: Grouped by user story so each can be implemented and verified independently.

**A deliberate departure from the plan's stage table**: the plan groups all frontend work into a
single Stage F. The tasks below distribute it into each user story instead, because a story is only
independently testable if it includes the screen that exercises it. The plan's stages remain
accurate as a description of the backend build order.

**Building on features 001 and 002**: this feature adds no dependency and invents no convention. It
is the first real consumer of `PageRequest`/`PagedResult`, it follows the `diagnostics` slice for
list screens, and it closes the foreign-key exception feature 002 recorded against Constitution VIII.

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

- [ ] T001 [P] Add the `Organization` group with `View` (`organization.view`) and `Manage` (`organization.manage`) to `backend/src/Crm.Application/Authorization/Permissions.cs`; the existing reflection scan discovers them, so no registration is needed
- [ ] T002 [P] Add `organization.code_conflict`, `organization.name_conflict`, `organization.has_dependents`, and `organization.department_inactive` to `backend/src/Crm.Application/Common/ErrorCodes.cs`
- [ ] T003 [P] Add `organization.*` keys and the four new `errors.code.*` entries to `frontend/projects/crm-web/public/assets/i18n/en.json` and `ar.json`, keeping key parity green under `npm run i18n:check`
- [ ] T004 [P] Add a unit test in `backend/tests/Crm.UnitTests/Authorization/PermissionCatalogTests.cs` asserting both new permissions are discovered by `Permissions.All`

**Checkpoint**: The catalog knows the two permissions and the application still builds.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Schema, entities, and the store. **Every user story depends on this phase**; nothing
below can start until the migration applies cleanly.

- [ ] T005 Create the abstract `OrganizationUnit` base in `backend/src/Crm.Domain/Organization/OrganizationUnit.cs`: `NameAr`, `NameEn`, `Code` with no setter after construction (INV-3), `IsActive`, `Activate()`, `Deactivate()`, `Rename(nameAr, nameEn)`, implementing `IAuditableEntity` and `ISoftDeletable`
- [ ] T006 [P] Create `Branch` in `backend/src/Crm.Domain/Organization/Branch.cs` with a `Create` factory; it belongs to nothing and contains nothing (FR-003)
- [ ] T007 [P] Create `Department` in `backend/src/Crm.Domain/Organization/Department.cs` with a `Create` factory and a `Teams` collection
- [ ] T008 Create `Team` in `backend/src/Crm.Domain/Organization/Team.cs` with a required `DepartmentId` (INV-1) and a `MoveTo(Department)` method that refuses an inactive destination (FR-016)
- [ ] T009 [P] Write unit tests in `backend/tests/Crm.UnitTests/Organization/OrganizationUnitTests.cs` for the entity rules: names are trimmed on write, a code cannot be changed after construction, and `MoveTo` refuses an inactive department
- [ ] T010 [P] Add entity configurations in `backend/src/Crm.Infrastructure/Persistence/Configurations/` for all three, each with **filtered** unique indexes (`WHERE [IsDeleted] = 0`) per data-model.md: code per kind, names per kind for branch and department, names per `(DepartmentId, Name*)` for team
- [ ] T011 Add `Branches`, `Departments`, and `Teams` `DbSet`s to `backend/src/Crm.Infrastructure/Persistence/CrmDbContext.cs`
- [ ] T012 Configure the three foreign keys from `User.DepartmentId`, `User.BranchId`, and `User.TeamId` with `ON DELETE NO ACTION` in the existing user configuration, closing feature 002's recorded Constitution VIII exception
- [ ] T013 Create the migration with `dotnet ef migrations add Organization --project backend/src/Crm.Infrastructure --startup-project backend/src/Crm.Api`
- [ ] T014 Review the generated migration by hand and confirm four things: three separate tables rather than a TPH hierarchy from the shared base class, the unique indexes carry their `WHERE [IsDeleted] = 0` filter, the three foreign keys are present, and no data migration was generated (every placement column is null)
- [ ] T015 Add a seed migration granting `organization.view` and `organization.manage` to the seeded `Administrator` role, following the pattern in `20260826210055_IdentitySeed.cs`; the `Agent` role receives neither
- [ ] T016 [P] Define `IOrganizationStore` in `backend/src/Crm.Application/Abstractions/IOrganizationStore.cs` covering the reads, the writes, and the dependent counts the delete rules need
- [ ] T017 Implement it in `backend/src/Crm.Infrastructure/Organization/OrganizationStore.cs`, relying on the global soft-delete query filter so no call site writes `WHERE IsDeleted = 0` by hand
- [ ] T018 [P] Add an integration test in `backend/tests/Crm.IntegrationTests/Organization/SchemaTests.cs` proving uniqueness survives soft deletion: create a unit, delete it, recreate one with the same code, and confirm it is accepted (this is the behaviour FR-006 depends on)
- [ ] T019 [P] Add an integration test in `backend/tests/Crm.IntegrationTests/Organization/SchemaTests.cs` asserting a duplicate code is refused by the database rather than only by a prior read, by inserting concurrently

**Checkpoint**: A migrated database with three tables, three foreign keys, filtered unique indexes,
and an Administrator who holds both permissions. Nothing is reachable over HTTP yet.

---

## Phase 3: User Story 1 - Maintain departments and their teams (Priority: P1) 🎯 MVP

**Goal**: An administrator can build the functional structure of the business - departments and the
teams inside them - and correct or retire either.

**Independent test**: Create a department, add two teams, rename one, deactivate the other. The list
shows the department with its remaining active team, and the deactivated team is absent from the
active-only listing.

### Tests for this story

- [ ] T020 [P] [US1] Unit tests in `backend/tests/Crm.UnitTests/Organization/DepartmentRulesTests.cs`: duplicate code refused ignoring case and surrounding whitespace, duplicate department name refused, both names required
- [ ] T021 [P] [US1] Unit tests in `backend/tests/Crm.UnitTests/Organization/TeamRulesTests.cs`: a team name may repeat across departments but not within one, and a team cannot exist without a department
- [ ] T022 [P] [US1] Unit tests in `backend/tests/Crm.UnitTests/Organization/DeletionRulesTests.cs`: deleting a department with live teams is refused and the refusal names them; deleting one with people placed in it is refused and the refusal counts them; deleting one with neither succeeds

### Implementation for this story

- [ ] T023 [P] [US1] Add the department use cases in `backend/src/Crm.Application/Organization/Departments/`: create, rename, set activation, delete, get, and list
- [ ] T024 [P] [US1] Add the team use cases in `backend/src/Crm.Application/Organization/Teams/`: create within a department, rename, set activation, delete, get, and list by department
- [ ] T025 [P] [US1] Add FluentValidation validators in `backend/src/Crm.Application/Organization/Validators/` for the create and rename requests: both names required, lengths per data-model.md, code required and within length on create and absent on rename
- [ ] T026 [US1] Add `DepartmentsController` in `backend/src/Crm.Api/Organization/DepartmentsController.cs` with `[RequirePermission(Permissions.Organization.View)]` on reads and `Manage` on writes, Staff population only, matching `contracts/organization-api.yaml` exactly
- [ ] T027 [US1] Add `TeamsController` in `backend/src/Crm.Api/Organization/TeamsController.cs` for the department-scoped create and list plus the team-addressed get, rename, activation, and delete
- [ ] T028 [US1] Map the four conflict cases to the shared `ProblemDetails` contract with their `errorCode`s, so the client can translate a refusal rather than display a sentence from the server
- [ ] T029 [US1] Record an audit entry through `IAuditRecorder` for every mutation - create, rename, activation change, delete - capturing actor, unit, and what changed (AR-005)
- [ ] T030 [P] [US1] Integration tests in `backend/tests/Crm.IntegrationTests/Organization/DepartmentEndpointsTests.cs` covering each endpoint, including the paged list shape and the `activeOnly` filter
- [ ] T031 [P] [US1] Integration tests for authorization: a caller without `organization.view` is refused a read, a caller with view but not `manage` is refused every write, and a portal-population caller is refused everything (AR-003)
- [ ] T032 [P] [US1] Integration tests in `backend/tests/Crm.IntegrationTests/Organization/ValidationTests.cs` for validation failures: a blank Arabic name, a page size over the maximum, and a rename attempting to change the code

### Frontend for this story

- [ ] T033 [P] [US1] Add `organization-api.service.ts` in `frontend/projects/crm-web/src/app/features/organization/` as the only place `HttpClient` appears in this feature
- [ ] T034 [US1] Add `frontend/projects/crm-web/src/app/features/organization/departments.page.ts` and its template, following the `diagnostics` reference slice, with all six UI states: loading, empty, success, validation error, authorization failure, server failure
- [ ] T035 [US1] Add `frontend/projects/crm-web/src/app/features/organization/department-form.component.ts` with both names on one form so a unit cannot be half-translated (LR-003), and the code field present on create and absent on edit
- [ ] T036 [US1] Show a department's teams within `frontend/projects/crm-web/src/app/features/organization/department-detail.page.ts`, so creating a team never begins with an empty department dropdown - the containment rule made visible
- [ ] T037 [US1] In `frontend/projects/crm-web/src/app/features/organization/departments.page.ts` sort by the name matching the active language rather than always by English (LR-002), and verify the hierarchy indents from the correct side under RTL using logical properties only
- [ ] T038 [P] [US1] Frontend specs `frontend/projects/crm-web/src/app/features/organization/departments.page.spec.ts`, `department-form.component.spec.ts`, and `organization-api.service.spec.ts`
- [ ] T039 [US1] Add the Organization entry to the shell navigation, visible only to a session holding `organization.view` - shaping the experience, never the boundary

**Checkpoint**: Departments and teams are fully manageable. This is the MVP and is demonstrable to a
stakeholder, even though nobody can yet be placed in what it creates.

---

## Phase 4: User Story 2 - Maintain branches (Priority: P2)

**Goal**: An administrator records the geographic branches the organization operates from.

**Independent test**: Create three branches, rename one, deactivate another; the list reflects all
three states and only active branches appear in the active-only listing.

Branches have no containment rule, so this phase is a smaller repeat of Phase 3 and depends on none
of its code beyond the foundational store.

- [ ] T040 [P] [US2] Unit tests in `backend/tests/Crm.UnitTests/Organization/BranchRulesTests.cs`: duplicate code and duplicate name refused; delete refused while people are placed in the branch, and the refusal counts them
- [ ] T041 [P] [US2] Add the branch use cases in `backend/src/Crm.Application/Organization/Branches/`: create, rename, set activation, delete, get, and list
- [ ] T042 [US2] Add `BranchesController` in `backend/src/Crm.Api/Organization/BranchesController.cs`, permissioned as Phase 3 and matching the contract
- [ ] T043 [US2] Record audit entries through `IAuditRecorder` for every branch mutation in `backend/src/Crm.Application/Organization/Branches/` (AR-005)
- [ ] T044 [P] [US2] Integration tests in `backend/tests/Crm.IntegrationTests/Organization/BranchEndpointsTests.cs` covering the endpoints, the authorization rules, and the delete refusal
- [ ] T045 [US2] Add `frontend/projects/crm-web/src/app/features/organization/branches.page.ts` and `frontend/projects/crm-web/src/app/features/organization/branch-form.component.ts`, six UI states, both names on one form
- [ ] T046 [P] [US2] Frontend specs `frontend/projects/crm-web/src/app/features/organization/branches.page.spec.ts` and `frontend/projects/crm-web/src/app/features/organization/branch-form.component.spec.ts`

**Checkpoint**: All three kinds of unit are manageable. The structure feature 004 needs is complete.

---

## Phase 5: User Story 3 - Reorganize without corrupting placement (Priority: P3)

**Goal**: Moving a team to another department carries its members with it.

**Independent test**: With people placed in a team, move the team and confirm every member's
recorded department follows it.

This is the feature's one real invariant and the thing it is most likely to get wrong.

### Tests for this story

- [ ] T047 [P] [US3] Unit tests in `backend/tests/Crm.UnitTests/Organization/TeamMoveTests.cs`: members are reassigned, a move into an inactive department is refused (FR-016), a move into a department already holding a team of that name is refused, and a move to the current department succeeds while changing nothing
- [ ] T048 [P] [US3] A unit test in `backend/tests/Crm.UnitTests/Organization/TeamMoveTests.cs` asserting the audit entry carries both departments and the affected count (AR-006)

### Implementation for this story

- [ ] T049 [US3] Add the move use case in `backend/src/Crm.Application/Organization/Teams/MoveTeam.cs`: update the team, load and reassign its members through the domain, and record one audit entry - all inside one explicit transaction so the move succeeds or fails as a whole (FR-015)
- [ ] T050 [US3] Do **not** use `ExecuteUpdateAsync` for the member reassignment; it bypasses `AuditingSaveChangesInterceptor`, so `UpdatedAt` and `UpdatedBy` would stop being written by the one operation that most needs a trail (research decision 2, and the plan's Complexity Tracking)
- [ ] T051 [US3] Add the move endpoint to `TeamsController`, returning `membersReassigned` as the contract specifies
- [ ] T052 [P] [US3] Integration test in `backend/tests/Crm.IntegrationTests/Organization/TeamMoveTests.cs` that places users on a team, moves it, and then scans for violations of INV-2 - a user whose department disagrees with their team's department. The scan must return zero rows (SC-003)
- [ ] T053 [P] [US3] Integration test in `backend/tests/Crm.IntegrationTests/Organization/TeamMoveTests.cs` proving atomicity: a move that fails partway leaves both the team and every member unchanged
- [ ] T054 [US3] Add `frontend/projects/crm-web/src/app/features/organization/move-team.dialog.ts` and its template, an explicit action rather than an editable field, showing how many people will be affected before confirming
- [ ] T055 [P] [US3] Frontend spec `frontend/projects/crm-web/src/app/features/organization/move-team.dialog.spec.ts` for the move dialog, including the refusal messages

**Checkpoint**: The organization can be reorganized without corrupting anyone's placement.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Retire the provider's placement claims, and close the feature out.

Retirement is sequenced last deliberately: it is the only work here that edits code feature 002
shipped, so a failure in it cannot be confused with a failure in the new feature.

- [ ] T056 Remove the department, branch, and team members from `ProviderIdentity` in `backend/src/Crm.Application/Abstractions/IIdentityProviderClient.cs`
- [ ] T057 Remove `ReadGuidClaim` and the three placement claim reads from `backend/src/Crm.Infrastructure/Identity/OpenIdConnectClient.cs`, leaving subject, name, and email untouched (FR-019)
- [ ] T058 Remove `Department`, `Branch`, and `Team` from `ProviderClaimNames` in `backend/src/Crm.Api/Configuration/CrmOptions.cs`
- [ ] T059 Remove the placement branch of `RefreshFromProvider` in `backend/src/Crm.Domain/Identity/User.cs`, and update its comment, which currently promises to preserve a value for "the organization feature" that has now arrived
- [ ] T060 Remove `ReadPlacement` and its call site from `backend/src/Crm.Application/Identity/StaffSignIn.cs`
- [ ] T061 Remove the three claim-name keys from `backend/src/Crm.Api/appsettings.Development.json` and any other settings file that carries them
- [ ] T062 Update the affected feature 002 tests in `backend/tests/Crm.UnitTests/Identity/` and `backend/tests/Crm.IntegrationTests/Auth/` so they assert the new behaviour rather than the old: sign-in no longer writes placement, and a provider that asserts it is ignored (spec edge case, SC-005)
- [ ] T063 Confirm the CRM's **own** placement claims are untouched - `TokenIssuer` still writes `crm_department`, `crm_branch`, and `crm_team` from the session identity, and `ICurrentUser.Scope` is unchanged in shape and meaning
- [ ] T064 [P] Confirm the contract drift test in `backend/tests/Crm.IntegrationTests/Contracts` passes, meaning every implemented endpoint is published in `contracts/organization-api.yaml` and nothing published is unimplemented
- [ ] T065 [P] Update `docs/getting-started.md` with how to build a structure locally, following `quickstart.md`
- [ ] T066 [P] Add the feature's compliance record at `specs/003-organization/compliance.md`, matching the shape of feature 002's
- [ ] T067 Run `./scripts/verify-backend.ps1` and `./scripts/verify-frontend.ps1` and confirm both pass; stop `ng serve` before the frontend script, because its `npm ci` corrupts `node_modules` under file locks
- [ ] T068 Walk `quickstart.md` by hand, including the Keycloak claim-mapper check that proves the retirement, and the SQL invariant scan

---

## Dependencies & Execution Order

```text
Phase 1: Setup
    │
    ▼
Phase 2: Foundational  ◄── BLOCKS EVERYTHING BELOW
    │
    ├──────────────┬───────────────┐
    ▼              ▼               │
Phase 3: US1   Phase 4: US2        │   US1 and US2 are independent of
(departments   (branches)          │   each other and may run in parallel
 and teams)                        │
    │                              │
    ▼                              │
Phase 5: US3 ◄─────────────────────┘
(team move - needs US1's teams)
    │
    ▼
Phase 6: Polish and retirement
```

**Story independence**: US1 and US2 share only the foundational store and can be built by two people
at once. US3 depends on US1, because it moves the teams US1 creates. Nothing in Phase 6 is needed by
any story, which is why it is last.

**Within a phase**: tasks marked `[P]` touch different files and may run together. Unmarked tasks in
the same phase are sequential, usually because they edit a file an earlier task creates.

## Parallel Execution Examples

**Phase 2**: T006 and T007 (two entity files) run together; T009, T010, T016, T018, and T019 are
each independent of one another. T008 waits for T007, because a team references a department.

**Phase 3**: the three test tasks T020-T022 run together, as do the use-case tasks T023-T025 once
the store exists. T026 and T027 are sequential only where they share the controller conventions.
T030-T032 and T038 run together at the end.

**Phases 3 and 4 together**: with two people, one takes US1 and the other US2 as soon as Phase 2
lands. They meet again at Phase 5.

## Implementation Strategy

**MVP**: Phases 1, 2, and 3. That delivers departments and teams - the half of the model with the
containment rule and the design risk - and is demonstrable on its own.

**Increment 2**: Phase 4 adds branches, completing the structure feature 004 consumes.

**Increment 3**: Phase 5 adds the move. It can be deferred, but not past the start of feature 004:
the moment anyone can be placed in a team, a move without resync silently corrupts their placement.

**Close-out**: Phase 6. The retirement in T056-T063 should be reviewed as one change rather than
task by task, because its correctness is a property of the whole - what was removed, and what was
deliberately left alone.

## Notes

- **68 tasks**: 4 setup, 15 foundational, 20 for US1, 7 for US2, 9 for US3, 13 polish.
- **T050 is a prohibition rather than an action.** It is listed as a task because the efficient
  alternative is the obvious thing to reach for, and the damage it does - silently stopping audit
  columns on the one operation that most needs them - would not show up in any test that was not
  looking for it.
- **T014 is a manual review of generated output.** The shared abstract base class is the risk: EF
  Core maps three separate tables here because no `DbSet<OrganizationUnit>` exists, but that is a
  property of the configuration rather than a guarantee, and a TPH hierarchy would be discovered
  late and cost a migration to undo.
- This feature ships no capability an end user notices. It is finished when feature 004 can place a
  person in a real unit, which is the standard the checkpoints are written against.
