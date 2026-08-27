# Testing

## Suites

| Suite | Location | Covers |
|---|---|---|
| Backend unit | `backend/tests/Crm.UnitTests` | Business rules, validators, configuration validation, the permission catalog |
| Backend integration | `backend/tests/Crm.IntegrationTests` | The real HTTP pipeline against a real SQL Server: authorization, error contract, pagination, correlation, hardening |
| Architecture | `backend/tests/Crm.ArchitectureTests` | Layering, controllers free of persistence, vendor packages confined to Infrastructure |
| Frontend | `frontend/projects/*/src/**/*.spec.ts` | Components, data-access services, error normalization, language switching |

## Prerequisites

The integration suite needs a **container runtime (Docker)**. It starts its own SQL Server
container, creates a uniquely named database inside it, applies migrations, and disposes of
everything at the end. It never touches a developer database and needs no manual setup.

The first run downloads the SQL Server image (about 2.3 GB); later runs take roughly 20 seconds.

If Docker is not running, the suite fails immediately with a message naming Docker. It does **not**
fall back to another database: a substitute would prove nothing about SQL Server constraints,
indexes, or migrations, and the failure would be reported as a misleading test error.

### Concurrency

Each run generates its own database name (`Crm_<uuid>`) inside its own container, so two runs on
one machine - or two agents on one build server - cannot interfere with each other. There is no
shared state to coordinate and no cleanup step to forget.

## Running

```powershell
./scripts/verify-backend.ps1     # restore, build, format, all three backend suites, publish
./scripts/verify-frontend.ps1    # install, lint, format, i18n parity, css check, tests, build
```

Individual suites during development:

```powershell
dotnet test backend/tests/Crm.UnitTests/Crm.UnitTests.csproj
npm --prefix frontend run test:ci
npx ng test core --no-watch          # a single frontend project
```

## Adding tests

Every feature needs all four mandated kinds (Constitution XIII):

1. **Business rule** - unit test against the Application layer. See `DiagnosticItemQueryTests`.
2. **Authorization** - integration test covering allowed *and* denied, including the wrong caller
   population. See `AuthorizationTests`.
3. **Validation failure** - assert the field, the stable code, and the shared error contract. See
   `ContractTests`.
4. **Frontend** - a component test and a data-access test, including at least one error state. See
   `diagnostics.page.spec.ts` and `diagnostics-api.service.spec.ts`.

Use `provideCrmTesting()` in frontend tests. It wires the real credential and error-normalization
interceptors, a router, and the real translation files, so tests assert against what users actually
get rather than a stub. Put a session in place with `TestBed.inject(AuthSession).set(token, user)`
when a test needs an authenticated caller.

## Signing in inside a backend test

Authorization is this application's subject matter, so most integration tests need a caller. There
are two ways in, and the difference matters.

**`SignInHarness`** runs the real handshake against an in-process identity provider. Use it whenever
the test is about authentication itself - provisioning, refusals, cookies, rotation, expiry:

```csharp
await using var harness = SignInHarness.Create(
    database.ConnectionString,
    new Dictionary<string, string?> { ["Identity:DefaultRole"] = "Agent" });

var account = harness.Provider.AddAccount();
(await harness.SignInAsync(account)).Succeeded.ShouldBeTrue();

using var client = harness.CreateAuthenticatedClient(await harness.IssueAccessCredentialAsync());
```

The harness performs the browser's part explicitly - following redirects, carrying cookies - which
is what makes the cookie rules observable. It also exposes:

- `Clock` - an `AdjustableTimeProvider`. Session limits are measured in hours, so an expiry test
  moves the clock rather than lowering the limit to something nobody deploys.
- `RequestSessionAsync(renewalCookie, withApplicationHeader, origin)` - for replaying a spent
  credential or reproducing a cross-site call.
- `WithServicesAsync(...)`, `GetSessionsForUserAsync(...)`, `GetEventsForUserAsync(...)` - to arrange
  and inspect what no request can reach.
- The `X-Test-Source` header (see `TestClientAddressFilter`) - the in-memory server has no socket,
  so this is how a test becomes two different callers for a throttling assertion.

**`CrmWebApplicationFactory.SignInAsync(permissions)`** skips the handshake and seeds a user, a real
session, and a credential carrying exactly the permissions named. Use it when the test is about
something else and simply needs a caller who can reach the endpoint.

### How the fake provider stands in for a real one

`FakeOidcProvider` implements the parts of OpenID Connect the application actually uses: a discovery
document, a JWKS endpoint, an authorization endpoint that issues codes against a PKCE challenge, and
a token endpoint that signs identity tokens with a test key. `SignInHarness` points the application's
provider HTTP client at it, so the code under test is the real `OpenIdConnectClient` performing a
real exchange - discovery, code-for-token, signature and nonce validation - against a provider that
happens to live in the same process.

What it does not stand in for is the *provider-specific* part: claim names differ between products,
which is why they are configuration (`Authentication:Staff:ClaimNames`). That is the residual risk
of building against generic OIDC, and `specs/002-auth-login/quickstart.md` covers it by running the
same flow against a real provider container.

## Conventions

- **Test names read as sentences** (`A_page_beyond_the_end_is_an_empty_success_not_an_error`).
  `backend/tests/.editorconfig` disables the naming analyzer for test projects only.
- **Coverage thresholds are a team convention, not a product requirement.** The specification
  requires the suites to exist and to cover the four kinds above; a number is a proxy that is easy
  to satisfy without testing anything. Treat a drop as a prompt to look, not as a gate to game.
- **Two deliberate non-tests**, both recorded in the spec: the one-second language-switch target
  (SC-004) is confirmed by observation rather than a flaky wall-clock assertion, and integration
  test isolation is guaranteed by construction (unique database names) rather than proven by a
  concurrency test.

## Verifying the gates actually fail

A gate nobody has watched fail is not a gate. To confirm, temporarily:

- break formatting (add stray indentation) and run the backend script - `dotnet format` fails;
- add `const unused = 1;` to a `.ts` file - ESLint fails;
- add a key to `en.json` only - the parity check fails and names the key;
- write `margin-left: 1rem` in a component style - the css check fails and names the file;
- invert an assertion in any test - the suite fails.

Revert afterwards. Each of these was executed during the foundation work and produced a non-zero
exit naming the offending file.

## Timings

Measured on the development machine used to build the project (SQL Server image already cached).

After feature 001 (foundation):

| Gate | Duration |
|---|---|
| Backend: restore, build, format, 87 tests, publish | 68 seconds |
| Frontend: clean install, lint, format, i18n + css checks, 25 tests, production build | 162 seconds |

After feature 002 (authentication), measured 2026-08-27:

| Gate | Duration |
|---|---|
| Backend: restore, build, format, 145 tests, publish | 33 seconds |
| Frontend: clean install, lint, format, i18n + css checks, 66 tests, production build | 169 seconds |

Together about 3.4 minutes - inside the ten-minute budget in SC-009 of feature 001, with room to
spare. The backend number is lower than feature 001's despite two-thirds more tests because that
measurement included a cold NuGet restore; the integration suite itself takes about 19 seconds,
container startup included. The frontend grew by seven seconds for 41 more tests.

The first run on a new machine adds the SQL Server image download, which is excluded from the
budget by the criterion itself.
