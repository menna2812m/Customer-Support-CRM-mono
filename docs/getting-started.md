# Getting Started

Local setup for the Customer Support CRM monorepo. Every step below was executed on a Windows
development machine while the foundation was built, except where a note says otherwise.

## Prerequisites

| Tool | Version | Needed for |
|------|---------|------------|
| .NET SDK | 10.0.400 (pinned by `global.json`) | Backend build, EF tooling |
| Node.js | 22.x LTS | Frontend build |
| Angular CLI | 22.x | Frontend tooling |
| SQL Server | 2019 or later (Developer or Express is fine) | Running the application locally |
| Docker | Any current version, Linux containers | Integration tests only |

Install the EF Core tools once:

```powershell
dotnet tool install --global dotnet-ef
```

## 1. Clone and restore

```powershell
git clone <repository-url> Customer-Support-CRM-mono
cd Customer-Support-CRM-mono
dotnet restore backend/Crm.sln
npm --prefix frontend ci
```

## 2. Configure the database connection

Development secrets never live in a committed file - they go into user-secrets:

```powershell
cd backend/src/Crm.Api
dotnet user-secrets init
dotnet user-secrets set "Database:ConnectionString" "Server=localhost;Database=CrmDev;Trusted_Connection=True;TrustServerCertificate=True"
cd ../../..
```

Everything non-secret (log levels, the CORS allowlist, the correlation header) is already in
`appsettings.Development.json` and needs no edit.

If a required setting is missing, startup stops immediately and names it. That is intended
behaviour, not a bug - see `Crm.Api/Configuration/CrmConfiguration.cs`.

## 3. Create the database

```powershell
dotnet ef database update --project backend/src/Crm.Infrastructure --startup-project backend/src/Crm.Api
```

The baseline migration creates the migration history table and nothing else: this feature
introduces no business tables by design.

In Development, `Database:AutoMigrateOnStartup` defaults to `true`, so simply running the API also
applies pending migrations. It is forced off in every other environment.

## 4. Run the backend

```powershell
dotnet run --project backend/src/Crm.Api
```

Default addresses are `https://localhost:7283` and `http://localhost:5233` (see
`Properties/launchSettings.json`). Confirm:

- `GET /health/live` returns `{"status":"Healthy",...}`.
- `GET /health/ready` returns `Healthy` with a `database` check listed. If the database is
  unreachable, it returns HTTP 503 and `Unhealthy` **without** exposing the server name,
  credentials, or exception text - and the application keeps running so it can tell you that.
- Every response carries an `X-Correlation-Id` header. Send your own and it is reused rather than
  replaced.
- An unknown path returns the shared error contract, for example:

  ```json
  {
    "type": "...",
    "title": "The request could not be completed.",
    "status": 404,
    "instance": "/api/v1/nope",
    "code": "not_found",
    "correlationId": "7f3e660c30377d593d3df4b5a09000c5"
  }
  ```

## 5. Run the frontend

```powershell
npm --prefix frontend start
```

Open `http://localhost:4200`. The home screen calls the API and renders the platform status.

The API address comes from `frontend/projects/crm-web/public/assets/config.json`, which is read at
runtime - no rebuild is needed to point a deployed bundle at a different environment, and no
component contains a backend address. The development origin `http://localhost:4200` is on the
CORS allowlist in `appsettings.Development.json`; an origin that is not on the list receives no
CORS headers and is blocked by the browser.

## 6. Sign in

Authentication is off by default (`Authentication:Staff:Enabled` is `false`), so the application
runs and every protected endpoint answers 401. That is the correct state for someone who only
wants to build a screen. To sign in for real you need an identity provider.

Any conforming OpenID Connect provider works. A local Keycloak container is the quickest:

```powershell
docker run -p 8080:8080 -e KC_BOOTSTRAP_ADMIN_USERNAME=admin -e KC_BOOTSTRAP_ADMIN_PASSWORD=admin `
  quay.io/keycloak/keycloak:latest start-dev
```

Create a realm called `crm`, a confidential client called `crm-api` with the redirect URI
`https://localhost:7283/api/v1/auth/callback`, and one user with an email address. Then:

