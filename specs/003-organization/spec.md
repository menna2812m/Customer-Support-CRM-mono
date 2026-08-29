# Feature Specification: Organization Structure

**Feature Branch**: `003-organization`
**Created**: 2026-08-30
**Status**: Draft
**Input**: User description: "Departments, branches, and teams as manageable entities"

Feature 001 declared organizational placement and feature 002 carried it, but nothing has ever
created a department, a branch, or a team. They exist only as three nullable identifier columns on
a user and as claim names read from the identity provider, and they are empty for every user in the
system. This feature gives them substance: real records an administrator maintains, so that the
users-and-permissions feature can place people in them.

The feature deliberately ships no capability an end user will notice. It creates the structure that
the next feature consumes. That is the cost of splitting the administration surface in two, and it
buys a settled organizational model before any screen depends on one.

## Clarifications

### Session 2026-08-30

- Q: How much of the administration surface belongs here? → A: Organization structure only.
  Users, roles, permissions, and the act of placing a person are feature 004, built immediately
  after this one.
- Q: What shape does the organization take? → A: Branches stand alone as geography; teams belong to
  departments. A person's team therefore implies their department, while their branch is
  independent of both.
- Q: Once these are real records, who owns a person's placement - the CRM or the identity provider?
  → A: The CRM. The provider's department, branch, and team claims are retired rather than left
  configured, because a claim that is read is a claim that can overwrite an administrator's decision.
- Q: Do the names themselves need to be bilingual, or only the interface labels? → A: The names
  themselves. Each unit stores an Arabic and an English name.
- Q: Is the organization tree itself scoped by organizational placement? → A: No. Structure is
  global reference data. Scoping the tree by the tree would be circular, and an administrator
  maintaining it needs to see all of it.
- Q: Can a unit's code be changed after it is created? → A: No. The code is set once and is
  immutable; the bilingual names are the mutable human label. A code that can change is an
  identifier that only looks stable.
- Q: Can an administrator restore a deleted unit? → A: No. Deletion is recorded rather than
  destructive so the audit history survives, but no restore interface is built. Deactivation already
  covers retiring a unit that has history worth keeping.
- Q: How are two administrators editing the same unit at once resolved? → A: Last write wins. No
  concurrency token; the audit trail makes an overwrite discoverable afterwards. The structure is
  small and rarely edited, so the collision is improbable and its damage mild.
- Q: Must names be unique as well as codes? → A: Department and branch names must be unique among
  their kind; team names only within their own department. A team name is only ever read under its
  department, so "Tier 1" may exist under several.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Maintain departments and their teams (Priority: P1)

An administrator opens the organization section and builds out the functional structure of the
business: departments such as Technical Support and Billing, and within each of them the teams that
do the work, such as Tier 1 and Tier 2. They can correct a name, retire a team that no longer
exists, and see at a glance which teams belong to which department.

**Why this priority**: The department-and-team hierarchy is the part of the model that feature 004
depends on most directly, because placing a person in a team is what gives their work a home. It is
also the only part with a containment rule, so it carries the design risk.

**Independent Test**: Create a department, add two teams to it, rename one, and deactivate the
other. The department list shows the department with its remaining active team, and the deactivated
team is no longer offered as a placement.

**Acceptance Scenarios**:

1. **Given** no departments exist, **When** an administrator creates one with an Arabic and an
   English name and a code, **Then** it appears in the department list under the name matching their
   active language.
2. **Given** a department exists, **When** an administrator adds a team to it, **Then** the team is
   listed under that department and nowhere else.
3. **Given** a department with two teams, **When** an administrator deactivates one team, **Then**
   the team remains visible in administration marked inactive, and is not offered when placing a
   person.
4. **Given** a department code that is already in use, **When** an administrator tries to reuse it
   in a different letter case, **Then** the attempt is refused with a message naming the conflict.
5. **Given** a department that still has teams, **When** an administrator tries to delete it,
   **Then** the attempt is refused and the reason names the dependent teams.

---

### User Story 2 - Maintain branches (Priority: P2)

An administrator records the geographic branches the organization operates from, such as Riyadh,
Jeddah, and Dammam, and keeps that list current as locations open and close.

**Why this priority**: Branches are independent of the department hierarchy and carry no containment
rule, so they deliver value on their own and can be built after the harder half. Feature 004 needs
them, but nothing in User Story 1 depends on them.

**Independent Test**: Create three branches, rename one, deactivate another. The branch list
reflects all three states, and only the active branches are offered as placements.

**Acceptance Scenarios**:

1. **Given** no branches exist, **When** an administrator creates one, **Then** it appears in the
   branch list in both languages.
2. **Given** a branch with people placed in it, **When** an administrator tries to delete it,
   **Then** the attempt is refused and the reason names the number of people affected.
