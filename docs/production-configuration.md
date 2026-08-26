# Production Configuration

Deployment and configuration for Windows Server + IIS, the hosting model chosen for this project.

## Hosting model

| Component | Artifact | Served by |
|---|---|---|
| Backend | `dotnet publish` output (includes `web.config` for the ASP.NET Core Module) | An IIS site or application, running in-process |
| Frontend | `ng build` output under `dist/crm-web/browser` (includes `web.config` with the SPA rewrite) | A separate IIS site, or a virtual directory alongside the API |

Both are produced and validated by `./scripts/verify-backend.ps1` and
`./scripts/verify-frontend.ps1`, so a broken artifact fails the gate rather than the deployment.

The frontend `web.config` rewrites any non-file path to `index.html` (client-side routing) while
leaving `/api` and `/health` alone, and disables caching for `index.html` so a deployment does not
strand users on the previous bundle.

## Configuration layers

Settings are resolved in this order, later winning:

1. `appsettings.json` - defaults shared by every environment.
2. `appsettings.Production.json` - non-secret production values. **No secret ever belongs here**;
   a unit test fails the build if a secret-bearing key gains a value.
3. Machine-level environment variables (`Database__ConnectionString`, and so on - double
   underscore separates sections).
4. The host-side protected store, through `ISecretsSource`.

Startup validates everything at once and refuses to run with a message naming every missing or
invalid setting (spec FR-007). It also refuses a wildcard CORS origin, and refuses
`AutoMigrateOnStartup` outside Development.

## Required settings

| Setting | Secret | Notes |
|---|---|---|
| `Database:ConnectionString` | **yes** | SQL Server connection for the application account |
| `Database:CommandTimeoutSeconds` | no | Default 30 |
| `Database:MaxRetryCount` | no | Default 3 |
| `Database:AutoMigrateOnStartup` | no | Must be `false`; startup refuses otherwise |
| `Cors:AllowedOrigins` | no | Explicit list; a wildcard stops startup |
| `Authentication:Staff:Authority` | no | Corporate identity provider metadata address |
| `Authentication:Staff:Audience` | no | Expected audience for staff tokens |
| `Authentication:Staff:Enabled` | no | `true` once the authentication feature ships |
| `Authentication:Portal:Issuer` | no | Issuer of CRM-owned portal tokens |
| `Authentication:Portal:Audience` | no | Expected audience for portal tokens |
| `Authentication:Portal:SigningKey` | **yes** | Key for CRM-issued portal tokens |
| `Authentication:Portal:Enabled` | no | `true` once the portal ships |
| `Observability:LogFilePath` | no | Path outside the site folder, on a drive with room |
| `Observability:RetainedFileCount` | no | Default 30 daily files |
| `Observability:CorrelationHeader` | no | Default `X-Correlation-Id` |

## Secrets

The shipped `ISecretsSource` reads a DPAPI-protected JSON file, encrypted for the local machine,
whose path comes from the `CRM_SECRETS_FILE` environment variable. The file lives **outside the
published folder** so a deployment never overwrites it and a folder copy never carries it away.

To replace the store - a vault, a managed identity, a hardware module - implement `ISecretsSource`
in `Crm.Infrastructure` and register it. No calling code changes; that is what the seam is for.

Development uses `dotnet user-secrets` instead, and never a committed file.

## Logging, rotation, and retention

Serilog writes compact JSON to `Observability:LogFilePath`, rolling daily and at 64 MB, retaining
`RetainedFileCount` files (default 30). Console output is Development-only: under IIS there is no
console to capture.

Every entry carries the correlation identifier, request path, and - when authenticated - the user
id and population. Sensitive values (passwords, tokens, secrets, authorization headers, connection
strings) are redacted at the pipeline boundary, so a single careless log statement cannot leak one.

Verified on 2026-08-26 by running the API and issuing requests: a dated file (crm-20260826.log)
appeared, entries were compact JSON with message templates preserved, a caller-supplied
correlation identifier appeared in the entries for that request, and a deliberately failing
connection attempt left no password in the file.

Still to verify after the first real deployment: that the account running the application pool can
write to the configured log directory, and that a second dated file appears the following day.

## Deployment checklist

1. Run both verification scripts; both must pass.
2. Copy the backend publish output to the site folder. Leave the secrets file where it is.
3. Copy the frontend build output to its site folder, including `web.config`.
4. Set `assets/config.json` for the environment (API base URL, default language). No rebuild is
   needed - it is read at runtime.
5. Apply migrations deliberately:
   `dotnet ef database update --project backend/src/Crm.Infrastructure --startup-project backend/src/Crm.Api`.
6. Confirm `/health/ready` reports `Healthy`, and that responses carry `X-Correlation-Id` and the
   baseline security headers.
