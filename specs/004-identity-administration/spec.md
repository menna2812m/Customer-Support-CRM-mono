# Feature Specification: Identity Administration

**Feature Branch**: `004-identity-administration`
**Created**: 2026-08-30
**Status**: Draft
**Input**: User description: "Administer people and their placement: pre-provision users by email,
administer roles as a multi-select with derived effective permissions, and place people in a branch,
department, or team where selecting a team derives the department. First sign-in claims a
pre-provisioned record only when the identity provider asserts a verified email; provider and
subject become the canonical identity thereafter."

Feature 003 built departments, branches, and teams and then deliberately shipped no way to put
anybody in them. The structure is real, correct, and unused. This feature is what makes it matter:
an administrator can find a person, decide what they may do, and say where in the organization they
belong.

It also closes the gap feature 002 left at the other end. Today a person exists in the CRM only
after they have signed in for the first time, which means the moment a new hire most needs their
access already arranged is the one moment nobody can arrange it. Here, an administrator can prepare
someone by email before their first day, and their first sign-in claims that preparation rather than
starting from nothing.

## Clarifications

### Session 2026-08-30

- Q: How much of the administration surface belongs here? → A: People. Administering users and
  placing them. Creating roles and editing the permissions behind them is a later feature, because
  defining authority is a different act from granting it and carries its own lockout risks.
- Q: Can an administrator set someone up before that person has ever signed in? → A: Yes.
  Pre-provisioning by email, so a new hire's roles and placement are ready on day one. The
  pre-provisioned record is claimed by the real identity at first sign-in.
- Q: How is placement expressed, given that a team already implies a department? → A: Selecting a
  team derives the department, which is then not independently editable. With no team, the
  department is chosen directly. The rule is enforced on the server regardless of what the interface
  allows, rather than letting an invalid combination be submitted and refused afterwards.
- Q: What stops an administrator removing their own access, or everyone's? → A: Two rules. No change
  may leave the system without an active administrator, and no administrator may demote, deactivate,
  or delete themselves. Someone else must do it.
- Q: May a person hold more than one role? → A: Yes. Effective permissions are the union of the
  roles held. There are no deny or override semantics; a role only ever adds.
- Q: On what basis may a first sign-in claim a pre-provisioned record? → A: A verified email, and
  nothing weaker. If the provider does not assert that the email is verified, or if more than one
  pre-provisioned record matches, the claim fails closed and an ordinary new account is created
  instead. Email is a one-time bootstrap; after binding, the provider and subject are the identity.
- Q: What happens when the email belongs to somebody already bound to a different subject? → A:
  Sign-in is refused and the collision is recorded for a person to resolve. Re-binding an established
  account from an email would be an account takeover with extra steps.
- Q: What does deleting a person actually do? → A: One indivisible operation: revoke every role,
  end every active session, record the deletion together with the roles held immediately beforehand,
  and remove the person from the lists. A half-completed delete that left a removed person holding
  administrator is the failure this must not have.
- Q: Does restoring a deleted person restore what they had? → A: No. Access is re-granted
  deliberately, never resurrected. The audit record says what they held; it is not an undo buffer.
- Q: Does deactivating end sessions too? → A: Yes, immediately. Access that ends at the next renewal
  is access that has not ended.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Give a person their access and their place (Priority: P1)

An administrator opens the list of people, finds someone who has signed in, grants them the roles
their job needs, and records which branch they work from and which department or team they work in.

**Why this priority**: This is the capability feature 003 was built to enable, and the smallest
slice that makes the organization structure worth having. Without it the structure stays empty and
no permission decision can be recorded anywhere.

**Independent Test**: Sign in as an administrator, find an existing person, grant a role and set a
placement, and confirm both are reflected on that person's next request. Delivers a working
administration surface with no pre-provisioning and no deletion.

**Acceptance Scenarios**:

1. **Given** a person holding no roles, **When** an administrator grants them a role, **Then** the
   person's effective permissions include that role's permissions and the change is audited.
2. **Given** a person with no placement, **When** an administrator selects a team, **Then** the
   person's department is recorded as that team's department without being chosen separately.