3. **Given** an active branch, **When** an administrator deactivates it, **Then** people already
   placed there keep their placement, and the branch stops being offered for new placements.

---

### User Story 3 - Reorganize without corrupting placement (Priority: P3)

A team is moved from one department to another, as happens when a business reorganizes. Everyone on
that team must end up recorded in the new department, not stranded in the old one.

**Why this priority**: This is the consistency rule the chosen hierarchy creates. It is lower
priority because it is not needed to stand the structure up, but it must exist before feature 004
places anybody, or the first reorganization silently corrupts the placement of every affected
person.

**Independent Test**: With people placed in a team, move the team to another department and confirm
every member's recorded department follows the team.

**Acceptance Scenarios**:

1. **Given** a team with members, **When** the team is moved to another department, **Then** every
   member's recorded department becomes the new department.
2. **Given** a team is moved, **When** the move completes, **Then** an audit record captures the
   team, both departments, who moved it, and how many people were affected.
3. **Given** a target department that is inactive, **When** an administrator tries to move a team
   into it, **Then** the attempt is refused.

### Edge Cases

- A unit is created with an Arabic name but a blank English one, or the reverse. Both are required;
  a half-translated organization is the seam Constitution VII exists to prevent.
- Two administrators create units with the same code at the same moment. Uniqueness is enforced at
  the store, not only by a prior check, so the second attempt is refused rather than admitted.
- Two administrators rename the same unit at the same moment. The later write wins and no conflict
  is reported; both changes appear in the audit trail, so the overwrite is discoverable afterwards.
- A department is deactivated while it still has active teams. Deactivation cascades to nothing;
  the teams remain, but a person cannot be placed in a team whose department is inactive.
- A person is already placed in a unit that is later deactivated. Their placement is untouched.
  Deactivation governs future choices, not past ones.
- Codes differing only by surrounding whitespace or letter case are the same code.
- A team is moved to the department it is already in. The move is accepted and changes nothing
  rather than being treated as an error.
- Two departments each have a team named "Tier 1". This is permitted, and remains permitted when
  one of those teams is moved - unless the destination department already has a team of that name,
  which refuses the move.
- The identity provider is still configured to assert department, branch, or team claims after this
  feature retires them. The claims are ignored, not honoured.

## Requirements *(mandatory)*

### Functional Requirements

**Structure**

- **FR-001**: The system MUST maintain three kinds of organizational unit: branch, department, and
  team.
- **FR-002**: Every team MUST belong to exactly one department. A team cannot exist without one.
- **FR-003**: Branches MUST be independent of departments and teams. No branch contains a
  department, and no department belongs to a branch.
- **FR-004**: The system MUST support any number of each kind of unit, and MUST NOT assume a single
  department or a single branch anywhere in its logic (Constitution V).

**Naming and identity**

- **FR-005**: Every unit MUST carry both an Arabic and an English name, and both MUST be present.
  Department and branch names MUST be unique among units of their kind; team names MUST be unique
  within their own department. Names are compared ignoring letter case and surrounding whitespace,
  and each language is checked independently.
- **FR-006**: Every unit MUST carry a code that is unique among units of its kind, compared
  ignoring letter case and surrounding whitespace. The code MUST be set when the unit is created and
  MUST NOT change afterwards, so that anything referring to a unit by code keeps referring to the
  same unit. A unit created with the wrong code is deleted and recreated, which FR-011 permits while
  it has no dependents.
- **FR-007**: The system MUST display the name matching the reader's active language, falling back
  to the other name only if the expected one is somehow absent.

**Maintenance**

- **FR-008**: An administrator MUST be able to create, rename, and list units of each kind.
- **FR-009**: An administrator MUST be able to deactivate and reactivate a unit. A deactivated unit
  remains visible in administration, and the system MUST be able to list active units alone, so that
  a consumer choosing a placement never has to filter inactive ones out for itself.
- **FR-010**: Deactivating a unit MUST NOT change the placement of anyone already placed in it.
- **FR-011**: An administrator MUST be able to delete a unit that has no dependents. Deletion MUST
  be recorded rather than destructive: the unit disappears from every list, while the record and its
  audit history survive. Restoring a deleted unit is not an interface this feature provides.
- **FR-012**: Deletion MUST be refused when the unit still has dependents, and the refusal MUST name
  what depends on it: teams for a department, placed people for any unit.
- **FR-013**: Lists MUST be paginated using the established pagination contract and MUST be
  searchable by either name or by code.

**Reorganization**

- **FR-014**: A team MUST be movable from one department to another.
- **FR-015**: When a team moves, the recorded department of every person on that team MUST be
  updated to the new department in the same operation. The move and those updates MUST succeed or
  fail as a whole; a partially applied move would leave exactly the inconsistency FR-017 forbids.
- **FR-016**: A team MUST NOT be moved into an inactive department.
- **FR-017**: A person's recorded department MUST agree with the department of their recorded team
  whenever both are present. A person may have a department without a team.

