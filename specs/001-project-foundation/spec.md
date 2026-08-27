# Feature Specification: Project Foundation

**Feature Branch**: `001-project-foundation`
**Created**: 2026-08-26
**Status**: Draft
**Input**: User description: "Create the Project Foundation feature for a Customer Support CRM. The product will be implemented as a monorepo containing an Angular frontend, an ASP.NET Core Web API backend, and a SQL Server database. This feature establishes the technical foundation required for future CRM features but does not implement customer, ticket, SLA, reporting, or communication business functionality yet."

## Clarifications

### Session 2026-08-26

- Q: Frontend application topology, given the two caller populations? → A: Single application now, with the workspace structured so shared core/UI live in libraries and a second application can be added later without moving code
- Q: What is the UI component and styling foundation for the shared presentation library? → A: Angular Material with the CDK, using its bidirectional support for RTL and its theming for appearance
- Q: What transport and request hardening belongs in this feature? → A: Application-enforced HTTPS with HSTS, standard security headers, an explicit per-environment CORS allowlist, and request size/complexity limits; rate limiting is deferred to the authentication feature
- Q: Where does the integration test suite get its database? → A: The suite starts a disposable SQL Server container itself, migrates it, and disposes of it; no shared or pre-provisioned test database
- Q: Are permissions code-declared or database rows, and what does the baseline migration contain? → A: Permissions are declared in code as the single source of truth; the baseline migration creates no business tables, establishing only the migration history

## User Scenarios & Testing *(mandatory)*

The users of this feature are the people who build and operate the CRM: platform and feature
developers, QA engineers, and operators. Business end users (agents, supervisors, customers)
gain no visible capability from this feature beyond an empty, bilingual application shell.

### User Story 1 - Run the whole platform locally (Priority: P1)

A developer joins the project, clones the repository, follows the setup documentation, and gets
the backend API, the frontend application, and the database working together on their machine
without asking anyone for missing steps or hidden configuration.

**Why this priority**: Nothing else in this feature can be verified, and no future feature can be
started, until the system runs end to end. This alone is the minimum viable foundation.

**Independent Test**: On a clean machine with only the documented prerequisites installed, follow
the setup documentation and confirm the API starts, the database schema is created from
migrations, the frontend loads, and the frontend successfully reaches the API.

**Acceptance Scenarios**:

1. **Given** a clean clone and the documented prerequisites, **When** the developer follows the
   setup steps, **Then** the backend starts, applies the baseline schema to an empty database,
   and reports a healthy status.
2. **Given** the backend is running, **When** the developer starts the frontend, **Then** the
   application shell loads and successfully calls the backend without manual URL editing in code.
3. **Given** a required configuration value is missing, **When** the backend starts, **Then** it
   stops with a clear message naming the missing setting instead of failing later with an
   unrelated error.
4. **Given** the database is unreachable, **When** the developer opens the health endpoint,
   **Then** the response reports the database dependency as unhealthy without exposing
   connection details.

---

### User Story 2 - Add a new capability using established conventions (Priority: P2)

A developer starting the first business feature finds ready-made conventions for routing,
request validation, error responses, list pagination, permission declaration, database schema
change, and layered placement of code, so that every future feature is built the same way
without re-litigating these decisions.

**Why this priority**: The purpose of the foundation is to make every later vertical feature
cheap and consistent. Conventions that are not established here get invented differently in each
feature.

**Independent Test**: Using only the documentation and the reference slice, add a small
non-business endpoint plus its screen, and confirm it inherits versioned routing, validation,
the shared error contract, pagination, permission enforcement, and migration workflow without
modifying shared infrastructure code beyond a single registration point.

**Acceptance Scenarios**:

1. **Given** the conventions are in place, **When** a developer adds a new endpoint, **Then** it
   is reachable under the versioned API path and requires an explicitly declared permission.
2. **Given** an endpoint receives an invalid payload, **When** the request is processed, **Then**
   the caller receives the shared error contract with field-level validation details, and no
   business logic runs.
3. **Given** a developer changes the data model, **When** they follow the documented workflow,
   **Then** the change is captured as a migration that can be applied to an empty database and
   re-applied without error.
4. **Given** a developer places business logic in a controller or has one frontend feature import
   another feature internals, **When** the automated checks run, **Then** the violation is
   reported as a failure.

