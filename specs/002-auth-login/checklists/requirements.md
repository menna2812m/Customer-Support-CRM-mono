# Specification Quality Checklist: Authentication and Login

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-08-26
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation details (languages, frameworks, APIs)
- [x] Focused on user value and business needs
- [x] Written for non-technical stakeholders
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous
- [x] Success criteria are measurable
- [x] Success criteria are technology-agnostic (no implementation details)
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified
- [x] Scope is clearly bounded
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into specification

## Notes

- **Resolved 2026-08-26**: all three clarifications were answered before the first validation pass
  and are recorded in the spec's Clarifications section. Two of them narrowed the feature
  substantially:
  - **Portal deferred** removed two user stories and eleven requirements (portal accounts, password
    storage and policy, reset, activation, per-account lockout, and the timing-attack requirement
    that only mattered because the CRM would have held passwords). Staff authenticate through the
    provider, so the CRM never sees a credential to protect.
  - **Generic OIDC** turned the provider from a scope question into a configuration value (FR-002),
    with the residual risk stated plainly in Assumptions rather than hidden.
  - **Role store in this feature** added five requirements and one user story, including the
    bootstrap administrator (FR-020, SC-007) - without it, a fresh deployment authenticates
    successfully and then admits nobody to anything.
- **Scope shrank, and that is recorded, not smoothed over**: 36 functional requirements against 39
  in the first draft, but covering less ground with more depth. The Out of Scope section names what
  the portal will need and confirms this feature builds sessions, rate limiting, and audit to serve
  both populations, so nothing has to be undone later.
- Session and rate-limit numbers sit in Assumptions rather than requirements, so planning can adjust
  them without a specification amendment; the requirements state that the limits exist and are
  enforced.
- **Clarification session 2026-08-26**: five further questions asked and answered, taking the
  specification from 36 to 41 functional requirements. Two closed gaps that would have shipped as
  quiet defects: sign-out ended only the CRM session, so a shared workstation would have restored
  the previous person on the next sign-in (FR-014); and nothing said what happens when a reissued
  email address arrives with a new provider subject, where the permissive reading hands a new hire
  a leaver's roles (FR-005). The others fixed the credential-delivery hedge in FR-014, gave ordinary
  staff a route to a role before the administration screens exist (FR-023), and defined where
  organizational placement comes from (FR-025) - without which `ICurrentUser.Scope` would have been
  empty forever while the spec claimed otherwise.
- Items marked incomplete require spec updates before `/speckit.clarify` or `/speckit.plan`.

## Definition of Done, confirmed against the delivered code (2026-08-27, task T098)

Constitution section 17, item by item. Full evidence is in [compliance.md](../compliance.md); this
is the tick list.

- [x] Specification requirements are implemented
- [x] Backend validation exists
- [x] Backend authorization exists where required
- [x] A database migration exists where required
- [x] Angular loading, empty, success, validation error, authorization failure, and server failure
      states are handled
- [x] Arabic and English behavior has been considered, including RTL layout
- [x] Tests for critical rules pass - 145 backend and 66 frontend, both gates green
- [x] Errors follow application conventions
- [x] Logging follows security requirements
- [x] Relevant documentation is updated

One item is deliberately **not** ticked anywhere, because it was not done: task T096, executing
`quickstart.md` against a real identity-provider container. What that leaves unverified is
provider-specific claim naming and the exact redirect-URI match - both configuration, both named in
the quickstart's "easy to get wrong" table. Recorded in compliance.md under Outstanding rather than
absorbed into a tick above.
