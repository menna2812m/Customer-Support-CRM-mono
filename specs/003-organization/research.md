# Phase 0 Research: Organization Structure

**Feature**: 003-organization | **Date**: 2026-08-30

The specification left no `NEEDS CLARIFICATION` markers - the clarification session resolved all
nine. What remains are technical questions the spec deliberately does not answer, because they are
implementation choices rather than product decisions. Six of them are recorded here.

---

## 1. Uniqueness under soft deletion

**Decision**: Filtered unique indexes that exclude soft-deleted rows -
`WHERE [IsDeleted] = 0` - one per uniqueness rule.

**Rationale**: FR-006 makes codes unique per kind and FR-005 makes department and branch names
unique per kind, while FR-011 keeps deleted rows in the table forever. A plain unique index would
therefore make a code unusable for the lifetime of the database as soon as one unit carrying it is
deleted, which contradicts the spec's own remedy for a mistyped code: delete the unit and recreate
it (FR-006). SQL Server's filtered indexes express exactly this, are enforced by the database rather
than by a prior read, and so survive the concurrent-creation edge case the spec calls out.

Case- and whitespace-insensitivity is handled by storing codes and names already trimmed, and by
relying on the database's case-insensitive default collation for comparison. Normalizing on write
rather than comparing case-insensitively on read keeps the index usable as an index.

**Alternatives considered**:

- *Plain unique index*: rejected because it leaks deleted rows into the namespace of live ones, and
  the spec explicitly expects codes to be reusable after a mistake is deleted.
- *Uniqueness enforced only in application code*: rejected because the spec's edge case requires two
  administrators creating the same code simultaneously to produce a refusal, and a read-then-write
  check cannot guarantee that.
- *A computed normalized column plus a unique index on it*: equivalent in effect but adds a column
  per rule for no benefit over trimming on write.

---

## 2. Moving a team without bypassing the audit trail

**Decision**: Load the affected users and update them through the domain, inside one explicit
transaction with the team's own change. Record a single audit entry for the move with the affected
count in its metadata.

**Rationale**: FR-015 requires the team move and every member update to succeed or fail as a whole,
and AR-006 requires the move to record both departments and how many people were affected. EF Core's
`ExecuteUpdateAsync` would be the efficient way to reassign members, but it issues SQL directly and
therefore bypasses both the change tracker and the `AuditingSaveChangesInterceptor` that maintains
`UpdatedAt` and `UpdatedBy`. Those columns would silently stop reflecting reality for exactly the
operation that most needs an audit trail.

The spec's own scale assumption makes the efficient path unnecessary: the structure is tens of units
maintained by hand, so a team's membership is tens of rows at most. Loading them is cheap, and it
keeps one code path - the interceptor - responsible for audit columns everywhere.

**Alternatives considered**:

- *`ExecuteUpdateAsync` for the member reassignment*: rejected as above. It would need the audit
  columns set by hand in the same statement, duplicating the interceptor's logic in one place and
  creating a second definition of what "updated" means.
- *A database trigger keeping department in step with team*: rejected outright. It moves a business
  rule into the schema where no test can see it, and Constitution I puts rules in Domain.
- *Recording one audit entry per affected user*: rejected as noise. The move is one administrative
  act; AR-006 asks for the count, not a row per person.

---

## 3. Foreign keys that feature 002 deliberately deferred

**Decision**: Add foreign keys from `User.DepartmentId`, `User.BranchId`, and `User.TeamId` to the
three new tables in this feature's migration, with `ON DELETE NO ACTION`.

**Rationale**: This is a debt feature 002 recorded against itself. Its Complexity Tracking entry
reads: "the organization feature adds the constraints when it adds the tables." The tables now
exist, so the constraints follow. No data migration is needed - every user's placement columns are
null today, because nothing has ever been able to populate them.

`NO ACTION` rather than a cascade is the deliberate choice: FR-012 already refuses to delete a unit
that has people placed in it, so the constraint exists to catch a bug, not to implement the rule.
A cascade would quietly null out placements the application intended to protect.

**Alternatives considered**:

- *Leaving the columns unconstrained*: rejected. Constitution VIII requires foreign keys, 002 was
  granted a temporary exception on the explicit basis that this feature would close it, and without
  the constraint FR-017's invariant depends entirely on application code being correct.
