# Feature Specification: Authentication and Login

**Feature Branch**: `002-auth-login`
**Created**: 2026-08-26
**Status**: Draft
**Input**: User description: "authentication and login"

## Context

Feature 001 delivered the authentication *seams* and deliberately no authentication: two bearer
schemes registered and disabled, a route guard that always returns true, a token interceptor that
attaches nothing, a code-declared permission catalog, and `ICurrentUser` carrying population and
organizational scope. Every endpoint already denies access by default.

This feature fills those seams in for **staff only**. When it ships, an agent can sign in through
the corporate identity provider, receive real permissions, and reach real screens; an
unauthenticated caller reaches nothing.

The external customer portal is **deferred**. The portal population, its scheme, and its place in
`ICurrentUser` all remain as feature 001 built them - registered, disabled, and untouched by this
feature - so the portal feature can fill them later without redesign.

## Clarifications

### Session 2026-08-26

- Q: Which corporate identity provider will staff authenticate against? → A: Not decided yet. Build against standard OIDC discovery only, so the provider is configuration rather than code.
- Q: How are external portal accounts created? → A: The portal is deferred entirely. Staff sign-in only in this feature; the portal scheme stays configured and unused.
- Q: Where do a staff member's permissions come from at sign-in? → A: This feature ships a minimal role-to-permission store seeded from the catalog, with assignments made by migration until the users-and-permissions feature adds a screen.
- Q: How do credentials reach the browser? → A: The short-lived access credential is held in memory and sent as a bearer token; the renewal credential is delivered in an HttpOnly, SameSite cookie and is never readable by script.
- Q: How does an ordinary staff member get a role before the administration screens exist? → A: A configurable default role is granted to any staff member who authenticates, with the bootstrap administrator as the named exception.
- Q: Where does a staff member's organizational placement come from? → A: From provider claims when present, falling back to the value stored on the CRM user record.
- Q: What does sign-out end? → A: The CRM session always; ending the identity provider session is offered as an explicit additional choice.
- Q: What happens when the provider presents a new subject whose email matches an existing user? → A: Refuse the sign-in, audit it as a collision, and require manual resolution - never link or duplicate automatically.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A staff member signs in and reaches their work (Priority: P1)

An agent opens the CRM at the start of their shift, signs in with their corporate credentials, and
lands on the application with their identity, permissions, and organizational placement resolved.
They never create or manage a separate CRM password.

**Why this priority**: Nothing in the CRM is usable by anyone until staff can sign in. This alone
turns the foundation into a system people can log into.

**Independent Test**: With an OIDC provider configured, sign in as a staff account and confirm the
application loads with that identity, and that an API call succeeds which would have returned 401
before.

**Acceptance Scenarios**:

1. **Given** an unauthenticated visitor, **When** they open any protected screen, **Then** they are
   sent to sign-in and returned to their original destination after signing in successfully.
2. **Given** valid corporate credentials, **When** the staff member completes sign-in, **Then** the
   application shows them as signed in, with their display name and their permitted navigation.
3. **Given** an account that authenticates at the provider but has no CRM access, **When** they sign
   in, **Then** they are told clearly that they have no access to this application, and are not left
   on a blank or broken screen.
4. **Given** a signed-in staff member, **When** they call an endpoint requiring a permission they
   hold, **Then** the call succeeds; **When** they call one they do not hold, **Then** it is refused
   without revealing what the resource contains.
5. **Given** the identity provider is unreachable, **When** a user attempts to sign in, **Then**
   they see an explanation that sign-in is temporarily unavailable, and the CRM does not present it
   as their mistake.
6. **Given** a new provider identity whose email matches an existing user, **When** they sign in,
   **Then** access is refused with an explanation that an administrator must resolve it, and the
   collision is recorded - no roles or history are inherited.

---

### User Story 2 - A session lasts a working day, and ending it means ending it (Priority: P1)

A signed-in user keeps working without being asked to sign in again every few minutes. When they
sign out - or when their session expires - their access stops immediately, including on a shared
machine.

