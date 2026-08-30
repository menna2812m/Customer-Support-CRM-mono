# Contracts: Identity Administration

| File | Scope | Binding on |
|------|-------|------------|
| [identity-api.yaml](./identity-api.yaml) | The nine operations this feature adds, across seven paths | This feature |

One contract file. Like feature 003, this feature consumes conventions rather than adding them - the
pagination contract, the ProblemDetails shape, and the permission declaration are all used as built.

## Everything here is marked `x-status: planned`

Every path carries the marker, and the contract-drift test reads it. The test compares the live
OpenAPI document against every `*.yaml` under `specs/` in both directions, so a path published here
without the marker would fail the suite the moment this file is committed - before a single endpoint
exists.

Removing a path's marker is therefore part of implementing it, not an afterthought. That is the
mechanism keeping this document honest: a contract that documented endpoints nobody built would
otherwise drift silently, and this feature publishes its whole surface up front while delivering it
across three user stories.

## Two shapes worth explaining

**`status` is derived, not stored.** A person is `invited`, `active`, or `inactive`, computed from
whether an identity is bound and whether they are enabled. It is exposed as one field because that
is what a list column needs, and it is computed rather than persisted because two columns already
carry the truth - a stored status could disagree with them, and would.

**`effectivePermissions` sits beside `roles` rather than replacing them.** The roles are the input,
the permissions are the consequence, and the interface shows both because showing only permissions
would invite someone to try editing one. There are no deny or override semantics: a role only ever
adds, so the union is the whole rule.

## What this contract does not offer, deliberately

- **No role or permission editing.** `GET /roles` is read-only. Defining authority is a later
  feature; this one grants it.
- **No user creation with credentials.** Passwords and recovery belong to the identity provider.
  `POST /people` prepares a record, it does not create an account anybody can sign in to.
- **No bulk operations.** People are added one at a time.
- **No restore endpoint.** Deletion is recorded rather than destructive so history survives, but
  bringing somebody back is not a surface this feature builds.

## Relationship to feature 003

This is the consumer feature 003 wrote its contract for. Two things it published are used here
exactly as intended:

- **`activeOnly=true`** on the unit lists drives the placement choosers, so this feature never
  filters inactive units for itself (FR-012).
- **`TeamResponse` carrying its department** is what makes deriving a person's department from their
  team a single call rather than two.

The `organization_department_inactive` code appears in this contract's error enum for the same
reason: placement into an inactive unit is refused by a rule feature 003 already defined, and
restating the code here keeps a client from having to guess which feature owns it.
