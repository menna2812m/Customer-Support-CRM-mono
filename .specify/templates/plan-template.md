# Implementation Plan: [FEATURE]

**Branch**: `[###-feature-name]` | **Date**: [DATE] | **Spec**: [link]
**Input**: Feature specification from `/specs/[###-feature-name]/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

[Extract from feature spec: primary requirement + technical approach from research]

## Technical Context

<!--
  ACTION REQUIRED: Replace the content in this section with the technical details
  for the project. The structure here is presented in advisory capacity to guide
  the iteration process.
-->

**Language/Version**: [e.g., Python 3.11, Swift 5.9, Rust 1.75 or NEEDS CLARIFICATION]  
**Primary Dependencies**: [e.g., FastAPI, UIKit, LLVM or NEEDS CLARIFICATION]  
**Storage**: [if applicable, e.g., PostgreSQL, CoreData, files or N/A]  
**Testing**: [e.g., pytest, XCTest, cargo test or NEEDS CLARIFICATION]  
**Target Platform**: [e.g., Linux server, iOS 15+, WASM or NEEDS CLARIFICATION]
**Project Type**: [e.g., library/cli/web-service/mobile-app/compiler/desktop-app or NEEDS CLARIFICATION]  
**Performance Goals**: [domain-specific, e.g., 1000 req/s, 10k lines/sec, 60 fps or NEEDS CLARIFICATION]  
**Constraints**: [domain-specific, e.g., <200ms p95, <100MB memory, offline-capable or NEEDS CLARIFICATION]  
**Scale/Scope**: [domain-specific, e.g., 10k users, 1M LOC, 50 screens or NEEDS CLARIFICATION]

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Mark each gate PASS / FAIL / N/A with a one-line justification. Every FAIL must be recorded in
Complexity Tracking below, otherwise the plan does not proceed to implementation.

| # | Gate (constitution principle) | Status |
|---|-------------------------------|--------|
| I | Business logic sits in Domain/Application, not controllers; no frontend feature reaches into another feature internals | |
| II | EF Core + SQL Server; every schema change ships as an EF Core migration | |
| III | Endpoints under `/api/v1`, explicit DTOs (no entities returned), shared pagination/filter/sort contract | |
| IV | Every protected operation declares its required permission; authorization enforced server-side; audit records for security-sensitive actions | |
| V | No single-department or single-branch assumption; organizational visibility scoping considered | |
| VI | Angular standalone APIs; `core/ shared/ features/` placement; HTTP only in data-access services; Reactive Forms for non-trivial forms | |
| VII | Arabic RTL and English LTR both handled; no hard-coded user-visible strings | |
| VIII | Keys, FKs, unique constraints, indexes; audit columns; no hard delete of traceable business records | |
| IX | Status changes, assignments, escalations recorded; history never overwritten | |
| X | Consistent error contract; no stack traces to clients; all six Angular UI states handled | |
| XI | Structured logging with correlation id; no secrets or sensitive customer data logged | |
| XII | Attachments go through the storage abstraction; allowed types, sizes, and authorization specified | |
| XIII | Tests cover business rules, authorization, and validation failures; critical Angular workflows tested | |
| XIV | Core workflows still function when the AI provider is unavailable; AI output labeled and user-accepted | |
| XV | External vendors sit behind adapters; retry and idempotency defined; failures cannot corrupt CRM state | |

## Project Structure

### Documentation (this feature)

```text
specs/[###-feature]/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)
<!--
  ACTION REQUIRED: Trim the tree below to the projects and folders this feature
  actually touches and expand it with real paths (concrete feature folder names,
  entities, endpoints). The top-level layout is fixed by Constitution Principles I
  and VI - do not substitute a different structure.
-->

```text
backend/
├── src/
│   ├── Crm.Api/               # controllers, filters, DI wiring (HTTP concerns only)
│   ├── Crm.Application/       # use cases, DTOs, validators, abstractions
│   ├── Crm.Domain/            # entities, value objects, domain rules
│   └── Crm.Infrastructure/    # EF Core, migrations, storage, integrations
└── tests/
    ├── Crm.UnitTests/
    └── Crm.IntegrationTests/

frontend/
└── src/app/
    ├── core/                  # cross-cutting services (auth, http, i18n, errors)
    ├── shared/                # reusable presentation components
    └── features/<feature>/    # pages, components, data-access for this feature
```

**Structure Decision**: [Name the projects, feature folders, and migration this plan
adds or changes, referencing the real directories captured above]

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| [e.g., 4th project] | [current need] | [why 3 projects insufficient] |
| [e.g., Repository pattern] | [specific problem] | [why direct DB access insufficient] |