---

### User Story 3 - Use the application in Arabic or English (Priority: P2)

Any user of the application shell selects Arabic or English and the entire interface, including
layout direction, follows the selection. Their choice is remembered the next time they open the
application.

**Why this priority**: Bilingual and bidirectional support is a first-class constitutional
requirement. Retrofitting direction-aware layout and translation after screens exist is far more
expensive than establishing it in the empty shell.

**Independent Test**: Open the shell, switch language, and confirm every visible string changes,
the layout mirrors, formatting follows the locale, and the selection survives a reload.

**Acceptance Scenarios**:

1. **Given** the application is displayed in English, **When** the user switches to Arabic,
   **Then** all shell text changes to Arabic and the layout switches to right-to-left without a
   full page rebuild or loss of the current screen.
2. **Given** the user selected Arabic, **When** they close and reopen the application, **Then**
   Arabic and right-to-left layout are still applied.
3. **Given** a translation key is missing in the active language, **When** the screen renders,
   **Then** a documented fallback is shown and the gap is reported to developers, never an empty
   or broken label.
4. **Given** the active language is Arabic, **When** dates and numbers are displayed, **Then**
   they follow the conventions of the active locale.

---

### User Story 4 - Operate and diagnose the running system (Priority: P3)

An operator or supporting developer investigating a reported failure can take the identifier the
user saw, find every related server-side log entry, and understand what failed - without finding
passwords, tokens, or customer data in the logs.

**Why this priority**: Diagnosability is required before any real traffic exists, but the system
can be assembled and demonstrated before it is fully instrumented.

**Independent Test**: Trigger a deliberate failure through the frontend, capture the identifier
shown to the user, and locate the complete server-side trace of that request from the logs.

**Acceptance Scenarios**:

1. **Given** any request reaches the backend, **When** it is processed, **Then** it carries a
   correlation identifier that appears in every log entry for that request and is returned to the
   caller.
2. **Given** the caller supplies its own correlation identifier, **When** the request is
   processed, **Then** that identifier is reused rather than replaced.
3. **Given** an unexpected error occurs, **When** the response is returned, **Then** the caller
   receives a generic message plus the correlation identifier, and no stack trace or internal
   detail.
4. **Given** a request contains credentials or tokens, **When** it is logged, **Then** those
   values are absent or redacted in the log output.

---

### User Story 5 - Verify quality with a single command (Priority: P3)

A developer or an automated pipeline can build, test, lint, and format-check the whole repository
using documented commands, and gets a clear pass or fail.

**Why this priority**: Quality gates make the constitution enforceable, but they are only useful
once there is code to check.

**Independent Test**: From a clean checkout, run the documented commands for each stack and
confirm each reports success, and that introducing a deliberate lint, format, or test violation
turns the result into a failure.

**Acceptance Scenarios**:

1. **Given** a clean checkout, **When** the documented backend and frontend verification commands
   are run, **Then** build, tests, lint, and format checks all execute and report success.
2. **Given** a deliberately broken test, lint rule, or format violation, **When** the commands
   run, **Then** the result is a failure that names the offending file.
3. **Given** the integration test suite runs, **When** it completes, **Then** it has exercised the
   API through real HTTP handling against a test database and left no residual test data.

---

### Edge Cases

- The database is unreachable at startup, or becomes unreachable while the system is running.
- A required configuration value is missing, empty, or malformed in a non-development environment.
- Two processes attempt to apply pending migrations at the same time.
- A caller requests an API version that does not exist, or omits the version segment entirely.
- A caller sends no credentials, expired credentials, or valid credentials lacking the required
  permission.
- An unhandled exception escapes a request handler.
- The frontend loses network connectivity mid-request, or receives a response that does not match
  the shared error contract.
- A translation key exists in one language but not the other, or a language is requested that the
  system does not support.
- A long or right-to-left value is rendered in a layout designed left-to-right, and vice versa.
- A log statement is written that would otherwise contain a password, token, or personal data.
- The frontend and backend are served from different origins in development.
- A browser request arrives from an origin that is not on the allowlist for the environment.
- A caller sends an oversized, deeply nested, or otherwise abusive payload.
- A caller reaches the API over an insecure connection.

## Requirements *(mandatory)*