**Why this priority**: A sign-in that cannot be relied on, or a sign-out that does not actually
revoke access, is worse than no sign-in at all. Support staff routinely share workstations.

**Independent Test**: Sign in, remain active past the access-credential lifetime, and confirm work
continues uninterrupted. Then sign out and confirm that a previously working request is refused.

**Acceptance Scenarios**:

1. **Given** a signed-in user working continuously, **When** their short-lived access credential
   expires, **Then** it is renewed without interrupting what they were doing.
2. **Given** a user who has been idle beyond the inactivity limit, **When** they return, **Then**
   they must sign in again.
3. **Given** a signed-in user, **When** they sign out, **Then** their session is revoked and any
   further request with the previous credentials is refused.
4. **Given** a user signed in on two devices, **When** they sign out on one, **Then** the other is
   unaffected unless they explicitly chose to sign out everywhere.
5. **Given** a user on a shared workstation, **When** they sign out and choose to end access on that
   computer, **Then** returning to the CRM requires authenticating again rather than silently
   restoring the previous person.
6. **Given** a user whose session has expired, **When** they attempt an action, **Then** they are
   returned to sign-in with their unsaved context handled predictably rather than silently lost.

---

### User Story 3 - Permissions arrive with the session, and changes take effect safely (Priority: P2)

A signed-in agent holds exactly the permissions their roles grant. When an administrator changes
those roles - by deployment, until the administration screens exist - the change reaches the agent
without them signing out, and never grants more than intended.

**Why this priority**: Sign-in that grants nothing proves the pipe works but delivers no capability.
This is what makes the first business feature usable by the right people.

**Independent Test**: Assign a role, sign in, confirm the permitted call succeeds and an
unpermitted one is refused. Change the assignment, wait one renewal cycle, and confirm the change
took effect without a sign-out.

**Acceptance Scenarios**:

1. **Given** a staff member with a role granting a permission, **When** they sign in, **Then** their
   session carries that permission and the matching endpoints admit them.
2. **Given** a deployment that configures a default role, **When** a staff member signs in for the
   first time, **Then** they receive that role and can do the work it permits.
3. **Given** a deployment that configures no default role, **When** a staff member signs in for the
   first time, **Then** they are recognised as a valid user with no access, and told so plainly.
4. **Given** a permission removed from a user's role, **When** at most one renewal cycle has passed,
   **Then** the affected endpoints refuse them, without the user signing out.
5. **Given** a caller who edits their own session data or request payload to claim a permission,
   **When** the request is processed, **Then** the claim is ignored and the request is refused.
6. **Given** a first deployment with no users, **When** the configured bootstrap administrator signs
   in, **Then** they hold administrative permissions, so the system is never locked out of itself.

---

### User Story 4 - Authentication is accountable and abuse is throttled (Priority: P3)

Every sign-in, renewal, and sign-out is recorded so a security question can be answered afterwards,
and the anonymous endpoints cannot be hammered.

**Why this priority**: Required before real traffic, but it protects a capability that must exist
first.

**Independent Test**: Complete a sign-in and a sign-out, confirm the audit trail shows both with the
correlation identifier and no secret material; then exceed the request limit on an anonymous
endpoint and confirm throttling, with a legitimate user elsewhere unaffected.

**Acceptance Scenarios**:

1. **Given** any sign-in, renewal, sign-out, or refusal, **When** it occurs, **Then** an audit record
   exists with who, what, when, the outcome, and the correlation identifier - and no token or secret.
2. **Given** repeated requests to an anonymous authentication endpoint from one source, **When** the
   threshold is crossed, **Then** that source is throttled while unrelated users are unaffected.
3. **Given** a renewal credential presented a second time, **When** it is received, **Then** the
   session is revoked and the event is recorded as a suspected compromise.
4. **Given** an investigator with the identifier a user quoted, **When** they search the audit trail,
   **Then** they can reconstruct that user's authentication events without reading application logs.

---

### Edge Cases

