# Research: Identity Administration

**Feature**: 004-identity-administration | **Date**: 2026-08-30

Seven decisions. Two are about making a uniqueness rule mean what the specification says it means,
two are about an operation being indivisible, and the rest are about not building something the
product already has.

---

## 1. A person's identity is unique per provider, and invitations have no subject yet

**Decision**: Replace the plain unique index on `ProviderSubject` with a composite filtered unique
index on `(Provider, ProviderSubject) WHERE ProviderSubject IS NOT NULL`. `ProviderSubject` becomes
nullable and gains a `Provider` companion column.

**Rationale**: Two requirements collide on this one index. FR-015a says the identity is the provider
together with the subject, so two providers may legitimately issue the same subject string to
different people. FR-013 says a pre-provisioned person exists before any subject does. Making the
column nullable is necessary but nowhere near sufficient: SQL Server treats `NULL` as a value in a
unique index and permits exactly one row to hold it, so the second invitation ever created would be
rejected by a constraint that looks correct. The filter is what makes many unclaimed records legal
while keeping every real identity unique. This is the same instrument feature 003 used for codes and
names, applied to a different problem, which is a point in its favour: one pattern the team already
reads fluently.

**Alternatives considered**: A sentinel empty string instead of `NULL`, which permits a plain unique
index but only by inventing a magic value that means "absent" and then having to exclude it
everywhere anyway - the filter moves the exception into the schema where it is enforced rather than
remembered. A separate `Invitation` table, rejected during design because it duplicates placement and
roles into a second shape that must agree with the first.

---

## 2. Deleting a person frees their email address

**Decision**: Change the unique index on `Email` to `WHERE IsDeleted = 0`, comparing the normalized
form the product already stores.

**Rationale**: FR-026 requires a deleted person's address to become available, and FR-014 requires
pre-provisioning to refuse an address already in use. Today's unfiltered index makes the first
impossible: a soft-deleted row occupies the address permanently, so a record created in error by
typo is unfixable by the obvious remedy of deleting it and adding it again. Feature 003 met exactly
this problem with codes and answered it the same way, and answering it differently here would leave
the product with two rules about what deletion frees.

**Alternatives considered**: Blanking the address on delete, which frees the index but destroys the
audit trail's ability to say who was removed - unacceptable under Principle VIII. Leaving the index
unfiltered and telling administrators to use a different address, which is not a remedy.

---

## 3. Access already ends immediately; nothing needs inventing

**Decision**: Satisfy FR-023 and FR-024 by revoking the person's sessions. Add no new mechanism.

**Rationale**: The obvious worry is that a self-contained access credential stays valid until it
expires, which would make "immediately" mean "within fifteen minutes"
(`AccessCredentialMinutes: 15`). It does not. Feature 002's token validation already resolves the
session claim on every request and checks it against the session store
(`AuthenticationSetup.cs`, `IsActiveAsync`). A revoked session therefore stops working on the very
next request, and `Session.RevokedAt` with its filtered index is the seam to do it through. This
research existed to find out whether SC-005 was achievable as written or needed the specification
weakened; the answer is that the platform already supports it, and the finding is recorded because
the opposite conclusion was the likely one.

**Alternatives considered**: Shortening the credential lifetime, which weakens "immediately" into
"soon" and taxes every request in the product to solve a problem in one screen. A revocation list
checked per request, which is what the session store already is.

---

## 4. Delete is one transaction, and the audit is the only record of what was revoked

**Decision**: Perform role revocation, session revocation, the soft delete, and the audit write
inside one explicit transaction, using tracked entities rather than set-based operations. The audit
payload carries the roles held immediately beforehand.