### Functional Requirements

#### Repository and structure

- **FR-001**: The repository MUST be a single monorepo containing a clearly separated backend
  workspace, frontend workspace, and repository-level documentation and tooling, each buildable
  independently.
- **FR-002**: The backend MUST be organized into the four constitutional layers (Api,
  Application, Domain, Infrastructure), and an automated check MUST fail when the dependency
  direction is violated or business logic is placed in a controller.
- **FR-003**: The frontend MUST provide the constitutional folder structure (cross-cutting core,
  reusable shared presentation, and per-feature folders), and an automated check MUST fail when
  one feature imports another feature internals. Additionally:
  - The workspace MUST contain exactly one application in this feature.
  - Cross-cutting core and shared presentation code MUST live in workspace libraries consumed
    through stable import paths, not inside the application folder.
  - Adding a second application later (the future external customer portal) MUST require no
    relocation of existing code and no change to the shared libraries themselves.
- **FR-004**: The repository MUST document how a new vertical feature is added on both sides,
  including where each kind of code belongs.

#### Configuration and environments

- **FR-005**: All environment-specific values (database connection, log levels, allowed origins,
  API base URL, default language) MUST come from configuration, and no secret MUST be committed
  to the repository.
- **FR-006**: The system MUST support distinct development and production configuration profiles.
- **FR-007**: In non-development environments, the backend MUST fail fast at startup with a clear
  message naming any missing or invalid required setting.
- **FR-008**: The production configuration strategy MUST target hosting on Windows Server under
  IIS, and MUST be documented. Specifically:
  - Non-secret production settings MUST come from a production settings file that ships with the
    deployment artifact, overridable by machine-level environment variables.
  - Secrets (database credentials, signing keys, integration keys) MUST come from a protected
    store outside source control and outside the published folder, resolved at startup.
  - The backend deployment artifact MUST be a published output that can be dropped into an IIS
    site, and the frontend deployment artifact MUST be static assets that IIS can serve with
    client-side routing fallback.
  - The document MUST list every setting required in production and state which are secret.
- **FR-009**: The frontend MUST resolve the backend base URL and default language from
  environment configuration; no component MUST contain a hard-coded backend address.

#### Database and migrations

- **FR-010**: The backend MUST connect to the relational database using configured credentials,
  and MUST report a connection failure as an unhealthy dependency and a structured log entry
  rather than an unhandled crash.
- **FR-011**: A baseline migration MUST exist, and applying migrations to an empty database MUST
  produce the complete current schema. The baseline MUST create no business tables: this feature
  introduces no persisted business data, so the resulting schema contains only the migration
  history. Its purpose is to prove the migration workflow end to end.
- **FR-012**: Migrations MUST be the only supported mechanism for schema change, MUST be
  re-runnable without error, and the workflow for creating and applying them MUST be documented.
- **FR-013**: Automatic migration on application startup MUST be configurable and MUST be disabled
  by default outside development.
- **FR-014**: A shared traceability convention MUST be available to all future entities, stamping
  created and updated timestamps and actors automatically, together with a documented approach for
  avoiding hard deletion of business records.

#### API conventions

- **FR-015**: Every application endpoint MUST be exposed under the versioned path `/api/v1`, and
  the versioning convention MUST allow a future version to be added without breaking v1 callers.
  Operational probes - health and, in development only, the API documentation - are deliberately
  exempt: they are infrastructure endpoints consumed by hosting and tooling rather than
  application APIs, their lifecycle is independent of the business API, and versioning them would
  break the hosting contract on every version bump. This exemption applies to those endpoints only
  and MUST NOT be extended to any endpoint that carries business data.
- **FR-016**: A request for an unknown or unsupported API version MUST return the shared error
  contract rather than an unhandled or generic framework error.
- **FR-017**: A single error contract MUST be defined and used for every failure response,
  carrying a machine-readable code, a human-readable message, the correlation identifier, and
  optional field-level details.
- **FR-018**: Unexpected exceptions MUST produce a generic client-facing message plus the
  correlation identifier, and MUST NOT expose stack traces, framework details, SQL, or
  configuration values.
- **FR-019**: A shared validation convention MUST validate every incoming request payload before
  any business logic runs, and validation failures MUST return the error contract with one entry
  per offending field.