3. **Given** a person placed in a team, **When** an administrator clears the team and selects a
   department directly, **Then** the department stands alone and no team is recorded.
4. **Given** a deactivated department, **When** an administrator opens the placement chooser,
   **Then** that department is not offered.
5. **Given** an administrator viewing their own record, **When** they attempt to remove their own
   administrator role, **Then** the attempt is refused and the reason names the rule.

---

### User Story 2 - Prepare someone before their first day (Priority: P2)

An administrator adds a person by email address before that person has ever signed in, giving them
roles and a placement in advance. When the person signs in for the first time, they arrive with
everything already arranged.

**Why this priority**: Valuable but not required for the administration surface to work. It removes
the gap between a person's first sign-in and their first useful minute, and it is the reason a
person can exist in the CRM before the identity provider has ever mentioned them.

**Independent Test**: Pre-provision an address, sign in as that person for the first time with a
verified email, and confirm the roles and placement set in advance survive the sign-in and that no
second record was created.

**Acceptance Scenarios**:

1. **Given** a pre-provisioned person who has never signed in, **When** they sign in with a verified
   email, **Then** their prepared roles and placement are retained and the record is bound to their
   provider identity.
2. **Given** a pre-provisioned person, **When** someone signs in with that email and the provider
   does not assert it is verified, **Then** the preparation is left untouched and an ordinary new
   account is created instead.
3. **Given** two pre-provisioned records somehow matching one address, **When** a first sign-in
   occurs, **Then** neither is claimed and the ambiguity is recorded.
4. **Given** an address already belonging to a person bound to a different provider subject,
   **When** a sign-in presents that address, **Then** sign-in is refused and the collision is
   recorded for manual resolution.
5. **Given** an address already belonging to somebody who has signed in, **When** an administrator
   tries to pre-provision it, **Then** the attempt is refused rather than silently merged.

---

### User Story 3 - Take access away, completely (Priority: P3)

An administrator deactivates a person who has left, or deletes a record created in error, and can
rely on that person's access ending at once rather than at some later moment.

**Why this priority**: Needed before the feature can be trusted in production, but the administration
surface is demonstrable without it, and its rules only matter once people have been given access by
the earlier stories.

**Independent Test**: Deactivate a person holding an active session and confirm their next request is
refused; delete a person holding roles and confirm the roles are gone, the sessions are ended, and
the audit records what they held.

**Acceptance Scenarios**:

1. **Given** a person with an active session, **When** an administrator deactivates them, **Then**
   their existing session stops working immediately rather than at its next renewal.
2. **Given** a person holding roles, **When** an administrator deletes them, **Then** the roles are
   revoked, the sessions ended, and the audit entry records the roles held immediately beforehand.
3. **Given** the only remaining active administrator, **When** anyone attempts to deactivate,
   delete, or demote them, **Then** the attempt is refused and the system keeps at least one.
4. **Given** a deleted person, **When** their email address is used to pre-provision somebody new,
   **Then** it is accepted, because the address is free again.
5. **Given** a deleted person who is later restored, **When** their record returns, **Then** it
   carries no roles and access must be granted again deliberately.

### Edge Cases

- A person is placed in a team, and feature 003 then moves that team to another department. The
  person's department follows the team, which is the behaviour feature 003 already guarantees.
- A person is placed in a department, and an administrator then tries to delete that department.
  Feature 003 already refuses while people are placed there; this feature is what finally makes that
  refusal reachable.
- A unit is deactivated while people are still placed in it. Existing placements stand; the unit is
  simply no longer offered for new ones.
- A pre-provisioned person is deleted before ever signing in. Their address becomes available again
  and their unclaimed preparation goes with them.
- A person holds two roles granting the same permission. Revoking one leaves the permission, because
  effective permissions are a union rather than a list of grants.
- A sign-in presents a verified email matching a pre-provisioned record that has been deactivated.
  The record is claimed and the person remains deactivated, because deactivation is an
  administrator's decision that a sign-in does not overturn.
- An administrator's own session is affected by a change they make to someone else, because the two
  share a role. Nothing special happens; only self-affecting changes are guarded.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a list of people that can be searched by name or email address
  and filtered by branch, by department, by team, by active state, and by whether the person has ever
  signed in.
