# Quickstart: Authentication and Login

**Feature**: 002-auth-login | **Date**: 2026-08-26

How a developer runs the CRM with authentication switched on, and the acceptance walkthrough for
User Stories 1 to 3. **Nothing below exists yet** - this is the target implementation is measured
against, and it folds into `docs/getting-started.md` when the work lands.

## Prerequisites

Everything from feature 001, plus **an OIDC provider to develop against**. Any standards-compliant
provider works; the choice for production is deliberately still open (spec FR-001).

The lightest local option is a Keycloak container:

```powershell
docker run -d --name crm-idp -p 8080:8080 `
  -e KEYCLOAK_ADMIN=admin -e KEYCLOAK_ADMIN_PASSWORD=admin `
  quay.io/keycloak/keycloak:latest start-dev
```

Then create a realm, a confidential client with redirect URI
`https://localhost:7283/api/v1/auth/callback`, and one or two users.

The integration suite does **not** need this - it runs an in-process fake provider, so tests stay
fast and offline.

## 1. Configure the provider

```powershell
cd backend/src/Crm.Api
dotnet user-secrets set "Authentication:Staff:ClientSecret" "<from your provider>"
cd ../../..
```

Non-secret settings go in `appsettings.Development.json`:

```json
{
  "Authentication": {
    "Staff": {
      "Enabled": true,
      "Authority": "http://localhost:8080/realms/crm",
      "ClientId": "crm-api",
      "ClaimNames": {
        "Subject": "sub",
        "Name": "name",
        "Email": "email",
        "Department": "department",
        "Branch": "branch",
        "Team": "team"
      }
    }
  },
  "Session": {
    "AccessCredentialMinutes": 15,
    "InactivityHours": 8,
    "AbsoluteHours": 12
  },
  "Identity": {
    "BootstrapAdministrator": "you@yourcompany.example",
    "DefaultRole": "Agent"
  }
}
```

`BootstrapAdministrator` is what stops a fresh database from locking everyone out. `DefaultRole` is
what lets your colleagues sign in and do something without a migration each. Setting `DefaultRole`
to null is legitimate - new users then arrive with no access, which is the safer choice in a
production environment with real data.

## 2. Apply the migration

```powershell
dotnet ef database update --project backend/src/Crm.Infrastructure --startup-project backend/src/Crm.Api
```

This adds the identity tables and seeds the `Administrator`, `Agent`, and `ReadOnly` roles.

## 3. Run and sign in

```powershell
dotnet run --project backend/src/Crm.Api
npm --prefix frontend start
```

Open `http://localhost:4200`. You should be redirected to sign-in, then to the provider, and back
to the application signed in. Confirm:

- your display name appears in the shell, and the navigation reflects your role;
- reloading the page keeps you signed in, without a second trip to the provider;
- `GET /api/v1/diagnostics/items` succeeds - the same call returned 401 before this feature.

## 4. Confirm the parts that are easy to get wrong

| Check | How | Expected |
|---|---|---|
| Access credential is not stored | Application tab in developer tools | Nothing in local or session storage. The renewal cookie is present and marked HttpOnly |
| Renewal is invisible | Wait past the access-credential lifetime while working | Requests keep succeeding; exactly one call to `/auth/session` in the network log, not one per request |
| Sign-out really ends it | Sign out, then replay a previous API call | Refused with `session_expired` |
| Shared workstation | Sign out and choose "also end access on this computer", then click sign in | The provider asks for credentials again rather than restoring you silently |
| No access | Set `DefaultRole` to null, sign in with a fresh user | The no-access screen, not a blank page or an error |
| Language | Switch to Arabic, then sign out and back in | Sign-in screens are in Arabic and mirrored; the provider page too, where it honours the hint |
| Rate limiting | Request `/api/v1/auth/sign-in` in a tight loop | 429 with `Retry-After`, in the shared error contract |

## 5. Run the suites

```powershell
./scripts/verify-backend.ps1
./scripts/verify-frontend.ps1
```

The backend suite exercises the whole handshake against the in-process fake provider - PKCE, code
exchange, token validation, provisioning, rotation, reuse detection, revocation, and the collision
refusal. No provider container is needed.

Before a release, run the same flow once against the real provider. The fake proves our code; only
the real one proves the vendor's quirks.

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Redirect loop between the app and sign-in | The renewal cookie is not coming back | Check the SPA and API are same-site; a cross-site pair needs the deployment note in `docs/production-configuration.md` |
| Sign-in returns `identity_collision` | A new provider subject carries an email already on another user | Intended (FR-005). Resolve the duplicate deliberately; do not relax the rule |
| Everything 403s after signing in | No default role and no assignment | Set `Identity:DefaultRole`, or assign a role by migration |
| `provider_unavailable` | Provider down or `Authority` wrong | Check the discovery document loads in a browser |
| Session ends sooner than expected | Inactivity limit, or reuse detection revoked it | Check `AuthenticationEvent` for `credential.reused` - if present, something replayed a spent credential |