- **FR-020**: A single pagination contract (page selection in the request, and items plus paging
  metadata in the response) MUST be defined for reuse by every future list endpoint, together with
  consistent filtering and sorting conventions.
- **FR-021**: Public request and response contracts MUST be explicit data-transfer shapes, and an
  automated check MUST fail if a persistence entity is exposed directly by an endpoint.
- **FR-022**: The backend MUST publish machine-readable API documentation for the versioned
  surface in development.

#### Authentication and authorization extension points

- **FR-023**: The backend MUST provide a central authentication extension point that supports two
  caller populations side by side, without modifying controllers when either is implemented:
  - **Staff** (agents, supervisors, administrators) authenticate against the corporate identity
    provider; the foundation MUST provide the seam for validating externally issued identity
    tokens and for mapping the resulting claims onto CRM permissions.
  - **External customer portal users** authenticate against CRM-owned accounts; the foundation
    MUST provide the seam for a CRM-issued credential without assuming staff-only callers
    anywhere.
  - A single endpoint MUST be able to state which populations may reach it, and the resolution of
    the caller MUST be uniform to the rest of the application regardless of which scheme
    authenticated them.
  - No credential storage, login screen, token issuance, or provider configuration is delivered by
    this feature; only the extension points and their tests.
- **FR-024**: The backend MUST provide permission-based authorization primitives: a catalog of
  permission names, a way for each protected operation to declare the permission it requires, and
  enforcement of that declaration. Specifically:
  - The catalog MUST be declared in code and MUST be the single source of truth for which
    permissions exist; no permission table is created by this feature.
  - An endpoint MUST reference a catalog entry rather than a free-text string, so that a
    misspelled or unknown permission is caught before the application runs.
  - The catalog MUST be enumerable at runtime so that the future users-and-permissions feature can
    seed role assignments from it without redefining the list.
- **FR-025**: Access MUST be denied by default; anonymous access MUST be an explicit, reviewable
  opt-in used only by health and diagnostic endpoints in this feature.
- **FR-026**: A request without valid credentials MUST be rejected as unauthenticated, and an
  authenticated request lacking the required permission MUST be rejected as forbidden, both using
  the shared error contract and neither revealing whether the target resource exists.
- **FR-027**: A current-user context abstraction MUST expose the caller identity, which population
  the caller belongs to (staff or external portal user), and organizational scope (department,
  branch, team) so future features can restrict visibility without redesigning the foundation. A
  portal caller MUST be representable without organizational scope, and code MUST NOT assume every
  caller is a staff member.
- **FR-028**: An audit-record extension point MUST exist so that future security-sensitive
  operations can record who did what and when.

#### Frontend infrastructure

- **FR-029**: All backend access MUST go through feature data-access services; an automated check
  MUST fail when a component performs HTTP access directly.
- **FR-030**: Cross-cutting request behavior MUST be centralized once: base address resolution,
  correlation identifier propagation, an attachment point for future credentials, and
  normalization of backend errors into a single client-side error shape.
- **FR-031**: A global error handler MUST distinguish and present network failure, validation
  failure, unauthenticated, forbidden, not found, and server failure, and MUST never surface raw
  technical detail to the user.
- **FR-032**: The shell MUST provide reusable presentation for all six mandated screen states
  (loading, empty, success, validation error, authorization failure, server failure) and MUST
  demonstrate each one. These components MUST live in the shared presentation library and MUST be
  built on the chosen component foundation (Angular Material and the CDK) with a single theme
  definition, so that appearance is changed in one place rather than per screen.
- **FR-033**: The routing foundation MUST support per-feature lazy loading and MUST provide an
  extension point where a future authentication feature can protect routes. The shell layout MUST
  NOT assume a single audience, so that a future external customer portal experience can coexist
  with the staff experience without restructuring routing.
- **FR-034**: The frontend MUST provide a form foundation, based on reactive forms, that binds
  backend field-level validation errors to the corresponding inputs.

#### Localization and direction

- **FR-035**: All user-visible strings MUST come from translation resources; Arabic and English
  resource sets MUST both exist from the outset.
- **FR-036**: Users MUST be able to switch language at runtime without losing their current
  screen, and the selection MUST persist across sessions.