- The identity provider is unreachable, slow, or returns an error mid-sign-in.
- A staff account is disabled, deleted, or renamed at the provider while a CRM session is active.
- A returning user has a session credential issued before a role change.
- Two browser tabs refresh the same session simultaneously.
- The clock on the server and the provider differ by more than the allowed tolerance.
- A user signs in on a shared machine and walks away without signing out.
- A user signs out of the CRM but declines to end the provider session, then someone else uses the
  same browser.
- The provider asserts an identity the CRM has never seen, or one whose email now matches a
  different existing account.
- A user completes sign-in in one tab while another tab is showing the expired-session screen.
- A role references a permission that no longer exists in the catalog.
- Every role assignment is removed while a user is working.
- The bootstrap administrator identifier is misconfigured or matches nobody.
- Sign-in fails while the interface is in Arabic; every message, including failures, must be in that
  language.

## Requirements *(mandatory)*

### Functional Requirements

#### Staff authentication

- **FR-001**: Staff MUST authenticate through an external identity provider using standard OpenID
  Connect discovery, so the specific provider is configuration rather than code. The CRM MUST NOT
  store, accept, or transmit a staff password.
- **FR-002**: Provider settings - discovery address, client identity, and the claims used for
  subject, name, and email - MUST be configurable per environment without a code change.
- **FR-003**: The sign-in flow MUST return the user to the screen they originally requested.
- **FR-004**: The CRM MUST resolve a caller into a CRM user record on first sign-in, keyed on the
  provider's stable subject identifier, and MUST keep that record stable across later sign-ins even
  if the display name or email changes at the provider.
- **FR-005**: When the provider presents an unrecognised subject whose email address already
  belongs to a different user record, the CRM MUST refuse the sign-in, record the collision as an
  audited security event naming both records, and require a person to resolve it. It MUST NOT link
  the identities automatically, and MUST NOT create a second record silently.

  This happens in ordinary life - an employee leaves and their address is reissued to a new hire -
  and the two possible readings have opposite consequences: linking would hand the new person the
  leaver's roles and history, while silently duplicating would split one person's work across two
  records. Only a human can tell which case it is, so the CRM asks rather than guesses.
- **FR-006**: An account that authenticates successfully but holds no CRM permissions MUST receive a
  clear "no access to this application" outcome, distinct from a failed sign-in.
- **FR-007**: A CRM user record MUST support an inactive state, and an inactive user MUST be refused
  even when the provider authenticates them successfully.
- **FR-008**: Multi-factor authentication, password policy, and account lockout for staff are the
  provider's responsibility. The CRM MUST NOT weaken, bypass, or duplicate them.
- **FR-009**: Sign-in failures originating at the provider MUST be reported as a provider problem
  rather than as invalid user input, and MUST NOT expose provider error detail to the user.

#### Session lifecycle

- **FR-010**: A successful sign-in MUST establish a session carrying the caller's identity,
  population, permissions, and organizational scope.
- **FR-011**: Access credentials MUST be short-lived and MUST be renewable without the user
  re-entering credentials, for as long as the session remains valid.
- **FR-012**: A session MUST end after a period of inactivity, and MUST also have an absolute
  maximum lifetime after which re-authentication is required regardless of activity.
- **FR-013**: Renewal MUST be single-use: a renewal credential presented twice MUST be treated as
  compromised, and the session MUST be revoked.
- **FR-014**: Sign-out MUST revoke the CRM session immediately, so credentials issued for it stop
  working; a user MUST also be able to sign out of all their CRM sessions at once.
- **FR-015**: Sign-out MUST additionally offer to end the session at the identity provider, as an
  explicit choice rather than a default:
  - choosing it MUST send the user through the provider's sign-out so that returning to the CRM
    requires authenticating again;
  - declining it MUST leave the provider session untouched, so other corporate applications in the
    same browser are unaffected;
  - the choice MUST be presented in terms a user understands - ending access on this computer
    versus signing out of the CRM only - not in terms of tokens or providers.

  Without the offer, a shared workstation is unsafe: the CRM session ends, the provider silently
  re-authenticates the previous person on the next sign-in, and sign-out appears to have worked
  while having achieved nothing.
