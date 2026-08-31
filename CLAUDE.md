# Customer-Support-CRM-mono Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-08-30

## Active Technologies
- **Organization** (003-organization): no new dependency in either stack. Three tables -
  `Department`, `Branch`, `Team` - plus the placement foreign keys feature 002 deferred
- **Identity administration** (004-identity-administration): no new dependency and no new table.
  `User` gains a nullable `ProviderSubject` and a `Provider`; two plain unique indexes become
  filtered ones

- **Backend**: C# 14 / .NET 10 (SDK pinned to 10.0.400 via `global.json`), ASP.NET Core 10,
  EF Core 10 (SQL Server provider), Asp.Versioning.Mvc, FluentValidation, Serilog (001-project-foundation)
- **Frontend**: TypeScript / Angular 22 (CLI 22.1.6, Node 22.x), Angular Material + CDK,
  @jsverse/transloco, RxJS, Signals (001-project-foundation)
- **Database**: SQL Server - local instance for development, disposable Testcontainers instance for
  integration tests (001-project-foundation)
- **Testing**: xUnit v3 + Shouldly + NSubstitute, WebApplicationFactory + Testcontainers.MsSql,
  NetArchTest.Rules, Vitest via the Angular unit-test builder (001-project-foundation)
- **Hosting**: Windows Server + IIS in production; Kestrel + `ng serve` in development
- **Authentication** (002-auth-login): server-side OIDC handshake, CRM-issued access credentials,
  server-side sessions with rotating renewal credentials, role-to-permission store, framework rate
  limiting

## Project Structure

```text
backend/src/{Crm.Api,Crm.Application,Crm.Domain,Crm.Infrastructure}
backend/tests/{Crm.UnitTests,Crm.IntegrationTests,Crm.ArchitectureTests}
frontend/projects/{crm-web,core,ui}     # one app + @crm/core + @crm/ui libraries
specs/                                  # Spec Kit features
docs/  scripts/
```

## Commands

```powershell
dotnet run --project backend/src/Crm.Api          # API on https://localhost:7283
npm --prefix frontend start                       # frontend on http://localhost:4200
./scripts/verify-backend.ps1                      # build + 281 tests + format + publish (~2m)
./scripts/verify-frontend.ps1                     # lint + format + i18n + css + 115 tests + build (~170s)
npm --prefix frontend run i18n:check              # ar/en translation key parity
npm --prefix frontend run css:check               # no physical direction properties
dotnet ef migrations add <Name> --project backend/src/Crm.Infrastructure --startup-project backend/src/Crm.Api
```

The integration suite needs Docker running: it provisions its own SQL Server container.

## Code Style

Constitution v1.0.0 (`.specify/memory/constitution.md`) is binding. Highlights:

- No business logic in controllers; rules live in Domain/Application. Domain references nothing.
- Endpoints under `/api/v1`, explicit DTOs only, one ProblemDetails error contract, one pagination
  contract. Never return EF entities.
- Deny by default: every endpoint declares `[RequirePermission(...)]` and its admitted caller
  population (Staff / Portal). `[AllowAnonymous]` is explicit and rare.
- Angular: standalone APIs, `HttpClient` only inside `*-api.service.ts`, no cross-feature imports,
  reactive forms, all six UI states handled.
- Arabic and English together, always. Logical CSS properties only - no `left`/`right`.
- Structured logs with a correlation id; never log secrets or customer data.
- Tests are required for business rules, authorization, and validation failures.

## Recent Changes
- 004-identity-administration: people administration and placement; pre-provisioning by email with
  a verified-email claim rule; sign-in becomes match-then-create
- 003-organization: departments, branches, and teams as manageable entities; the product's first
  collection endpoints; provider-asserted placement retired in favour of CRM-owned placement
- 002-auth-login: Added C# 14 on .NET 10 (SDK 10.0.400); TypeScript on Angular 22 - unchanged from + Microsoft.AspNetCore.Authentication.OpenIdConnect (sign-in handshake

- 001-project-foundation: monorepo, four-layer backend, Angular workspace with `@crm/core` and
  `@crm/ui`, EF Core migrations, API conventions, auth extension points, i18n/RTL, logging and
  health, transport hardening, test and lint gates

<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->