**Retiring provider-asserted placement**

- **FR-018**: The system MUST stop reading department, branch, and team claims from the identity
  provider, and MUST stop overwriting stored placement at sign-in.
- **FR-019**: Retiring those claims MUST NOT affect any other part of sign-in: subject, email, and
  display name continue to come from the provider exactly as before.

### Authorization Requirements *(mandatory - Constitution Principles IV and V)*

- **AR-001**: Reading any organizational unit MUST require permission `organization.view`.
- **AR-002**: Creating, renaming, deactivating, deleting, or moving any unit MUST require permission
  `organization.manage`.
- **AR-003**: Both permissions MUST admit the Staff population only. The portal population has no
  business reading the organization's internal structure.
- **AR-004**: Visibility is scoped by nothing. Organizational structure is global reference data;
  scoping the structure by structure would be circular, and an administrator maintaining it needs
  to see all of it.
- **AR-005**: Creating, renaming, deactivating, reactivating, deleting, and moving a unit MUST each
  produce an audit record capturing the actor, the unit, and what changed.
- **AR-006**: A team move MUST additionally record both departments and the number of people whose
  placement changed as a result.

### Localization Requirements *(mandatory - Constitution Principle VII)*

- **LR-001**: All user-visible strings introduced by this feature MUST be translatable in Arabic and
  English.
- **LR-002**: Unit lists MUST sort by the name in the reader's active language, not always by the
  English name.
- **LR-003**: Both name fields MUST be presented together on the same form so that a unit cannot be
  created in one language and completed in the other later.
- **LR-004**: Layout MUST use logical properties only, so the department-and-team hierarchy indents
  from the correct side under both directions.

### Key Entities *(include if feature involves data)*

- **Branch**: A geographic location the organization operates from. Carries an Arabic name, an
  English name, a unique code, and whether it is active. Belongs to nothing.
- **Department**: A functional division of the business. Same attributes as a branch. Contains
  teams.
- **Team**: A working group inside exactly one department. Same attributes, plus the department it
  belongs to.
- **User** (existing, from feature 002): gains no new fields. Its existing department, branch, and
  team references become references to real records rather than unpopulated identifiers, and its
  department is kept in agreement with its team by FR-015.
- **History/audit**: Every creation, rename, activation change, deletion, and team move MUST remain
  traceable to an actor and a moment. A team move MUST remain traceable after the fact even though
  the team's current department no longer shows where it came from.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An administrator can build a complete organizational structure of departments, their
  teams, and branches without a code change, a migration, or a deployment. Today this is impossible
  by any means.
- **SC-002**: Every stored unit, and only the active ones, can be retrieved as a list suitable for
  driving a placement chooser - so that feature 004 needs no placeholder or hard-coded unit.
- **SC-003**: Moving a team between departments leaves zero people recorded in a department that
  does not match their team, verified by a check over stored data after the move.
- **SC-004**: No unit exists with only one of its two names filled in.
- **SC-005**: A sign-in performed while the identity provider asserts department, branch, or team
  claims leaves stored placement unchanged.

## Out of Scope

- **Placing people in units.** Choosing a person's department, branch, or team is feature 004. This
  feature creates the units and keeps them internally consistent; it never edits a person except as
  the unavoidable consequence of moving their team (FR-015).
- **Users, roles, and permissions administration**, all of which is feature 004.
- **Enforcing organizational scope on data.** Constitution V requires the model stay capable of
  scoping, and it does; no query anywhere is filtered by placement yet. The feature that introduces
  scoped data introduces its enforcement.
- **Importing or synchronising structure from an external directory.** The CRM owns this data
  outright, per the clarification above. A directory sync would be its own feature with its own
  reconciliation rules.
- **Organization chart visualisation, headcount reporting, or any analytic view** over the
  structure.
- **Moving a department between branches**, which the chosen shape makes meaningless: departments do
  not belong to branches.
- **Nesting**: a team inside a team, or a department inside a department. The hierarchy is exactly
  two levels deep on the functional side and one on the geographic side.

## Assumptions

- The structure is small and changes rarely. It is maintained by hand by a few administrators, not
  generated or bulk-imported, so the lists are measured in tens rather than thousands and no bulk
  editing affordance is needed.
- Codes are chosen by administrators for their own reference rather than to match an external
  system. Nothing outside the CRM depends on their values.
- The seams from features 001 and 002 are used rather than replaced: the permission catalog,
  `[RequirePermission]`, the audit recorder, soft deletion, the pagination contract, and the shared
  error contract.
- Deactivation and deletion are genuinely different operations serving different needs, rather than
  two names for the same thing: deactivation retires a unit that still has history, deletion removes
  one created in error.
- Feature 004 follows immediately. If it did not, this feature would be structure with no consumer,
  and the split would not be worth making.