**Rationale**: FR-024 makes deletion indivisible for a specific reason: a partial failure that
removed the person from the lists but left their role assignments standing would leave a deleted
person holding administrator, which is exactly the state the never-zero-administrators rule exists to
prevent, reached by a route that rule does not watch. The audit requirement is not
belt-and-braces either - `RoleAssignment` has `GrantedAt` and `GrantedBy` but no revocation history,
so revoking a role deletes the only row that ever recorded it. If the audit entry does not carry
those roles, FR-025's history simply does not exist anywhere. Feature 003 already established why
this must use tracked entities: `ExecuteUpdateAsync` bypasses `AuditingSaveChangesInterceptor`, so
the operation most needing a trail would be the one that stopped writing one.

**Alternatives considered**: Soft-deleting the person and leaving assignments in place, which is
simpler and leaves the dangerous state above. A `RevokedAt` column on `RoleAssignment` to give
revocation its own history, which is a better long-term model and is deferred to the feature that
administers roles themselves - adding it here would widen this feature into that one.

---

## 5. The never-zero-administrators rule needs a transaction, not just a check

**Decision**: Evaluate the guard and perform the mutation inside one serializable transaction.

**Rationale**: The rule is a read-then-write against a count, which is the classic shape that a plain
check gets wrong under concurrency. Two administrators demoting each other at the same instant each
read a count of two, each conclude they are not the last, and the system arrives at zero through two
individually valid operations. Feature 003 met the analogous problem with uniqueness and solved it
in the database, with an index that refuses the second write regardless of what either transaction
read. No index can express "at least one row must remain", so the equivalent guarantee has to come
from isolation instead. The cost is negligible and worth naming: these are single-figure-row tables
touched by single-figure administrators, so serializable contention is theoretical, while the
failure it prevents is a product nobody can administer without a database edit.

**Alternatives considered**: Checking without a transaction, which is correct in testing and wrong in
production exactly once. Optimistic concurrency on the user row, which detects a conflicting edit to
*that* row but not the arrival of a second transaction demoting a *different* administrator - the
rows do not overlap, so nothing collides.

---

## 6. Claiming happens in sign-in, and the verified-email claim is configuration

**Decision**: Extend the existing sign-in path from create-if-absent to match-then-create, and read
the verified-email assertion through a configurable claim name alongside the existing subject, name,
and email mappings.

**Rationale**: FR-015 to FR-020 describe an ordering - subject first, then a single verified email
match, then ordinary creation - and the only place that ordering can live is where the identity
arrives. Making the claim name configurable follows the shape already there: feature 003 emptied
`ProviderClaimNames` down to subject, name, and email, and this adds one member back rather than
hard-coding a Keycloak spelling into a product that states its provider is configurable. The
practical consequence worth planning around is that this is the one change in the feature touching a
path every user traverses, which is why it is sequenced last and why feature 002's existing sign-in
tests are updated rather than added beside.

**Alternatives considered**: A separate claim step after sign-in, which leaves a window where the
person exists twice. Trusting the email without the verified assertion, which the specification
rules out and which would let anyone who can authenticate any address inherit whatever was prepared
for it.

---

## 7. Placement is one operation, and the department is derived rather than accepted

**Decision**: A single placement operation takes branch, department, and team together. When a team
is present the department is taken from that team; a department that disagrees is refused rather
than overwritten.

**Rationale**: FR-010 and FR-011 want two things that sound contradictory - the department should be
derived, and a mismatch should be refused - and both are satisfied by deriving on write while
validating the input. Refusing rather than silently correcting matters because the two behaviours are
indistinguishable when the client is right and very different when it is wrong: a caller that sends a
stale department alongside a team has a bug, and quietly storing something it did not ask for hides
it. Keeping placement in one operation rather than three fields of a general update keeps the
invariant in one place; feature 003 learned the same lesson from the opposite direction, where the
team move is the single operation that carries the rule.

**Alternatives considered**: Accepting only a team or a department and inferring the rest, which
makes the wire format ambiguous about clearing a value. Three independent operations, which allows a
sequence of individually valid calls to pass through a state violating INV-2 between them.
