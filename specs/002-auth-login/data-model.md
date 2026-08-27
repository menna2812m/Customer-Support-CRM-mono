# Phase 1 Data Model: Authentication and Login

**Feature**: 002-auth-login | **Date**: 2026-08-26

The first real schema in the product. Feature 001 established the conventions - `Entity<TId>`,
`IAuditableEntity`, `ISoftDeletable`, `IHasOrganizationScope`, bounded strings, restrict-by-default
deletes, and an auditing interceptor that stamps every write. Everything below inherits them; where
a table departs from a convention, the reason is stated.

---

## Entities

### `User`

The CRM's own record of a person who can sign in. Created on first successful sign-in (FR-004).

| Field | Type | Rules |
|---|---|---|
| `Id` | `Guid` | Primary key, sequential. This is the identifier every other feature references |
| `ProviderSubject` | `string(200)` | The provider's stable subject. **Unique.** The only key used to recognise a returning user |
| `Email` | `string(256)` | Normalized lower-case. **Unique.** A conflict is refused and escalated, never resolved automatically (FR-005) |
| `DisplayName` | `string(200)` | Refreshed from the provider on each sign-in |
| `Population` | `int` | `Staff` or `Portal`. Only `Staff` is written by this feature |
| `IsActive` | `bool` | Default true. An inactive user is refused even when the provider authenticates them (FR-006) |
| `DepartmentId`, `BranchId`, `TeamId` | `Guid?` | Organizational placement, resolved per FR-025. No foreign key yet - the organization feature owns those tables and does not exist |
| `LastSignedInAt` | `DateTimeOffset?` | Convenience for support questions; not authoritative for anything |

Implements `IAuditableEntity`, `ISoftDeletable`, `IHasOrganizationScope`. Indexed on
`ProviderSubject` (unique), `Email` (unique), and `IsActive`.

**On the missing foreign keys**: Constitution VIII wants referential integrity, and placement
columns without a foreign key are a knowing exception. The alternative - inventing department and
branch tables here - would put the organization feature's schema in the wrong feature and force it
to migrate away from a guess. The columns are nullable, they are only ever written from a verified
claim or an existing value, and the organization feature adds the constraints when it adds the
tables.

### `Role`

A named set of permissions. Seeded; not editable through any interface in this feature (FR-020).

| Field | Type | Rules |
|---|---|---|
| `Id` | `Guid` | Primary key |
| `Name` | `string(100)` | **Unique.** For example `Administrator`, `Agent`, `ReadOnly` |
| `Description` | `string(500)` | What the role is for, in English, for administrators |
| `IsSystem` | `bool` | System roles are seeded and may not be deleted by a later administration screen |

### `RolePermission`

Which permissions a role grants. Values come from the code-declared catalog in feature 001.

| Field | Type | Rules |
|---|---|---|
| `RoleId` | `Guid` | Part of composite key; restrict on delete |
| `Permission` | `string(100)` | Part of composite key. Must exist in the catalog - a value that does not is reported at startup, not ignored (FR-018) |

### `RoleAssignment`

Which roles a user holds. Effective permissions are the union (FR-019).

| Field | Type | Rules |
|---|---|---|
| `UserId` | `Guid` | Part of composite key |
| `RoleId` | `Guid` | Part of composite key |
| `GrantedAt` | `DateTimeOffset` | From the clock provider |
| `GrantedBy` | `Guid?` | Null when granted by deployment or by the default-role rule; the audit record names which |

### `Session`

One established sign-in (FR-011). Revoked, never deleted.

| Field | Type | Rules |
|---|---|---|
| `Id` | `Guid` | Primary key. Appears in audit records and in the access credential |
| `UserId` | `Guid` | Foreign key, restrict |
| `CreatedAt` | `DateTimeOffset` | Start of the absolute lifetime |
| `LastActivityAt` | `DateTimeOffset` | Advanced on each renewal; drives the inactivity limit |
| `AbsoluteExpiresAt` | `DateTimeOffset` | `CreatedAt` plus the configured maximum; re-authentication required past it regardless of activity |
| `RevokedAt` | `DateTimeOffset?` | Set on sign-out, on reuse detection, or when the user is deactivated |
| `RevokedReason` | `string(100)?` | `signed_out`, `signed_out_everywhere`, `credential_reused`, `user_deactivated`, `password_of_record_changed` |
| `ClientDescription` | `string(200)?` | Coarse user-agent summary so a person recognises their own sessions. Not a fingerprint |
| `IpAddressAtCreation` | `string(45)?` | For the audit trail |