- **FR-037**: Layout direction MUST follow the active language (right-to-left for Arabic,
  left-to-right for English) and MUST be applied globally rather than per screen, using the
  component library bidirectional support rather than per-component direction overrides. Shared
  components MUST use direction-neutral spacing so that no component needs a right-to-left
  special case.
- **FR-038**: Dates, numbers, and other locale-sensitive values MUST be formatted according to the
  active language.
- **FR-039**: A missing translation key MUST render a documented fallback and MUST be reported to
  developers; an automated check MUST report keys present in one language but missing in the other.

#### Logging, correlation, and health

- **FR-040**: The backend MUST emit structured log entries with consistent fields, including
  timestamp, level, message, correlation identifier, request path, and caller identity when known.
  Because production runs under IIS, logs MUST be written to a durable, machine-readable
  destination on the host with a documented rotation and retention policy, and MUST NOT depend on
  console capture.
- **FR-041**: Every request MUST carry a correlation identifier: taken from the inbound request
  when supplied, otherwise generated; returned to the caller; and included in every error response.
- **FR-042**: Passwords, tokens, secrets, and personal customer data MUST never be written to
  logs; a redaction convention MUST exist and MUST be covered by a test.
- **FR-043**: Log verbosity MUST be configurable per environment without a code change.
- **FR-044**: Health endpoints MUST report overall status plus the status of each checked
  dependency, MUST be reachable without credentials, and MUST NOT expose connection strings,
  credentials, or internal topology. Liveness (is the process running) and readiness (are its
  dependencies usable) MUST be separately addressable, so that a dependency outage does not cause
  the host to recycle a healthy process. Any caching applied to a dependency check MUST be short
  enough to satisfy the reporting window in SC-006.

#### Testing and quality gates

- **FR-045**: A backend unit test suite MUST exist and MUST be the documented home for business
  rule tests.
- **FR-046**: A backend integration test suite MUST exist that exercises the API through real
  request handling against a real database. Specifically:
  - The suite MUST provision its own disposable database by starting a container, applying
    migrations to it, and disposing of it when the run ends, leaving nothing behind.
  - The suite MUST NOT depend on a shared, pre-provisioned, or developer-specific database, and
    MUST NOT require manual setup steps before it runs.
  - Concurrent runs on the same machine MUST NOT interfere with each other.
  - When the container runtime is unavailable, the suite MUST fail with a clear message naming
    the missing prerequisite rather than falling back to a substitute database or reporting a
    misleading test failure.
- **FR-047**: Reference tests MUST demonstrate the three mandated backend test kinds: a business
  rule test, an authorization test covering both allowed and denied access, and a validation
  failure test.
- **FR-048**: A frontend test setup MUST exist and MUST demonstrate testing a component and a
  data-access service, including an error-state case.
- **FR-049**: Linting and formatting MUST be configured for both stacks, MUST be runnable as
  documented commands, and MUST fail on violation.
- **FR-050**: The full verification set (build, tests, lint, format) MUST be runnable from a clean
  checkout using documented commands suitable for later use by an automated pipeline.
- **FR-051**: A reference vertical slice MUST exist that exercises the conventions end to end
  using non-business diagnostic data only, and MUST be removable without affecting the foundation.

#### Transport and request hardening

- **FR-052**: The backend MUST require secure transport: insecure requests MUST be redirected or
  rejected, and strict transport security MUST be advertised in non-development environments.
- **FR-053**: Every response MUST carry the baseline security headers, applied centrally rather
  than per endpoint. The baseline set is: `Content-Security-Policy`, `X-Content-Type-Options`,
  `Referrer-Policy`, a frame-embedding restriction (`X-Frame-Options` and CSP `frame-ancestors`),
  and `Permissions-Policy`. Adding to this set is a change to this requirement, not a local
  decision.
- **FR-054**: Cross-origin access MUST be governed by an explicit per-environment allowlist;
  requests from origins that are not listed MUST be refused, and no environment MUST permit an
  unrestricted origin policy.
- **FR-055**: Request size and complexity limits MUST be enforced centrally, and a request that
  exceeds them MUST return the shared error contract rather than an unhandled framework failure.
  The defaults are: request body at most 10 MB, JSON nesting depth at most 32, and any collection
  property in a request at most 500 items. An endpoint may lower a limit for its own payload;
  raising one requires an explicit, reviewed exception.