- **FR-016**: Credentials MUST be delivered in two distinct forms:
  - the short-lived **access credential** is held only in memory by the application and presented
    as a bearer credential on each API call - it is never written to browser storage;
  - the long-lived **renewal credential** is delivered in a cookie that scripts cannot read, is
    restricted to the site that issued it, and is only ever sent to the renewal endpoint.

  Neither credential MUST appear in URLs, browser history, or logs. The split is deliberate: the
  credential worth stealing is the one a page script can never reach, so a cross-site scripting
  flaw cannot yield persistent access.
- **FR-017**: Because the renewal credential travels as a cookie, the renewal endpoint MUST be
  protected against cross-site request forgery, and MUST refuse requests from origins outside the
  configured allowlist.
- **FR-018**: Concurrent sessions on separate devices MUST be independent: ending one MUST NOT end
  the others unless "sign out everywhere" was chosen.
- **FR-019**: Deactivating a user MUST revoke their sessions.

#### Permissions

- **FR-020**: The CRM MUST hold role definitions and the permissions each role grants, drawn from
  the catalog established in feature 001. A role referencing a permission absent from the catalog
  MUST be reported at startup rather than silently ignored.
- **FR-021**: A user MUST be assignable to one or more roles, and their effective permissions MUST
  be the union of those roles' permissions.
- **FR-022**: Role definitions and assignments MUST be changeable by deployment (migration or seed)
  in this feature. No screen for editing them is delivered here; the store MUST be shaped so the
  users-and-permissions feature can add one without changing how sessions are issued.
- **FR-023**: A configured bootstrap administrator identity MUST receive administrative permissions
  on first sign-in, so a fresh deployment is never locked out of itself. This MUST be an explicit
  configuration value, not a default account with known credentials.
- **FR-024**: Every other staff member who authenticates MUST be granted a configured **default
  role** on first sign-in, so the CRM is usable by a whole team before the administration screens
  exist. Specifically:
  - the default role MUST be named in configuration per environment, and MUST be one of the seeded
    roles;
  - configuring no default role MUST be permitted, and means new staff sign in with no access -
    the deployment chooses between reach and restriction, rather than the code choosing for it;
  - the default MUST apply only when the user has no role assignment yet, so it can never re-grant
    access that was deliberately removed;
  - granting it MUST be audited like any other assignment.
- **FR-025**: A session MUST carry the caller's effective permissions and organizational scope, and
  a change to either MUST take effect within one renewal cycle without the user signing out.
- **FR-026**: Organizational placement - department, branch, and team - MUST be resolved at each
  sign-in as follows:
  - when the provider asserts placement claims, read through configurable claim names, those values
    are used and are written to the user record so the CRM holds its own copy;
  - when the provider asserts none, the value already stored on the user record is used;
  - when neither exists, the caller has no organizational scope, and any feature that scopes by
    organization MUST treat that as "sees nothing extra" rather than "sees everything".

  Reading from the directory keeps placement where it is already maintained; the stored fallback
  means a provider carrying no organizational data does not prevent the organization feature from
  populating it later.
- **FR-027**: The CRM MUST never accept the caller's own assertion of population, permissions, or
  organizational scope from a request payload or a client-supplied claim.

#### Abuse protection

- **FR-028**: The anonymous authentication endpoints - sign-in initiation, the provider callback,
  and token renewal - MUST be rate limited per source.
- **FR-029**: Rate limiting MUST be implemented as a reusable capability that any later feature can
  apply to its own endpoints, not as logic local to authentication. This closes the exclusion
  recorded in feature 001 (FR-056).
- **FR-030**: Exceeding a rate limit MUST return the shared error contract with a distinct,
  machine-readable code, and MUST tell the caller when they may retry.
- **FR-031**: Rate limiting MUST NOT be able to lock out all users at once: limits are per source,
  and a shared corporate egress address MUST NOT trivially exhaust the limit for an entire office.

#### Sign-in experience

