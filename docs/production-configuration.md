# Production Configuration

Deployment and configuration for Windows Server + IIS, the hosting model chosen for this project.

## Hosting model

| Component | Artifact                                                                                    | Served by                                                     |
| --------- | ------------------------------------------------------------------------------------------- | ------------------------------------------------------------- |
| Backend   | `dotnet publish` output (includes `web.config` for the ASP.NET Core Module)                 | An IIS site or application, running in-process                |
| Frontend  | `ng build` output under `dist/crm-web/browser` (includes `web.config` with the SPA rewrite) | A separate IIS site, or a virtual directory alongside the API |

### The SPA and the API must share a registrable domain and a scheme

This is a deployment constraint, not a preference, and getting it wrong produces a failure that
looks like nothing at all: users sign in, land back on the application, and are immediately signed
out again.

A session survives a page reload because the browser holds a renewal cookie. That cookie is
`SameSite=Lax`, which means the browser sends it only on requests the browser considers same-site.
"Same-site" is decided by the **registrable domain** - `example.com` in `crm.example.com` - and by
the **scheme**. The host and the port are irrelevant: `https://crm.example.com` and
`https://api.example.com:8443` are same-site. The scheme is not. Browsers have enforced
_schemeful_ same-site since Chrome 89, so `http://crm.example.com` and `https://crm.example.com`
are cross-site even though the host is identical.

So these work:

| SPA                       | API                            | Why                                                         |
| ------------------------- | ------------------------------ | ----------------------------------------------------------- |
| `https://crm.example.com` | `https://crm.example.com/api`  | Same host                                                   |
| `https://crm.example.com` | `https://api.example.com`      | Same registrable domain                                     |
| `https://crm.example.com` | `https://api.example.com:8443` | Same registrable domain and scheme; the port does not count |

And these do not:

| SPA                       | API                                 | What happens                                                                                          |
| ------------------------- | ----------------------------------- | ----------------------------------------------------------------------------------------------------- |
| `https://crm.example.com` | `https://crm-api.azurewebsites.net` | The cookie is cross-site; the browser withholds it, and every renewal is answered `session_expired`   |
| `https://crm.example.com` | `https://crm.example.co.uk`         | Different registrable domain, same problem                                                            |
| `https://crm.example.com` | `http://crm.example.com`            | Same host, different scheme - schemeful same-site makes this cross-site, and the symptom is identical |

Loosening the cookie to `SameSite=None` would restore the behaviour and remove the CSRF protection
that the `SameSite=Lax` setting is there to provide, so it is not offered as an option. Put the two
behind one domain and one scheme instead - an IIS application under the SPA's site, or a sibling
host on the same domain, both served over HTTPS.

Two related settings follow from this:

- `Cors:AllowedOrigins` must list the SPA's exact origin. The renewal and sign-out endpoints check
  the request's `Origin` against this list as well, so an origin missing here is refused with
  `forbidden` rather than merely blocked at the response.
- `Authentication:Staff:ApplicationBaseUrl` must be the SPA's origin. It is where the callback sends
  the browser after the handshake, and it is read from configuration precisely because in
  development the SPA is a different origin (`http://localhost:4200`) from the API.

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

