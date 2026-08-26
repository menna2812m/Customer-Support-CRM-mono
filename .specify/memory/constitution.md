<!--
SYNC IMPACT REPORT
==================
Version change: (template, unversioned) -> 1.0.0
Rationale: Initial ratification. The file previously contained only unfilled
template placeholders; this is the first concrete adoption of the constitution.

Principles defined (all new):
  - I. Layered Modular Monolith
  - II. Backend Technology Baseline
  - III. API Contract Standards
  - IV. Backend-Enforced Authentication & Authorization
  - V. Multi-Level Organization Model
  - VI. Feature-First Angular Architecture
  - VII. Arabic/English as First-Class Languages
  - VIII. Relational Integrity & Traceability
  - IX. Preserved Customer & Ticket History
  - X. Consistent Error Handling
  - XI. Structured Logging & Safe Observability
  - XII. Abstracted File Handling
  - XIII. Behavior-Focused Testing
  - XIV. AI as an Optional Capability
  - XV. Integrations Behind Adapters

Sections added:
  - Feature Development with Spec Kit
  - Definition of Done
  - Governance

Sections removed: none (placeholder sections SECTION_2 / SECTION_3 replaced).

Templates and artifacts:
  UPDATED .specify/templates/plan-template.md - Constitution Check gates filled;
    source layout replaced with the actual monorepo structure.
  UPDATED .specify/templates/spec-template.md - Added Authorization & Permissions
    and Localization requirement subsections.
  UPDATED .specify/templates/tasks-template.md - Added principle-driven task
    categories (migrations, authorization, Angular UI states, i18n, logging, audit).
  REVIEWED .specify/templates/checklist-template.md - no changes required.
  PENDING README.md / docs/quickstart.md - not present in the repository yet;
    create them referencing this constitution when the solution is scaffolded.

Deferred TODOs: none.
-->

# Customer Support CRM Constitution

## Core Principles

### I. Layered Modular Monolith

The system MUST be a monorepo containing an Angular frontend and an ASP.NET Core Web API
backend. The backend MUST follow a modular monolith architecture organized into `Crm.Api`,
`Crm.Application`, `Crm.Domain`, and `Crm.Infrastructure`.

- Business logic MUST NOT be implemented inside API controllers.
- Controllers MUST handle only HTTP concerns: request binding, authorization orchestration,
  and response mapping.
- Business rules MUST live in the Domain or Application layer.
- Infrastructure concerns (database access, external APIs, file storage, messaging, caching)
  MUST remain outside the Domain layer.
- The frontend MUST use a feature-first architecture, and a feature MUST NOT depend on the
  internal implementation of another feature.

Rationale: layer boundaries are what keep a growing CRM changeable; once rules leak into
controllers or vendor code leaks into the domain, every later feature pays the cost.

### II. Backend Technology Baseline

- The backend MUST use ASP.NET Core Web API and MUST expose REST APIs. Razor Pages and MVC
  Views MUST NOT be used for application UI.
- Entity Framework Core MUST be used for relational database access, with SQL Server as the
  primary relational database.
- Database schema changes MUST be managed through EF Core migrations.
- SignalR MAY be used for real-time functionality (live notifications, live chat, ticket
  updates, agent presence).
- Hangfire MAY be used for background processing (SLA timers, escalation, scheduled
  notifications, retries, integration processing).

### III. API Contract Standards

- All public application APIs MUST be versioned under `/api/v1`.
- Endpoints MUST follow REST-oriented conventions where appropriate, e.g.
  `GET /api/v1/customers`, `POST /api/v1/customers`, `GET /api/v1/customers/{id}`,
  `PATCH /api/v1/customers/{id}`.
- Request and response contracts MUST use explicit DTOs; database entities MUST NOT be
  returned directly from controllers.
- All incoming request data MUST be validated, and validation failures MUST return the
  system's consistent API error structure.
- Pagination MUST follow one shared contract throughout the system, and filtering and sorting
  conventions MUST be consistent across resource endpoints.

### IV. Backend-Enforced Authentication and Authorization

- Authentication MUST be implemented centrally.
- Authorization MUST always be enforced by the backend. Frontend role or permission checks
  only shape the user experience and MUST NOT be treated as security enforcement.
- The system MUST support both role-based and permission-based authorization.
- Every protected operation MUST explicitly declare its required permissions, for example:
  `customers.view`, `customers.create`, `customers.update`, `tickets.view`, `tickets.create`,
  `tickets.assign`, `tickets.escalate`, `users.manage`, `reports.view`.
- Security-sensitive operations MUST generate audit records where applicable.

### V. Multi-Level Organization Model

The architecture MUST support multiple departments, branches, teams, and agents.
Authorization and data visibility MUST be capable of being scoped by organizational
structure. Business logic MUST NOT assume a single department or a single branch.

Rationale: single-scope assumptions are cheap to make and expensive to unwind; organizational
scoping must exist in the model from the first feature.

### VI. Feature-First Angular Architecture

- The frontend MUST use Angular standalone APIs.
- Feature-specific code MUST live under `src/app/features/<feature-name>`, cross-cutting
  application services under `src/app/core`, and reusable presentation components under
  `src/app/shared`.
- Angular components MUST NOT call `HttpClient` directly; HTTP access MUST be encapsulated in
  feature data-access services.
- Reactive Forms MUST be used for non-trivial forms.
- Angular Signals SHOULD be preferred for local UI state where appropriate.
- RxJS MUST be used where stream-based asynchronous behavior is appropriate.