- **FR-032**: The application MUST present a sign-in screen for unauthenticated visitors, and MUST
  distinguish "not signed in" from "signed in but not permitted".
- **FR-033**: Route protection MUST replace the placeholder guard delivered in feature 001, and MUST
  shape the experience only - the backend remains the authority on every decision.
- **FR-034**: The application MUST attach the caller's credentials to API calls through the single
  interceptor seam delivered in feature 001, and MUST renew them transparently, retrying the
  original request once on expiry.
- **FR-035**: Concurrent requests encountering an expired credential MUST trigger a single renewal,
  not one per request.
- **FR-036**: When a session ends mid-use, the user MUST be informed and returned to sign-in rather
  than shown a generic failure.
- **FR-037**: The signed-in user's identity MUST be visible in the application shell, with sign-out
  reachable from every screen.
- **FR-038**: The application MUST show only the navigation the user's permissions allow, while
  never treating that as a security boundary.

#### Auditing and logging

- **FR-039**: Every sign-in attempt, sign-out, renewal, refusal, and suspected renewal-credential
  reuse MUST produce an audit record through the seam delivered in feature 001, capturing actor,
  action, outcome, time, source, and correlation identifier.
- **FR-040**: No token, renewal credential, session identifier, or provider secret MUST appear in
  any log or audit record.
- **FR-041**: A refused sign-in MUST be logged with enough context to investigate - the identity
  referenced, the source, and the outcome - without recording anything that was submitted.

### Authorization Requirements *(mandatory - Constitution Principles IV and V)*

- **AR-001**: Sign-in initiation, the provider callback, and token renewal MUST be reachable
  anonymously; nothing else added by this feature may be.
- **AR-002**: Sign-out and "sign out everywhere" MUST require an authenticated caller and MUST act
  only on that caller's own sessions.
- **AR-003**: Reading the current user's own profile and permissions MUST require an authenticated
  caller and MUST return only their own.
- **AR-004**: Every endpoint added by this feature MUST declare which caller populations may reach
  it. All of them admit staff only; the portal population is registered but unused.
- **AR-005**: Organizational scope carried in a session MUST come from a verified provider claim or
  from the CRM user record, never from a value supplied by the client. A claim is only trusted
  because the token carrying it was validated - an unverified assertion is not a source.
- **AR-006**: Deactivating a user or changing role assignments is out of scope as an interface, but
  where such a change is applied by deployment it MUST be audited with the actor recorded as the
  deployment rather than left anonymous.

### Localization Requirements *(mandatory - Constitution Principle VII)*

- **LR-001**: Every string introduced by this feature - sign-in, sign-out, no-access, session
  expiry, provider-unavailable, rate-limit, and every failure message - MUST exist in Arabic and
  English.
- **LR-002**: Sign-in and no-access screens MUST mirror correctly under right-to-left, including
  button order, alignment, and any focus order.
- **LR-003**: Where the CRM can influence the provider's own sign-in page language, it MUST pass the
  user's current language so the experience does not switch languages mid-flow.
- **LR-004**: Backend failures MUST remain language-neutral machine-readable codes that the client
  translates, consistent with the error contract from feature 001.

### Key Entities

- **User**: the CRM's record of a person who can sign in - stable identifier, the provider subject it
  is keyed on, display name, email, population, active state, and organizational placement. The
  provider subject is unique; email is expected to be unique too, and a conflict is refused and
  escalated rather than resolved automatically (FR-005).
- **Role**: a named set of permissions drawn from the catalog, for example "Agent" or
  "Administrator".
- **Role assignment**: which roles a user holds. Effective permissions are the union.
- **Session**: an established sign-in - the user, when it started, when it was last renewed, when it
  expires, the client it belongs to, and whether it has been revoked.
- **Renewal credential**: the single-use value that extends a session, with its own expiry and a
  record of whether it has been used.
- **Authentication event**: the audit record of a sign-in, renewal, sign-out, or refusal - actor,
  outcome, source, time, and correlation identifier.

Every one of these carries the traceability stamps established in feature 001, and none is hard
deleted.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A staff member with valid corporate credentials reaches the application in under 15
  seconds from opening the CRM, including the redirect to the provider and back.
