# Quickstart: Project Foundation

**Feature**: 001-project-foundation | **Date**: 2026-08-26

This is the walkthrough the foundation must satisfy, written as the developer will experience it.
It doubles as the acceptance script for User Story 1 (SC-001: clone to running in under 30
minutes) and User Story 5. **Nothing below exists yet** - this document is the target that
implementation is measured against, and it becomes `docs/getting-started.md` when the work lands.

## Prerequisites

| Tool | Version | Needed for | Verified on this machine |
|------|---------|------------|--------------------------|
| .NET SDK | 10.0.400 (pinned by `global.json`) | Backend build and EF tooling | 10.0.400 present |
| Node.js | 22.x LTS | Frontend build | 22.22.3 present |
| npm | 10.x | Frontend packages | 10.9.8 present |
| Angular CLI | 22.x | Frontend tooling | 22.1.6 present |
| SQL Server | 2019 or later, any edition including Developer/Express | Running the app locally | `sqlcmd` present |
| Docker | Any recent version, Linux containers enabled | Integration tests only - not the app, not production | 29.7.2 present |

Install the EF Core tools once: `dotnet tool install --global dotnet-ef`.

## 1. Clone and restore

```powershell
git clone <repository-url> Customer-Support-CRM-mono
cd Customer-Support-CRM-mono
dotnet restore backend/Crm.sln
npm --prefix frontend ci
```

## 2. Configure the database connection

Development secrets never live in a committed file:

```powershell
cd backend/src/Crm.Api
dotnet user-secrets set "ConnectionStrings:Crm" "Server=localhost;Database=CrmDev;Trusted_Connection=True;TrustServerCertificate=True"
cd ../../..
```

Everything non-secret (log levels, CORS allowlist, feature switches) is already in
`appsettings.Development.json` and needs no edit.

## 3. Create the database

```powershell
dotnet ef database update --project backend/src/Crm.Infrastructure --startup-project backend/src/Crm.Api
```

The baseline migration creates the migration history table and nothing else - this feature
introduces no business tables by design.

## 4. Run the backend

```powershell
dotnet run --project backend/src/Crm.Api
```

Then confirm:

- `https://localhost:7001/health/live` returns `Healthy`.
- `https://localhost:7001/health/ready` returns `Healthy` with a `database` check listed.
- `https://localhost:7001/scalar` renders the API documentation (development only).
- Every response carries an `X-Correlation-Id` header.

If a required setting is missing, startup stops immediately and names the setting. That is intended
behavior, not a bug.

## 5. Run the frontend

```powershell
npm --prefix frontend start
```

Open `http://localhost:4200` and confirm:

- The shell loads and reaches the API (the diagnostics page lists items).
- The language switcher flips the entire UI between Arabic and English, and the layout mirrors
  between RTL and LTR.
- The selected language survives a page reload.
- The diagnostics page can demonstrate each of the six states: loading, empty, success, validation
  error, authorization failure, and server failure.

## 6. Run the verification suites

```powershell
./scripts/verify-backend.ps1      # restore, build, unit + integration + architecture tests, format check
./scripts/verify-frontend.ps1     # install, lint, unit tests, format check, translation key parity
```

The integration suite starts its own SQL Server container, applies migrations to a uniquely named
database, and disposes of it at the end - it never touches `CrmDev`. The first run downloads the
image; subsequent runs are inside the 10-minute budget. If Docker is not running, the suite fails
with a message naming Docker rather than a misleading test failure.

## 7. Add your first real feature

1. Create the branch and specification with `/speckit.specify`.
2. Backend: entity and rules in `Crm.Domain`, use case, DTOs and validator in `Crm.Application`,
   EF configuration and migration in `Crm.Infrastructure`, a thin controller in `Crm.Api` that
   declares its permission with `[RequirePermission(...)]` and its admitted populations.
3. Frontend: a folder under `projects/crm-web/src/app/features/<feature>`, with a
   `<feature>-api.service.ts` for HTTP, a page component using `StateContainerComponent`, and
   `ar`/`en` translation keys added together.
4. Tests: a business rule test, an authorization test covering allowed and denied, a validation
   failure test, and a frontend component plus data-access test.
5. Run both verification scripts before opening the pull request.

The reference diagnostics slice is the worked example of all of the above. Once your feature
exists, that slice can be deleted in a single commit.

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| Startup stops naming a setting | A required configuration value is missing | Set it via user-secrets (development) or the environment (elsewhere) |
| `/health/ready` reports the database unhealthy | SQL Server unreachable or the database does not exist | Check the instance is running, then re-run step 3 |
| Integration tests fail immediately with a Docker message | Container runtime not running | Start Docker; the tests never fall back to another database on purpose |
| Frontend loads but every call fails | API not running, or the origin is not on the development CORS allowlist | Start the API; check the allowlist in `appsettings.Development.json` |
| A screen shows an untranslated key | Key added to one language only | Add it to both `ar.json` and `en.json`; the parity check catches this before review |