| Setting                                   | Secret  | Notes                                                                                            |
| ----------------------------------------- | ------- | ------------------------------------------------------------------------------------------------ |
| `Database:ConnectionString`               | **yes** | SQL Server connection for the application account                                                |
| `Database:CommandTimeoutSeconds`          | no      | Default 30                                                                                       |
| `Database:MaxRetryCount`                  | no      | Default 3                                                                                        |
| `Database:AutoMigrateOnStartup`           | no      | Must be `false`; startup refuses otherwise                                                       |
| `Cors:AllowedOrigins`                     | no      | Explicit list; a wildcard stops startup. Must contain the SPA origin                             |
| `Authentication:Staff:Enabled`            | no      | `true` in any environment where people sign in                                                   |
| `Authentication:Staff:Authority`          | no      | Provider base address; standard OIDC discovery is appended                                       |
| `Authentication:Staff:ClientId`           | no      | The confidential client registered for this deployment                                           |
| `Authentication:Staff:ClientSecret`       | **yes** | Through the secrets source. Startup refuses an enabled scheme without one                        |
| `Authentication:Staff:ApplicationBaseUrl` | no      | The SPA's origin - where the callback returns the browser                                        |
| `Authentication:Staff:ClaimNames:*`       | no      | `Subject`, `Name`, `Email`, `Department`, `Branch`, `Team`. Defaults are the standard OIDC names |
| `Token:Issuer` / `Token:Audience`         | no      | What the CRM stamps on its own credentials and validates                                         |
| `Token:SigningKey`                        | **yes** | HMAC key for CRM-issued credentials. At least 32 bytes                                           |
| `Token:KeyId`                             | no      | Names the key in the credential header so it can be rotated                                      |
| `Token:AccessCredentialMinutes`           | no      | Default 15. Also the upper bound on how stale a permission change can be                         |
| `Session:InactivityHours`                 | no      | Default 8 - a working day of idleness ends the session                                           |
| `Session:AbsoluteHours`                   | no      | Default 12 - the hard ceiling, however active the user                                           |
| `Session:CookieName`                      | no      | Default `crm_renewal`                                                                            |
| `Identity:BootstrapAdministrator`         | no      | See below. Provider subject or email                                                             |
| `Identity:DefaultRole`                    | no      | Role granted to a staff member with no assignment. `null` means new staff arrive with no access  |
| `RateLimiting:Enabled`                    | no      | Must be `true` outside Development; startup refuses otherwise                                    |
| `RateLimiting:Policies:auth-sign-in:*`    | no      | `PermitLimit` and `WindowSeconds` for sign-in and callback                                       |
| `RateLimiting:Policies:auth-session:*`    | no      | `PermitLimit` and `WindowSeconds` for renewal                                                    |
| `Authentication:Portal:Issuer`            | no      | Issuer of CRM-owned portal tokens                                                                |
| `Authentication:Portal:Audience`          | no      | Expected audience for portal tokens                                                              |
| `Authentication:Portal:SigningKey`        | **yes** | Key for CRM-issued portal tokens                                                                 |
| `Authentication:Portal:Enabled`           | no      | `true` once the portal ships                                                                     |
| `Observability:LogFilePath`               | no      | Path outside the site folder, on a drive with room                                               |
| `Observability:RetainedFileCount`         | no      | Default 30 daily files                                                                           |
| `Observability:CorrelationHeader`         | no      | Default `X-Correlation-Id`                                                                       |

## Setting up the identity provider

The CRM speaks generic OpenID Connect with the authorization-code flow and PKCE. Any conforming
provider works; what differs between products is claim naming, which is why it is configuration.

At the provider, register a **confidential client** for this deployment and record its id and
secret. Then:

1. **Redirect URI**: `https://<api-host>/api/v1/auth/callback`, exactly - the provider must match it
   character for character. Note that this is the **API** host, not the SPA host. The handshake runs
   server-side and the browser never receives a provider token.
2. **Post-logout redirect URI**: the SPA origin. Only used when a user asks to end access on this
   computer.
3. **Scopes**: `openid profile email`, plus whichever scope carries organizational placement if the
   directory publishes it.
4. **Claims**: confirm the names the provider actually emits and set `Authentication:Staff:ClaimNames`
   to match. A name that does not exist is not an error - the value simply arrives empty - so this is
   worth checking against a real token rather than assuming.

Placement claims (`Department`, `Branch`, `Team`) are optional. When the provider asserts none, the
CRM leaves whatever it already holds, so a directory without organizational data does not erase
placement that a later feature will populate.

`specs/002-auth-login/quickstart.md` runs this end to end against a provider container, including a
table of the things that are easy to get wrong.

## The bootstrap administrator

A fresh deployment has an empty user table, and every role assignment is made by an administrator.
Without a way in, that is a locked door with the key inside.

`Identity:BootstrapAdministrator` is that way in. Set it to the provider subject or the email address
of the first administrator. On their first sign-in - and only while they hold no role at all - they
are granted the `Administrator` role, and the grant is written to the authentication event trail as
a bootstrap grant so it is never invisible.

Three properties make this safe to leave configured:

- It grants nothing to a user who already has an assignment, so it cannot silently restore access
  that was deliberately removed.
- It is a configured value, not an assertion any caller can influence.
- There is no default. A default administrator with known credentials would be a back door, so a
  deployment that sets nothing here simply has no bootstrap path and must seed the assignment
  directly in the database.

Once real administrators exist, clear the setting.

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