```powershell
cd backend/src/Crm.Api
dotnet user-secrets set "Authentication:Staff:Enabled" "true"
dotnet user-secrets set "Authentication:Staff:ClientSecret" "<the client secret>"
dotnet user-secrets set "Token:SigningKey" "<at least 32 random characters>"
dotnet user-secrets set "Identity:BootstrapAdministrator" "<your email address>"
cd ../../..
```

`Authority` and `ClientId` are already in `appsettings.Development.json` pointing at the container
above; the three values here are secrets or personal, so they belong in user-secrets.

`Identity:BootstrapAdministrator` is what stops a fresh database from being a locked door: the first
person to sign in matching that subject or email is granted the `Administrator` role, and the grant
is written to the authentication event trail. It grants nothing to somebody who already holds a
role, so it cannot restore access that was deliberately removed. Clear it once real administrators
exist. `Identity:DefaultRole` is set to `Agent` in development, so anybody else who signs in can do
day-to-day work; in production it defaults to nothing, and new staff arrive with no access until
somebody grants it.

`specs/002-auth-login/quickstart.md` walks the same setup in more detail, with a table of the
mistakes that are easy to make.

## 7. Build an organization

Sign in as a user holding the `Administrator` role, then open **Organization**. Nothing else
grants `organization.manage`, so an `Agent` sees no such entry and is refused the routes directly.

1. **Create a branch** under Organization → Branches: Riyadh / الرياض, code `RUH`. Both names are
   required on the same form - a unit created in one language and completed in the other later is
   the half-translated state the product exists to avoid.
2. **Create a department**, Technical Support / الدعم الفني, code `TS`.
3. **Add teams from inside the department.** Open it and add Tier 1 and Tier 2. The department is
   not a dropdown; it comes from the context you are in, which is why a team can never be created
   without one.
4. **Create a second department** with a team also called Tier 1. This is allowed: team names are
   unique only within their own department.
5. **Switch to Arabic.** The lists re-sort by the Arabic name, and the team hierarchy indents from
   the other side.

The code is set once and cannot be changed afterwards. A unit created with the wrong code is
deleted and recreated, which frees the code again.

Nobody can be *placed* in any of this yet - that is the users-and-permissions feature. Until then
the structure is real and unused.

`specs/003-organization/quickstart.md` lists the rules worth trying to break, and the SQL that
proves a team move carried its members.

## 8. Run the verification suites

```powershell
./scripts/verify-backend.ps1      # restore, build, tests, format check
./scripts/verify-frontend.ps1     # install, lint, tests, format check
```

The integration suite starts its own SQL Server container, applies migrations to a uniquely named
database, and disposes of it at the end - it never touches `CrmDev`. The first run downloads the
image (about 2.3 GB); later runs take about 20 seconds. If Docker is not running, the suite fails
with a message naming Docker rather than a misleading test failure.

## 9. Add your first feature

1. Create the branch and specification with `/speckit.specify`.
2. Backend: entity and rules in `Crm.Domain`, use case, DTOs and validator in `Crm.Application`,
   EF configuration and migration in `Crm.Infrastructure`, a thin controller in `Crm.Api`.
3. Frontend: a folder under `projects/crm-web/src/app/features/<feature>`, with a
   `<feature>-api.service.ts` for HTTP and a page component using `crm-state-container`.
4. Tests: a business rule test, an authorization test covering allowed and denied, a validation
   failure test, and a frontend component plus data-access test.
5. Run both verification scripts before opening the pull request.

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| Startup stops naming a setting | A required configuration value is missing | Set it with user-secrets (development) or the environment |
| `/health/ready` reports the database unhealthy | SQL Server unreachable, or the database does not exist | Check the instance is running, then re-run step 3 |
| Integration tests fail immediately with a Docker message | Container runtime not running | Start Docker; the tests never fall back to another database on purpose |
| Frontend loads but every call fails | API not running, or its origin is not on the allowlist | Start the API; check `Cors:AllowedOrigins` in `appsettings.Development.json` |

## Verification status of this document

Steps 1, 4, 5 (configuration side), 6 and the troubleshooting rows were executed and confirmed
during implementation. Steps 2 and 3, and the browser check in step 5, require a local SQL Server
instance, which was not installed on the machine used to build the foundation - they follow the
commands the tooling reports and should be confirmed on the first developer machine that has SQL
Server available (task T054).
