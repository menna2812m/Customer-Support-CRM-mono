# Customer Support CRM

A bilingual (Arabic / English) customer support CRM: an Angular frontend and an ASP.NET Core
modular monolith backend over SQL Server, in one repository.

This repository currently contains the **project foundation** - the platform every business
feature will be built on. It deliberately contains no customer, ticket, SLA, reporting, or
communication functionality yet.

## Quick start

```powershell
dotnet restore backend/Crm.sln
npm --prefix frontend ci

# configure the development database (secrets never live in a committed file)
dotnet user-secrets --project backend/src/Crm.Api set "Database:ConnectionString" "Server=localhost;Database=CrmDev;Trusted_Connection=True;TrustServerCertificate=True"
dotnet ef database update --project backend/src/Crm.Infrastructure --startup-project backend/src/Crm.Api

dotnet run --project backend/src/Crm.Api     # https://localhost:7283
npm --prefix frontend start                  # http://localhost:4200
```

Full walkthrough: [docs/getting-started.md](docs/getting-started.md).

## Verification

Two commands, no arguments, callable unchanged by a pipeline:

```powershell
./scripts/verify-backend.ps1     # build (warnings as errors), format, unit + integration + architecture tests, publish
./scripts/verify-frontend.ps1    # lint, format, translation parity, direction-neutral styles, tests, build
```

The integration suite starts its own SQL Server container, so Docker must be running. See
[docs/testing.md](docs/testing.md).

## Layout

```text
backend/src/       Crm.Api, Crm.Application, Crm.Domain, Crm.Infrastructure
backend/tests/     unit, integration, architecture
frontend/projects/ crm-web (application), core (@crm/core), ui (@crm/ui)
docs/              getting started, conventions, production configuration, testing
scripts/           verification entry points and repository checks
specs/             Spec Kit features
.specify/memory/   the project constitution
```

## How work is done here

Every business capability is a Spec Kit feature and follows Specify → Clarify → Plan → Checklist →
Tasks → Analyze → Implement. The rules that bind all of them live in
[`.specify/memory/constitution.md`](.specify/memory/constitution.md); the practical how-to is in
[docs/conventions.md](docs/conventions.md).

Three of those rules explain most of the code you will read:

- **Business logic never sits in a controller**, and the Domain layer references nothing.
  Architecture tests fail the build if that slips.
- **Authorization is always enforced by the backend.** Every endpoint declares the permission it
  requires and the caller populations it admits; access is denied by default.
- **Arabic and English are both first class**, from the first screen. Layout uses logical CSS
  properties so right-to-left needs no per-screen exception.

## Reference slice

`/api/v1/diagnostics` and the matching Angular feature are a deliberately removable worked example
of every convention. Delete them once the first real feature exists - the procedure is in
[docs/conventions.md](docs/conventions.md).