- **FR-002**: The list MUST be paginated using the product's existing pagination contract.
- **FR-003**: An administrator MUST be able to view a single person's identity details, the roles
  they hold, the permissions those roles grant, and their placement.
- **FR-004**: A person's name and email address MUST be presented as owned by the identity provider
  and MUST NOT be editable in the CRM.
- **FR-005**: An administrator MUST be able to grant and revoke roles individually, and a person MUST
  be able to hold any number of them at once.
- **FR-006**: Effective permissions MUST be the union of the permissions granted by the roles a
  person holds, with no deny or override semantics.
- **FR-007**: Effective permissions MUST be presented as derived and MUST NOT be directly editable.
- **FR-008**: Granting a role a person already holds MUST succeed without creating a duplicate.
- **FR-009**: An administrator MUST be able to record a person's branch independently of their
  department and team.
- **FR-010**: When a team is selected for a person, the system MUST record that team's department as
  the person's department, and MUST NOT accept a different department alongside it.
- **FR-011**: A person's recorded department MUST equal their team's department whenever a team is
  recorded, and the system MUST refuse any request that would break this rather than silently
  correcting it.
- **FR-012**: Placement choosers MUST offer only active units.
- **FR-013**: An administrator MUST be able to create a person by email address before that person
  has signed in, and MUST be able to give that person roles and placement immediately.
- **FR-014**: Creating a person by an email address that already belongs to an existing person MUST
  be refused.
- **FR-015**: On first sign-in, the system MUST identify a returning person by their provider and
  subject before considering their email address.
- **FR-016**: When no person matches by provider and subject, the system MUST claim a pre-provisioned
  person only when exactly one unclaimed record matches the normalized email address and the identity
  provider asserts that the address is verified.
- **FR-017**: When the provider does not assert a verified email, or when more than one record
  matches, the system MUST NOT claim any record, MUST create an ordinary new person, and MUST record
  the refused claim.
- **FR-018**: When an email address belongs to a person already bound to a different provider
  subject, the system MUST refuse the sign-in and record the collision for manual resolution.
- **FR-019**: Once a person is bound to a provider and subject, the system MUST NOT re-bind them on
  the basis of an email address under any circumstances.
- **FR-020**: The claim MUST retain the roles and placement set in advance, and MUST record that a
  claim occurred.
- **FR-021**: The name the system uses for the identity provider's verified-email assertion MUST be
  configurable, as the provider itself is.
- **FR-022**: An administrator MUST be able to deactivate and reactivate a person.
- **FR-023**: Deactivating a person MUST end all of that person's active sessions immediately.
- **FR-024**: An administrator MUST be able to delete a person, and deletion MUST revoke every role,
  end every session, and remove the person from the lists as one indivisible operation that either
  wholly succeeds or wholly fails.
- **FR-025**: Deleting a person MUST record the roles they held immediately beforehand, because no
  other record of a revoked role survives.
- **FR-026**: Deleting a person MUST free their email address for reuse.
- **FR-027**: A restored person MUST hold no roles.
- **FR-028**: The system MUST refuse any change that would leave it with no active person holding the
  administrator role.
- **FR-029**: The system MUST refuse an administrator's attempt to remove their own administrator
  role, deactivate themselves, or delete themselves.
- **FR-030**: Every refusal MUST identify which rule refused it, so the interface can explain it in
  the reader's language rather than repeating a sentence from the server.

### Authorization Requirements *(mandatory - Constitution Principles IV and V)*

- **AR-001**: Viewing people, their roles, and their placement MUST require permission
  `identity.view`.
- **AR-002**: Creating, placing, role-granting, deactivating, and deleting people MUST require
  permission `identity.manage`.
- **AR-003**: Every operation in this feature MUST admit the Staff caller population only.
- **AR-004**: Visibility is not scoped by organizational placement. Administration is global,
  because an administrator arranging the organization must be able to see all of it, and because
  scoping the people list by a placement this feature exists to assign would be circular.
- **AR-005**: Granting or revoking a role MUST produce an audit record capturing the actor, the
  person affected, and the role.