Indexed on `UserId` and on `RevokedAt` (filtered), because "this user's live sessions" is the query
sign-out-everywhere and deactivation both run.

### `RenewalCredential`

The single-use value that extends a session (FR-012). Rotated on every renewal.

| Field | Type | Rules |
|---|---|---|
| `Id` | `Guid` | Primary key |
| `SessionId` | `Guid` | Foreign key, restrict |
| `TokenHash` | `string(200)` | **A hash, never the value.** A database leak must not hand over live sessions |
| `ExpiresAt` | `DateTimeOffset` | Independent of the session's own expiry |
| `UsedAt` | `DateTimeOffset?` | Set when spent. Presenting a spent credential revokes the whole session |
| `ReplacedById` | `Guid?` | The credential issued in its place - the rotation chain, useful when investigating a reuse |

### `AuthenticationEvent`

The audit trail for authentication specifically (FR-035). Written through `IAuditRecorder` and also
persisted here, so a security question can be answered without reading application log files.

| Field | Type | Rules |
|---|---|---|
| `Id` | `Guid` | Primary key |
| `OccurredAt` | `DateTimeOffset` | From the clock provider |
| `Action` | `string(100)` | `sign_in.succeeded`, `sign_in.refused`, `sign_in.collision`, `session.renewed`, `session.revoked`, `credential.reused`, `role.granted` |
| `Outcome` | `string(50)` | `succeeded` or `refused` |
| `UserId` | `Guid?` | Null when no user could be resolved - a refused sign-in still gets a record |
| `SubjectReference` | `string(200)?` | The provider subject or email referenced by the attempt, so a refusal is investigable |
| `SessionId` | `Guid?` | When applicable |
| `IpAddress` | `string(45)?` | Source |
| `CorrelationId` | `string(128)` | Ties the event to the request logs |
| `Detail` | `string(500)?` | Human-readable context. **Never** a credential, token, or hash |

Indexed on `UserId`, `OccurredAt`, and `Action`.

---

## Relationships

```text
User 1───* RoleAssignment *───1 Role 1───* RolePermission
User 1───* Session 1───* RenewalCredential
User 1───* AuthenticationEvent   (optional: a refused attempt has no user)
```

Every delete is restrict. Sessions and credentials are revoked or marked used; users are
deactivated. Nothing in this schema is hard deleted, which is what Constitution VIII asks of records
that carry history.

---

## Seeded data

Applied by migration (FR-020), so a deployment has something to grant:

| Role | Permissions |
|---|---|
| `Administrator` | Every permission in the catalog, resolved at seed time from the registry |
| `Agent` | `customers.view`, `customers.create`, `customers.update`, `tickets.view`, `tickets.create` |
| `ReadOnly` | `customers.view`, `tickets.view`, `reports.view` |

`Administrator` resolving from the registry rather than a written list means a permission added to
the catalog later is not silently missing from the administrator role.

---

## State transitions

**Session**: `active → revoked`. One direction, several triggers (sign-out, sign out everywhere,
reuse detected, user deactivated). There is no "suspended" state - a session is usable or it is not.

**Renewal credential**: `issued → used` (normal rotation) or `issued → expired` (time). Presenting a
credential already in `used` is not a state change on that row; it revokes the session and records
`credential.reused`.

**User**: `active ⇄ inactive`. Deactivation revokes live sessions; reactivation grants nothing back
by itself - roles are unchanged, sessions are not restored.

---

## Validation rules carried from the specification

| Rule | Source |
|---|---|
| Email normalized lower-case and trimmed before comparison | FR-004, and the collision rule depends on it |
| A new subject whose email exists on another user is refused | FR-005 |
| An inactive user is refused even on a valid provider assertion | FR-006 |
| A role permission absent from the catalog is reported at startup | FR-018 |
| The default role applies only when the user has no assignment yet | FR-023 |
| Placement written only from a verified claim or an existing stored value | FR-025, AR-005 |
| A spent renewal credential revokes its session | FR-012 |