- *`ON DELETE SET NULL`*: rejected because it turns a refused operation into a silent data change,
  and placement is not something to lose by accident.

---

## 4. Bilingual names

**Decision**: Two columns per unit - `NameAr` and `NameEn` - both required, rather than a related
translations table.

**Rationale**: The set of languages is fixed at two by Constitution VII and is not configuration; a
translations table models a variable set. Two columns keep every read a single-table query, make
"both are required" a `NOT NULL` constraint rather than an application rule, and let the
name-uniqueness indexes of decision 1 be ordinary indexes on ordinary columns. If a third language
ever arrives it will be a schema change, which is the honest cost of a feature that does not exist.

Sorting (LR-002) is done by ordering on the column matching the request's language, chosen in the
query rather than by sorting in memory, so it composes with paging. Arabic ordering uses the
database's default collation; no special collation is introduced, because a collation choice affects
every string in the database and belongs to a decision about the database, not to this feature.

**Alternatives considered**:

- *A `UnitName(UnitId, Language, Value)` table*: rejected as modelling variability that does not
  exist, at the cost of a join on every list and a much harder uniqueness rule.
- *One JSON column holding both names*: rejected - it defeats indexing, and the uniqueness rules
  need indexes.

---

## 5. What "retiring the provider's placement claims" actually removes

**Decision**: Remove the reading of placement from the provider, and nothing else. The CRM keeps
issuing its own placement claims from stored data.

The distinction matters because both are called "claims". Concretely:

| Removed | Kept |
|---------|------|
| `ProviderIdentity`'s department, branch, and team fields | `CrmClaimNames.DepartmentId/BranchId/TeamId` in the CRM's own token |
| `ReadGuidClaim` in `OpenIdConnectClient` | `TokenIssuer` writing those claims from the session identity |
| `ProviderClaimNames.Department/Branch/Team` and their `appsettings` entries | `ProviderClaimNames.Subject/Name/Email` |
| The placement branch of `User.RefreshFromProvider` | Everything else `RefreshFromProvider` does: email, display name |
| `StaffSignIn.ReadPlacement` | `ICurrentUser.Scope`, unchanged in shape and meaning |

**Rationale**: FR-018 stops the provider overwriting placement; FR-019 requires the rest of sign-in
to be untouched. `ICurrentUser.Scope` keeps working and keeps meaning the same thing - it simply
sources from the user record, which is now the only writer. Nothing downstream of the token changes,
which is what keeps this from being a breaking change to feature 002's contract.

The provider asserting CRM primary keys was never workable in practice - it required Keycloak to
know the CRM's Guids - which is why placement is null for every user today. Removing it costs
nothing that ever worked.

**Alternatives considered**:

- *Leaving the claim mapping configured but unread*: rejected. Configuration that is read by nothing
  is a trap for the next person, and the spec's edge case requires an asserting provider to be
  ignored rather than merely inactive by default.
- *Keeping the provider authoritative and matching by code*: this was clarification question 3 and
  the answer was no. Recorded here only because the `Code` field this feature adds would make it
  cheap to revisit as its own feature later.

---

## 6. Frontend shape

**Decision**: One `features/organization/` folder with three list-and-form pairs - departments,
branches, teams - reached from one section of the navigation. Teams are managed from within their
department rather than as a top-level list.

**Rationale**: Constitution VI puts feature code under one folder, and the three entities are one
administrative job. Managing teams from inside their department is what makes FR-002's containment
visible in the interface: a team is created in the context of the department it will belong to, so
the required department is never an empty dropdown the user must remember to fill. The team *move*
(User Story 3) is the one place a team is addressed outside its department, and it is an explicit
action rather than an edit of a field.

The existing `diagnostics` feature is the reference slice for the list pattern - it is the only
place in the codebase that consumes `PagedResult` end to end - so the list screens follow it rather
than inventing a second convention.

**Alternatives considered**:

- *Three sibling top-level lists*: rejected because it hides the containment rule and makes team
  creation start with a dropdown of departments.
- *A single tree view of the whole organization*: rejected as more UI than tens of units justify,
  and it fits branches poorly, since they are not part of the tree at all.

---

## Resolved unknowns

No `NEEDS CLARIFICATION` markers remain in the Technical Context. The nine product-level questions
were resolved in the spec's clarification sessions on 2026-08-30; the six technical questions above
are resolved here.