- **AR-006**: Changing a placement MUST produce an audit record capturing the actor, the person
  affected, and the placement before and after.
- **AR-007**: Deactivating or deleting a person MUST produce an audit record capturing the actor,
  the person affected, and - for a deletion - the roles held immediately beforehand.
- **AR-008**: A refused claim and a subject collision MUST each produce an audit record identifying
  the attempt, without recording credentials, tokens, or personal data beyond the address involved.

### Localization Requirements *(mandatory - Constitution Principle VII)*

- **LR-001**: All user-visible strings introduced by this feature MUST be translatable in Arabic and
  English.
- **LR-002**: Unlike an organization unit, a person has one name supplied by the identity provider
  rather than one per language. Lists of people are therefore ordered by that single name, and the
  ordering MUST NOT change with the reader's language.
- **LR-003**: Unit names shown in placement choosers and in the people list MUST appear in the
  reader's language, matching how feature 003 presents them.
- **LR-004**: Every refusal MUST be expressed as the interface's own wording in the reader's
  language, never as text taken from the server.

### Key Entities *(include if feature involves data)*

- **Person**: Somebody who may sign in, or who has been prepared to. Carries the identity the
  provider owns, whether the account is active, and where in the organization they belong. A person
  who has never signed in is distinguished by having no provider identity bound yet, rather than by a
  separate status that could disagree with it.
- **Role held**: The fact that a person holds a role, and when and by whom it was granted. A person
  may hold several; the same role is never held twice.
- **Placement**: A person's branch, department, and team. The department is determined by the team
  whenever there is one.
- **History/audit**: Every grant, revocation, placement change, deactivation, deletion, refused
  claim, and collision MUST remain traceable. The roles a deleted person held MUST survive the
  deletion, because revocation leaves no other trace.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An administrator can find a named person and record their placement in under one
  minute, without leaving the people area.
- **SC-002**: A scan for people whose recorded department disagrees with their team's department
  returns zero rows, at all times and after any sequence of placements, team moves, and deletions.
- **SC-003**: No sequence of permitted actions can leave the system with zero active administrators;
  every attempt is refused.
- **SC-004**: A person prepared in advance who signs in with a verified email retains one hundred
  percent of the roles and placement prepared for them, and no duplicate record is created.
- **SC-005**: A person who is deactivated or deleted cannot complete an authenticated request
  afterwards, without waiting for any credential to expire.
- **SC-006**: No sign-in ever binds an established account to a different provider identity on the
  strength of an email address.
- **SC-007**: Every screen this feature adds is reachable from the application's navigation by a
  session permitted to use it.

## Out of Scope

- **Creating roles and editing permissions.** Which permissions a role grants stays as deployed.
  Defining authority is a separate feature with its own lockout risks.
- **Portal users.** Everything here admits Staff only. Customer-facing accounts are a later concern.
- **Bulk import.** People are added one at a time.
- **A restore interface.** Deletion is recorded rather than destructive so history survives, but
  bringing somebody back is not a screen this feature builds.
- **Credentials.** Passwords, multi-factor settings, and account recovery belong to the identity
  provider and are never handled here.
- **Delegated administration.** There is no notion of an administrator limited to their own
  department; that would require the scoping AR-004 deliberately excludes.

## Assumptions

- **The canonical identity is the provider together with the subject, and the system records which
  provider.** The description called the identity "provider + subject", and only the subject is
  stored today. This specification assumes the provider is recorded alongside it, so a second
  identity provider can never be mistaken for the first. If a single provider is guaranteed
  permanently, this can be dropped - but doing so later means changing the identity of every
  existing person and the sign-in path a second time.
- Roles are those the deployment seeds. The administrator role is the one that carries
  `identity.manage`, which is what makes the never-zero rule meaningful.
- The identity provider owns each person's display name and email address, and the CRM reflects them
  rather than editing them, consistent with feature 003 retiring provider-asserted placement in the
  other direction.
- Email addresses are compared in a normalized form, as the product already does when looking a
  person up by address.
- An administrator is a small population working on a desktop; this is an administration surface, not
  an end-user one.
- Feature 003's organization structure exists and is populated. This feature places people into it
  and does not create units.