- **SC-002**: A user working continuously for a full shift is never interrupted to re-authenticate,
  while an idle session ends within the configured inactivity limit.
- **SC-003**: 100% of requests presenting credentials from a signed-out, expired, or revoked session
  are refused, verified by automated tests covering each case.
- **SC-004**: A reused renewal credential revokes the session in 100% of attempts, verified by test.
- **SC-005**: A user with a role holding a permission is admitted by the matching endpoints, and a
  user without it is refused, in 100% of tested cases.
- **SC-006**: A role change reaches the affected user within one renewal cycle without them signing
  out, verified by test.
- **SC-007**: On a fresh deployment with an empty user table, the configured bootstrap administrator
  signs in and holds administrative permissions - verified by test, so the lock-out scenario cannot
  reach production.
- **SC-008**: 100% of authentication events appear in the audit trail with actor, outcome, and
  correlation identifier, and 0 records contain a token or secret - verified by an automated check
  over a full test run.
- **SC-009**: Exceeding the rate limit on an anonymous endpoint returns the shared error contract
  with a retry indication, and unrelated sources are unaffected, verified by test.
- **SC-010**: Every user-facing string added by this feature exists in both languages and the
  sign-in and no-access screens mirror correctly under right-to-left, verified by the existing
  parity and direction checks.
- **SC-011**: With the provider unreachable, sign-in fails within 10 seconds with a clear message,
  and the application does not hang, loop, or blame the user.

## Out of Scope

- **The external customer portal** - portal sign-in, CRM-owned customer accounts, password storage,
  password reset, self-registration, and account activation. The portal population and its scheme
  remain registered and disabled exactly as feature 001 left them, so nothing here has to be undone
  when the portal arrives. Everything a portal needs that this feature happens to build - sessions,
  rate limiting, audit - is built to serve both populations.
- **User and role administration screens** - creating users, assigning roles, editing role
  definitions, or deactivating accounts through the interface. This feature stores that data and
  applies it; the users-and-permissions feature makes it editable.
- **Departments, branches, and teams** as manageable entities. Organizational placement is read and
  carried, not edited.
- **Single sign-on beyond the configured provider**, social sign-in, and customer identity
  federation.
- **MFA enrolment and policy management** - delegated to the provider.
- **Impersonation or "sign in as user"** support tooling.
- **API keys, service accounts, and machine-to-machine authentication.**

## Assumptions

- The seams from feature 001 are used rather than replaced: the staff bearer scheme, the permission
  catalog, `ICurrentUser`, the route-guard extension point, the token interceptor, the audit
  recorder, and the shared error contract.
- The provider is OIDC-compliant and supports discovery. Building against standard OIDC is a
  deliberate trade: if the eventually chosen provider needs non-standard claim or group handling,
  that mapping is a contained change behind the configured claim names in FR-002.
- Staff identity is federated, so the CRM is not responsible for staff password policy, expiry,
  lockout, or MFA - only for what happens after a token is validated. This is why no password
  storage, reset, or account-lockout requirement appears in this feature.
- Role definitions and assignments are applied by migration in this feature. That is deliberately
  primitive: it is enough to grant real access now, and it avoids building an administration screen
  that the users-and-permissions feature would immediately replace.
- Default session parameters, subject to review during planning: access credential 15 minutes,
  inactivity limit 8 hours, absolute session lifetime 12 hours.
- Default rate limits, subject to review during planning: 20 sign-in initiations and 60 renewals per
  source per minute.
- Sessions are stored server-side so that revocation is immediate rather than eventual.

## Dependencies

- **Feature 001 (project foundation)** - merged and available. This feature fills seams it created,
  including the rate-limiting exclusion it recorded.
- **An OIDC provider reachable from the API**, with an application registered and redirect
  addresses agreed. Which provider is deliberately left open (see FR-001); a development-time
  provider is enough to build and test against.
- **Operations** to supply provider client credentials through the secrets seam established in
  feature 001.