### VII. Arabic and English as First-Class Languages

Arabic and English MUST be first-class supported languages from the beginning of development.
The frontend MUST support Arabic RTL layout and English LTR layout. UI implementations MUST
NOT assume LTR layout, and user-visible strings MUST NOT be hard-coded where translation is
required.

### VIII. Relational Integrity and Traceability

- Relational integrity MUST be enforced using primary keys, foreign keys, unique constraints,
  and indexes where appropriate.
- Business-critical history MUST NOT be silently destroyed.
- Entities requiring traceability SHOULD carry `CreatedAt`, `CreatedBy`, `UpdatedAt`, and
  `UpdatedBy`.
- Hard deletion MUST be avoided for business records when historical traceability is required.

### IX. Preserved Customer and Ticket History

Customer interaction history MUST be traceable. Ticket status changes, assignments,
escalations, and other significant actions MUST be recorded where required by the feature
specification. Historical records MUST NOT be overwritten solely for UI convenience.

### X. Consistent Error Handling

- The backend MUST expose a consistent error contract.
- Unexpected exceptions MUST NOT expose stack traces or sensitive implementation details to
  clients.
- Frontend features MUST explicitly handle loading, empty, success, validation error,
  authorization failure, and server failure states.

### XI. Structured Logging and Safe Observability

- The backend MUST use structured logging. Serilog SHOULD be used unless a later architecture
  decision replaces it.
- Requests SHOULD carry correlation identifiers.
- Important operations MUST log enough context to diagnose failures.
- Passwords, tokens, secrets, and sensitive customer data MUST NOT be written to application
  logs.

### XII. Abstracted File Handling

Attachments MUST be accessed through an abstraction rather than direct feature-specific
filesystem access. File metadata MUST be stored separately from raw file content where
appropriate. Allowed file types, maximum sizes, authorization, and malware/security
requirements MUST be defined by the relevant feature specification.

### XIII. Behavior-Focused Testing

- Every backend feature MUST include automated tests for its important business rules.
- Critical API workflows SHOULD include integration tests.
- Authorization rules MUST be tested.
- Validation failure scenarios MUST be tested.
- Critical Angular workflows MUST include frontend tests.
- Tests MUST focus on business behavior rather than implementation details.

### XIV. AI as an Optional Capability

- AI MUST remain an optional supporting capability; core CRM functionality MUST NOT depend on
  an AI provider being available.
- Customers and agents MUST still be able to perform normal ticket workflows when AI services
  are unavailable.
- AI-generated or AI-suggested content MUST be clearly identifiable.
- Where AI modifies or proposes business data, explicit user acceptance MUST be required
  unless a feature specification explicitly defines an approved automatic workflow.
- AI integration MUST remain isolated behind an application abstraction so providers can
  change without rewriting CRM business logic.

### XV. Integrations Behind Adapters

- External integrations MUST be implemented behind interfaces or adapters (WhatsApp, SMS,
  email, ERP, AI providers, external customer systems).
- CRM domain logic MUST NOT depend directly on vendor SDKs.
- Integration failures MUST NOT corrupt internal CRM state.
- Retry and idempotency requirements MUST be defined for integration workflows where
  appropriate.

## Feature Development with Spec Kit

Each business capability MUST be implemented as its own Spec Kit feature, and a feature
specification MUST represent a complete vertical business slice where practical.

A feature MAY include domain changes, database changes, backend services, API endpoints,
Angular pages, Angular components, authorization, and tests.

Frontend and backend MUST NOT normally be split into separate specifications for the same
business feature.

Every feature MUST follow this order: Specify, Clarify, Plan, Checklist, Tasks, Analyze,
Implement.

Implementation MUST NOT begin until important ambiguities discovered during clarification
have been resolved.

## Definition of Done

A feature is complete only when all of the following hold:

- Specification requirements are implemented.
- Backend validation exists.
- Backend authorization exists where required.
- A database migration exists where required.
- Angular loading, empty, success, validation error, authorization failure, and server failure
  states are handled.
- Arabic and English behavior has been considered, including RTL layout.
- Tests for critical rules pass.
- Errors follow application conventions.
- Logging follows security requirements.
- Relevant documentation is updated.

## Governance

This constitution supersedes other development practices for this repository. Where a
practice, template, or generated plan conflicts with it, this document wins.

**Amendment procedure**: amendments MUST be proposed as a change to
`.specify/memory/constitution.md`, MUST state their rationale, and MUST include a Sync Impact
Report listing every affected template or document. Dependent artifacts
(`.specify/templates/plan-template.md`, `spec-template.md`, `tasks-template.md`,
`checklist-template.md`, and runtime guidance docs) MUST be updated in the same change or
explicitly flagged as pending.

**Versioning policy**: semantic versioning applies to this document.

- MAJOR: backward-incompatible governance changes, or removal/redefinition of a principle.
- MINOR: a new principle or section, or materially expanded guidance.
- PATCH: clarifications, wording, and non-semantic refinements.

**Compliance review**: every plan MUST pass the Constitution Check gate before Phase 0
research and again after Phase 1 design. Any violation MUST be recorded in the plan's
Complexity Tracking table with its justification and the rejected simpler alternative;
unjustified violations block implementation. Code review MUST verify compliance with the
principles a change touches.

**Version**: 1.0.0 | **Ratified**: 2026-08-26 | **Last Amended**: 2026-08-26