- **FR-056**: Rate limiting is deliberately excluded from this feature; the foundation MUST NOT
  make it harder to add later, and the exclusion MUST be recorded in the developer documentation
  so the authentication feature picks it up rather than rediscovering it.

#### Accessibility

- **FR-057**: The shared presentation components delivered by this feature - the application
  shell, the language switcher, and the six screen-state components - MUST be operable without a
  mouse and understandable to assistive technology. Specifically: every interactive element MUST
  be reachable and activatable by keyboard in a sensible order, MUST expose an accessible name,
  and MUST show a visible focus indicator; state changes that matter to the user (a screen
  entering the loading or error state) MUST be announced; and text MUST meet WCAG 2.1 AA contrast
  in both language directions. Because every future feature composes these components, an
  accessibility defect here is inherited by every screen in the CRM.

### Authorization Requirements *(mandatory - Constitution Principles IV and V)*

- **AR-001**: The health endpoints (liveness and readiness) MUST be reachable anonymously, and
  MUST expose only status information.
- **AR-002**: Machine-readable API documentation MUST NOT be exposed anonymously outside
  development.
- **AR-003**: Every other endpoint delivered by this feature, including the reference slice, MUST
  require an explicitly declared permission and MUST deny requests that do not carry it.
- **AR-004**: Every endpoint MUST declare which caller populations may reach it. An external
  portal caller MUST NOT be able to reach a staff-only endpoint even when holding a permission of
  the same name.
- **AR-005**: The permission catalog MUST be able to express the permission names named in the
  constitution (for example `customers.view`, `tickets.assign`, `users.manage`) even though no
  business permission is enforced by this feature.
- **AR-006**: The current-user context MUST carry organizational scope so that future features can
  filter records by department, branch, or team; no code delivered by this feature MUST assume a
  single department or branch.
- **AR-007**: Authentication and authorization failures MUST be logged with the correlation
  identifier and the attempted operation, and MUST NOT log the submitted credentials.

### Localization Requirements *(mandatory - Constitution Principle VII)*

- **LR-001**: Every user-visible string introduced by this feature - shell navigation, language
  switcher, the six screen states, and all error messages presented to users - MUST exist in both
  Arabic and English.
- **LR-002**: Layout, alignment, icon direction, and navigation flow MUST mirror correctly under
  right-to-left without per-screen exceptions.
- **LR-003**: Backend error responses MUST be language-neutral: they MUST carry a machine-readable
  code that the frontend maps to a translated message, so that no untranslated server text reaches
  the user.

### Key Entities

This feature persists no business data. The following are conceptual contracts that later
features build on:

- **Error response**: machine-readable code, human-readable message, correlation identifier, and
  optional per-field validation details.
- **Paged result**: the returned items plus the paging metadata needed to request the next page.
- **Permission**: a named, declarable capability that a protected operation requires, declared in
  code and enumerable at runtime; role-to-permission assignment is persisted later by the
  users-and-permissions feature.
- **Current-user context**: caller identity plus organizational scope (department, branch, team).
- **Traceability stamps**: created and updated timestamps and actors available to every future
  entity.
- **Correlation identifier**: the value that ties a client-visible failure to its server-side logs.
- **Health report**: overall status plus per-dependency status.
- **Translation resource**: the Arabic and English key/value sets backing all user-visible text.

The only database content introduced is the migration history that records which schema
migrations have been applied.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A developer with only the documented prerequisites goes from clone to both
  applications running against a working database in under 30 minutes, using the documentation
  alone and asking no questions.
- **SC-002**: Adding a new endpoint plus screen that follows the conventions requires changing no
  more than one shared registration point per side, and requires no new decisions about routing,
  validation, error shape, or pagination.
- **SC-003**: 100% of failure responses produced by the delivered endpoints conform to the single
  error contract, verified by automated tests covering validation, unauthenticated, forbidden,
  not found, and unexpected-error cases.
- **SC-004**: 100% of shell text and layout direction changes when the language is switched, the
  current screen is preserved, and neither language has a missing key. The change is expected to
  be visible within one second; that latency is a design target confirmed by observation, not an
  automated timing assertion, because a wall-clock threshold that small produces flaky tests.
