# Phase 1 Data Model: Organization Structure

**Feature**: 003-organization | **Date**: 2026-08-30

Three new tables, three foreign keys added to an existing one, and one migration. All three entities
derive from `Entity` and implement `IAuditableEntity` and `ISoftDeletable`, so the auditing
interceptor and soft-delete query filter from feature 001 apply without new code.

---

## Shared shape

Every organizational unit carries the same core, which is why the three entities differ only in
their relationships:

| Column | Type | Notes |
|--------|------|-------|
| `Id` | `uniqueidentifier` | Primary key, from `Entity` |
| `NameAr` | `nvarchar(200)` | Required. Arabic name (FR-005) |
| `NameEn` | `nvarchar(200)` | Required. English name (FR-005) |
| `Code` | `nvarchar(32)` | Required. Immutable after creation (FR-006) |
| `IsActive` | `bit` | Defaults true. Deactivation is not deletion (FR-009) |
| `CreatedAt` / `CreatedBy` | `datetimeoffset` / `uniqueidentifier?` | Audit columns via interceptor |
| `UpdatedAt` / `UpdatedBy` | `datetimeoffset?` / `uniqueidentifier?` | Audit columns via interceptor |
| `IsDeleted` / `DeletedAt` / `DeletedBy` | `bit` / `datetimeoffset?` / `uniqueidentifier?` | Soft deletion (FR-011) |

Names and codes are trimmed on write. Comparison relies on the database's case-insensitive default
collation rather than on `LOWER()` in queries, so the uniqueness indexes remain usable as indexes
(research decision 1).

`Code` has no setter after construction. Immutability is a property of the entity, not a rule the
application remembers to apply, so no use case can violate FR-006 by accident.

---

## Branch

Geography. Belongs to nothing and contains nothing (FR-003).

**Indexes**

| Index | Definition | Enforces |
|-------|------------|----------|
| `IX_Branch_Code` | unique on `Code`, `WHERE IsDeleted = 0` | FR-006 |
| `IX_Branch_NameAr` | unique on `NameAr`, `WHERE IsDeleted = 0` | FR-005 |
| `IX_Branch_NameEn` | unique on `NameEn`, `WHERE IsDeleted = 0` | FR-005 |

---

## Department

A functional division. Contains teams.

**Indexes**: identical to Branch - unique `Code`, `NameAr`, `NameEn`, each filtered on
`IsDeleted = 0`.

**Relationships**: one-to-many with `Team`. A department with any live team cannot be deleted
(FR-012).

---

## Team

Belongs to exactly one department (FR-002).

| Column | Type | Notes |
|--------|------|-------|
| *(shared shape above)* | | |
| `DepartmentId` | `uniqueidentifier` | **Required.** FK to `Department.Id`, `ON DELETE NO ACTION` |

**Indexes**

| Index | Definition | Enforces |
|-------|------------|----------|
| `IX_Team_Code` | unique on `Code`, `WHERE IsDeleted = 0` | FR-006 - codes are unique across all teams |
| `IX_Team_Department_NameAr` | unique on (`DepartmentId`, `NameAr`), `WHERE IsDeleted = 0` | FR-005 - names unique **within** a department |
| `IX_Team_Department_NameEn` | unique on (`DepartmentId`, `NameEn`), `WHERE IsDeleted = 0` | FR-005 |
| `IX_Team_DepartmentId` | non-unique | Listing a department's teams |

The asymmetry between the code index and the name indexes is deliberate and is the clarified
decision: a code identifies a team globally, while a team name is only ever read under its
department, so `Tier 1` may exist under several.

---

## User (existing - changed)

No new columns. Three constraints that feature 002 deferred:

| Constraint | Definition |
|------------|------------|
| `FK_User_Department` | `User.DepartmentId` → `Department.Id`, `ON DELETE NO ACTION` |
| `FK_User_Branch` | `User.BranchId` → `Branch.Id`, `ON DELETE NO ACTION` |
| `FK_User_Team` | `User.TeamId` → `Team.Id`, `ON DELETE NO ACTION` |

All three columns remain nullable - a person need not be placed. No data migration is required
because every one of them is null in every environment: nothing has ever been able to write them.

`NO ACTION` is chosen over a cascade because FR-012 already refuses to delete a unit with people in
it. The constraint is there to catch a bug, not to implement the rule; a cascade would quietly null
placements the application intended to protect (research decision 3).

---

## Invariants

**INV-1 (FR-002)**: every team has a department. Enforced by a non-nullable foreign key.

**INV-2 (FR-017)**: if a user has a team, their department equals that team's department. A user may
have a department with no team. Not expressible as a database constraint without denormalizing the
department onto the same row it already lives on, so it is enforced in the Domain at both write
sites - placing a person (feature 004) and moving a team (this feature, FR-015) - and asserted by a
test that scans for violations after a move (SC-003).

**INV-3 (FR-006)**: a code never changes. Enforced by the entity exposing no way to change it.

---

## State transitions

```text
                 create
                    │
                    ▼
              ┌──────────┐   deactivate    ┌────────────┐
              │  Active  │ ──────────────► │  Inactive  │
              │          │ ◄────────────── │            │
              └──────────┘    reactivate   └────────────┘
                    │                            │
                    └──────────┬─────────────────┘
                               │ delete
                               │ (refused if dependents exist - FR-012)
                               ▼
                         ┌──────────┐
                         │ Deleted  │  terminal - no restore interface (FR-011)
                         └──────────┘
```

Deactivation and deletion are different operations for different needs: deactivation retires a unit
that still has history, deletion removes one created in error. Deleted is terminal by decision - the
row and its audit trail survive, but no screen brings it back.

A deactivated unit is excluded from the active-units listing that a placement chooser consumes
(FR-009), and people already placed in it keep their placement (FR-010).

---

## The team move (FR-015)

The only operation in this feature that writes to more than one table.

```text
move(team, destinationDepartment):
  refuse if destination is inactive                     FR-016
  refuse if destination already has a team of this name FR-005
  accept as a no-op if destination is the current department

  begin transaction
    team.DepartmentId := destination
    for each user where TeamId = team.Id:
        user.DepartmentId := destination                FR-015, restoring INV-2
    record audit: team, both departments, affected count  AR-006
  commit                                                FR-015 - all or nothing
```

Members are loaded and updated through the domain rather than by a set-based statement, so the
auditing interceptor keeps maintaining `UpdatedAt` and `UpdatedBy` on the affected users. The
reasoning, and the cost, are in research decision 2 and in the plan's Complexity Tracking.

---

## Dependent rules for deletion (FR-012)

| Unit | Refused when |
|------|--------------|
| Department | any live team belongs to it, **or** any user has it as their department |
| Branch | any user has it as their branch |
| Team | any user has it as their team |

"Live" excludes soft-deleted rows throughout, which the global query filter from feature 001
provides without a `WHERE` clause at each call site. The refusal names what depends on the unit - the
dependent teams, or the number of people affected - because a refusal that does not say why is a
refusal the administrator cannot act on.

---

## Permissions added

Two constants in `Crm.Application.Authorization.Permissions`, discovered by the existing reflection
scan and therefore requiring no registration:

| Permission | Guards |
|------------|--------|
| `organization.view` | every read (AR-001) |
| `organization.manage` | create, rename, activate, deactivate, delete, move (AR-002) |

Both are granted to the seeded `Administrator` role by the same migration, in the pattern feature
002's `IdentitySeed` established. The `Agent` role receives neither: maintaining the organization is
not day-to-day work.
