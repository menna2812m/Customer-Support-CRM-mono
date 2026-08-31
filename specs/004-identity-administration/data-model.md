# Data Model: Identity Administration

**Feature**: 004-identity-administration | **Date**: 2026-08-30

No new tables. One existing table changes shape, two of its indexes change meaning, and one
existing join table is used for the first time by an administrator rather than by a seed.

---

## User (changed)

The person. Already carries identity, activation, placement, and audit columns.

| Field | Change | Notes |
|-------|--------|-------|
| `ProviderSubject` | **nullable** | Null until the identity is bound. Null is what "prepared but never signed in" means; there is no separate status column, because two sources of truth for the same fact can disagree |
| `Provider` | **new** | Which identity provider issued the subject. Null while the subject is null; set with it, permanently, in the same operation |
| `Email` | unchanged | Stored normalized, as today. The one attribute that may match a person before their identity exists, and only once |
| `DisplayName`, `Population` | unchanged | Provider-owned; reflected, never edited here (FR-004) |
| `IsActive` | unchanged | Enable/disable. Orthogonal to whether a subject is bound: a prepared person may be deactivated before they ever arrive |
| `DepartmentId`, `BranchId`, `TeamId` | unchanged | Foreign keys added by feature 003. This feature is what finally writes them |
| audit + soft-delete columns | unchanged | Maintained by the interceptor |

### Indexes

| Index | Before | After | Why |
|-------|--------|-------|-----|
| Subject | `UNIQUE (ProviderSubject)` | `UNIQUE (Provider, ProviderSubject) WHERE ProviderSubject IS NOT NULL` | Identity is provider-plus-subject (FR-015a), and many prepared people have no subject at all. SQL Server permits only one `NULL` in a unique index, so the filter is required, not tidy |
| Email | `UNIQUE (Email)` | `UNIQUE (Email) WHERE IsDeleted = 0` | A deleted person must release their address (FR-026) while a live one must not be duplicated (FR-014) |

### Derived states

Not stored. Computed from two columns that cannot disagree with themselves:

| State | Condition | Meaning |
|-------|-----------|---------|
| **Invited** | `ProviderSubject IS NULL` | Prepared, never signed in. Claimable |
| **Active** | subject bound and `IsActive` | Ordinary |
| **Inactive** | `IsActive = 0` | Deactivated; sessions revoked. May be either bound or invited |

---

## RoleAssignment (unchanged)

Composite key `(UserId, RoleId)` - already exactly right for this feature, and worth stating
because it is easy to assume otherwise. The composite permits a person to hold many roles while the
database itself refuses the same role twice, so FR-008's idempotence is a property of the schema
rather than of a check that could be forgotten.

`GrantedAt` and `GrantedBy` record the grant. **There is no revocation history**: revoking deletes
the row. That single fact is why FR-025 puts the roles held into the deletion's audit entry - it is
not redundancy, it is the only copy.

---

## Session (unchanged)

Used, not altered. `RevokedAt` with its filtered index is the seam through which FR-023 and FR-024
end access, and the reason they can promise immediacy: token validation already resolves the session
on every request, so revocation lands on the next one rather than at credential expiry.

---

## Invariants

| # | Invariant | Enforced |
|---|-----------|----------|
| INV-1 | A bound identity is unique per provider | Filtered composite unique index |
| INV-2 | `TeamId IS NOT NULL` ⇒ `DepartmentId = Team.DepartmentId` | Derived on write here; resynced on team move by feature 003. Scanned by SC-002 because neither half is sufficient alone |
| INV-3 | A live email address belongs to at most one person | Filtered unique index |
| INV-4 | At least one active, non-deleted person holds the administrator role | Application guard inside a serializable transaction. The only invariant here not backed by the schema, because no constraint can express it |
| INV-5 | A bound subject is never re-bound | No write path sets `ProviderSubject` on a row that already has one |
| INV-6 | Placement references only active units at the time it is set | Validated on write; existing placements survive later deactivation |

---

## State transitions

```text
                    pre-provision (email)
                              │
                              ▼
                        [ Invited ]───────────┐
                              │               │
        first sign-in,        │               │ deactivate / delete
        verified email,       │               │ (no identity ever bound)
        exactly one match     │               ▼
                              │        [ Inactive / Deleted ]
                              ▼
                        [ Active ]◄──────────┐
                              │              │
              deactivate      │              │ reactivate
              (sessions ended)▼              │
                       [ Inactive ]──────────┘
                              │
                       delete │ (roles revoked, sessions ended,
                              ▼  roles recorded in the audit entry)
                        [ Deleted ]
                              │
                      restore │ (no roles return - FR-027)
                              ▼
                        [ Inactive ]
```

A first sign-in that finds no claimable record creates an **Active** person directly, as today. A
sign-in whose email belongs to an already-bound person with a different subject makes no transition
at all: it is refused and recorded (FR-018).

---

## The claim decision

The one piece of logic in this feature that is a decision rather than a rule, stated as a table
because its failure mode is silent and a reader needs to see every branch at once.

| Subject match | Email matches | Verified | Outcome |
|---------------|---------------|----------|---------|
| yes | - | - | Returning person; sign in unchanged |
| no | one unclaimed | yes | **Claim**: bind provider and subject, keep prepared roles and placement, audit the claim |
| no | one unclaimed | no or absent | No claim. **Refuse sign-in**; audit the refusal (`identity_email_not_verified`) |
| no | more than one unclaimed | any | No claim. **Refuse sign-in**; audit the ambiguity (`identity_email_ambiguous`) |
| no | one already bound | any | **Refuse sign-in**; audit the collision (`identity_subject_collision`) |
| no | none | - | Create an ordinary person, as today |

Every row that does not claim either creates a plain person or refuses outright. Nothing partially
claims, and no row modifies a record that is already bound.

The two middle rows refuse rather than creating a person beside the one they declined to claim,
which is a correction to what FR-017 first said. The address is not available: it belongs to the
unclaimed record, and `UNIQUE (Email) WHERE IsDeleted = 0` is what makes that true rather than
intended. A second live row simply cannot be written, so refusal is the only reachable outcome -
and the one that tells the administrator their preparation went unused.

The ambiguous row is unreachable through this schema for the same reason: at most one live person
can hold an address, so "more than one unclaimed match" cannot be produced today. It stays in the
decision, and in the tests, because the decision is a pure function over what it is handed and the
branch costs nothing - and because the day that index filter changes, the branch is already right.