- **SC-005**: Given only the identifier shown to a user when a request fails, an operator can
  retrieve the complete server-side log trail for that request in under 2 minutes.
- **SC-006**: The readiness endpoint reports the database as unhealthy within 30 seconds of it
  becoming unreachable, and healthy again within 30 seconds of it being restored. Dependency-check
  caching is therefore capped at 5 seconds.
- **SC-007**: A full test run produces zero log entries containing passwords, tokens, or other
  secret values, verified by an automated check.
- **SC-008**: 100% of protected endpoints reject anonymous callers, authenticated callers lacking
  the declared permission, and callers from a population the endpoint does not admit, verified by
  automated tests.
- **SC-009**: From a clean checkout, the documented verification commands run build, tests, lint,
  and format checks for both stacks and complete in under 10 minutes on a typical developer
  machine, excluding the one-time download of the test database image.
- **SC-010**: The delivered code contains no customer, ticket, SLA, reporting, or communication
  business rules, confirmed at review.
- **SC-011**: 100% of responses carry the baseline security headers, requests from an origin
  outside the environment allowlist are refused, and an oversized payload returns the shared error
  contract - all verified by automated tests.

## Out of Scope

The following are explicitly excluded from this feature and will each be specified as their own
vertical Spec Kit feature: authentication and login experience; users, roles, and permission
administration; departments, branches, and teams; customers; tickets; SLA and escalation; agent
dashboard; notifications; knowledge base; customer portal; email, WhatsApp, SMS, and live chat
channels; reporting; AI assistance; audit log browsing; and external system integrations.

Also out of scope here: rate limiting and throttling (deferred to the authentication feature,
where login and per-identity policy give it meaning), real-time transport and background job
processing setup, attachment storage implementation, deployment pipelines and environment
provisioning, and any production infrastructure. The foundation MUST NOT preclude any of these, but delivers none of them. The
audit extension point delivered by this feature is a writing surface only; no audit browsing or
retention behavior is included.

## Assumptions

- The technology stack is fixed by the project constitution and by the request (Angular frontend,
  ASP.NET Core Web API backend, SQL Server database, EF Core migrations). This specification
  therefore names those constraints where they are requirements rather than implementation
  choices; all remaining technical decisions belong to the planning phase.
- Localization is applied at runtime so that users can switch language in the running application,
  rather than shipping a separately built bundle per language.
- In development, the frontend and backend run as separate processes on different origins, so
  cross-origin access must be configured for development.
- For running the application in development, a SQL Server instance is available to each developer
  locally or over the network; provisioning it is a documented prerequisite, not part of this
  feature. The integration test suite does not use it - it provisions its own disposable
  containerized database per run.
- A container runtime is available on developer machines and, later, on build agents. It is
  required for the integration test suite only; neither the application nor production hosting
  depends on containers, since production runs on Windows Server under IIS.
- The reference vertical slice uses diagnostic, non-business data only and exists to prove the
  conventions; it is expected to be deleted or replaced once real features exist.
- No continuous integration provider is configured yet, so verification commands must be plain,
  documented commands that a pipeline can later call unchanged.
- Business permission names are registered in the catalog as examples only; no business
  authorization behavior is enforced by this feature.
- Production runs on Windows Server under IIS (confirmed). The foundation therefore assumes
  file-based settings plus machine environment variables, a host-side secret store, published
  folder deployment, and durable on-host log files; it does not assume container orchestration or
  a managed cloud configuration service.
- Two caller populations exist (confirmed): staff authenticated through the corporate identity
  provider, and external customer portal users with CRM-owned accounts. Which specific corporate
  identity provider is used, and how portal credentials are stored, are decided by the future
  authentication feature; this feature only proves that both can plug in.

## Dependencies

- A reachable SQL Server instance for running the application in development.
- A container runtime on every machine that runs the integration test suite, with network access
  to pull the SQL Server image.
- The project constitution v1.0.0, which fixes the architecture, API, authorization, localization,
  logging, and testing rules this feature implements.
- A Windows Server with IIS available as the production target, and a decision by operations on
  which host-side secret store is used; the foundation defines the seam, operations supplies the
  store.
- Confirmation from the identity owners of which corporate identity provider staff will
  authenticate against. Not needed to build this feature, but needed by the future authentication
  feature that fills the seam.
