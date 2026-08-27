# Phase 0 Research: Authentication and Login

**Feature**: 002-auth-login | **Date**: 2026-08-26

Eight clarifications fixed the provider strategy, the portal deferral, the permission source, the
credential split, default roles, placement resolution, sign-out semantics, and identity collisions.
What remained were mechanism decisions. Each is recorded as Decision / Rationale / Alternatives.

---

## 1. Who issues the token the API validates

**Decision**: The CRM issues its own access credential. The provider authenticates the person once,
during sign-in; the API then mints a short-lived JWT signed with its own key, carrying the CRM user
identifier, population, permissions, and organizational scope. The existing `Staff` scheme
validates it - `StaffAuthOptions` already has `Issuer` and `SigningKey`, so no scheme code changes.

**Rationale**: Four clarified requirements are unreachable with a provider-issued token. It cannot
be revoked by us (FR-013 sign-out must stop access immediately), cannot be rotated single-use
(FR-012), cannot carry CRM permissions (FR-023 - the provider does not know them), and putting a
corporate access token in a browser widens the blast radius of any XSS well beyond this
application.

**Alternatives**: Forward the provider's access token and validate it per request - this was
feature 001's stated assumption and is less code. Rejected because the failure mode is silent:
sign-out would appear to work in the interface while the token continued to validate until the
provider expired it, which is exactly the shared-workstation case User Story 2 exists to prevent.
A hybrid (validate the provider token but keep a revocation list) was rejected as the worst of
both: server state anyway, plus a foreign token in the browser.

**Recorded as a deviation** from feature 001 research decision 6, in plan.md and in the
Complexity Tracking table.

---

## 2. OIDC flow and where the exchange happens

**Decision**: Authorization Code flow with PKCE, executed **server-side** by the API. The SPA never
speaks to the provider directly and needs no OIDC library. Sequence:

1. SPA sends the browser to `GET /api/v1/auth/sign-in?returnUrl=…`.
2. API stores flow state (PKCE verifier, nonce, return path) in a short-lived cookie and redirects
   to the provider.
3. Provider redirects back to `GET /api/v1/auth/callback`.
4. API exchanges the code, validates the ID token, resolves or provisions the user, creates a
   session, sets the renewal cookie, and redirects to the SPA - **with no token in the URL**.
5. SPA calls `POST /api/v1/auth/session` (renewal cookie only) and receives an access credential in
   the response body, which it keeps in memory.

**Rationale**: The redirect in step 4 carries nothing secret, which is what FR-014 requires
(no credential in URLs or history). The SPA getting its access credential through a normal API call
means one code path for "get a credential" - the same one renewal uses - rather than two.

**Alternatives**: Public-client PKCE in the browser (rejected - the SPA would hold provider tokens
and need a library, and we would still need our own token for revocation); implicit flow (rejected
- deprecated and puts tokens in URLs); posting the token to the SPA via fragment (rejected - it is
still the browser history problem wearing a hat).

---

## 3. Credential delivery and CSRF

**Decision**: Access credential in the response body, held in a JavaScript variable, sent as
`Authorization: Bearer`. Renewal credential in a cookie marked HttpOnly, Secure, `SameSite=Lax`,
and path-scoped to the renewal and sign-out endpoints. The renewal endpoint additionally requires a
custom request header that a cross-site form post cannot set, and checks `Origin` against the
existing CORS allowlist.

**Rationale**: This is the clarified split. Cookies ignore port when deciding same-site, so
`localhost:4200 → localhost:5233` in development and `crm.example → crm.example/api` in production
are both same-site, and `SameSite=Lax` holds. The custom header plus origin check covers the
residual CSRF surface without introducing a token-issuing dance of its own.

**Deployment consequence, to be documented**: the SPA and the API must be served from the same
registrable domain in production. If operations ever splits them across unrelated domains, the
cookie becomes cross-site, `SameSite=None` would be required, and this decision must be revisited.

**Alternatives**: Store the renewal credential in local storage (rejected - readable by any script,
which is the exact failure the clarification chose against); a full BFF proxying every call
(rejected - larger change to the frontend for protection the split already provides).

---

## 4. Token signing

**Decision**: Symmetric HMAC-SHA256 with a key supplied through the `ISecretsSource` seam from
feature 001. Key identifier included in the token header so a rotation can be introduced without
invalidating live sessions.

**Rationale**: One issuer and one validator, in the same process. A symmetric key is simpler to
operate and has no public-key distribution problem. The key identifier costs nothing now and is
what makes rotation possible later without a flag day.

**Alternatives**: Asymmetric RSA/ECDSA with a JWKS endpoint (rejected for now - it exists to let
*other* services validate without the signing key, and there are no other services; adopting it
later is a contained change because of the key identifier).

---

## 5. Session and renewal storage

**Decision**: Both in SQL Server. A session row per sign-in; a renewal credential row per issued
credential, storing only a hash of the credential, its expiry, and whether it has been used.
Rotation on every renewal: the old row is marked used, a new one is issued. Presenting a used
credential revokes the whole session and records a suspected-compromise event.

**Rationale**: Immediate revocation (FR-013) needs server state, and reuse detection (FR-012) needs
to remember what was already spent. Hashing means a database leak does not hand over live sessions.

**Alternatives**: Distributed cache (rejected - a second piece of infrastructure for data that is
small, must survive restarts, and already has a database); no rotation (rejected by FR-012).

---

## 6. Identity resolution and provisioning

**Decision**: Look up by provider subject. If found, refresh display name, email, and placement, and
sign in. If not found: refuse when the email belongs to another user (FR-005), otherwise create the
user, grant the default role if one is configured, grant administrative permissions if the subject
or email matches the configured bootstrap administrator, and audit each of these. All of it in one
transaction, so a failure mid-way leaves no half-created user.

**Rationale**: Subject is the only stable key - names and emails change. The collision refusal is
the clarified behaviour, and doing the whole thing transactionally is what keeps Constitution XV's
"integration failures must not corrupt internal state" true when the provider misbehaves.

**Alternatives**: Key on email (rejected - reissued addresses are exactly the collision case);
provision lazily on first authorized call (rejected - a user who exists only after their second
request is hard to reason about and impossible to audit cleanly).

---

## 7. Permissions in the session

**Decision**: Effective permissions are computed at sign-in and at every renewal, from role
assignments, and embedded as claims in the access credential. With a 15-minute access credential, a
role change lands within 15 minutes without a sign-out (FR-023).

**Rationale**: Claims in the token keep authorization decisions free of a database round trip per
request, which matters because every endpoint checks a permission. Recomputing at renewal is what
bounds staleness to one cycle.

**Alternatives**: Look permissions up per request (rejected - a query on every call to gain
freshness the requirement does not ask for); cache with invalidation (rejected - a cache-coherence
problem in exchange for at most 15 minutes).

---

## 8. Rate limiting

**Decision**: The framework's built-in rate limiter, configured as **named policies** applied by
attribute, so any later feature can annotate an endpoint. Partition by client address, with the
policy names and limits in configuration. Exceeding a limit returns the shared error contract with
a distinct code and a `Retry-After` header.

**Rationale**: FR-026 requires a reusable capability rather than authentication-local logic. This is
the debt feature 001 recorded and deferred here by name.

**Alternatives**: A hand-rolled counter (rejected - the framework primitive handles partitioning,
queueing, and concurrency correctly); a reverse-proxy limit in IIS (rejected as the only measure -
it is invisible to tests and lost on a server rebuild, the same reasoning that put the security
headers in the application in feature 001).

---

## 9. Testing the handshake

**Decision**: An in-process fake OIDC provider in the integration test project, serving discovery,
JWKS, authorization, and token endpoints, and signing ID tokens with a test key. The real handshake
runs end to end against it - including PKCE, nonce, and signature validation. A real provider is
exercised separately, before a release, not in the default suite.

**Rationale**: The suite must prove the flow, not a mock of it, while staying inside the ten-minute
budget feature 001 established. A fake that implements the protocol tests our code honestly; what
it cannot test is a specific vendor's quirks, which is what the pre-release check is for.

**Alternatives**: Keycloak in Testcontainers on every run (rejected - roughly triples suite startup
for the same assertions; kept as an optional check); mocking the provider client interface
(rejected - would not exercise token validation, which is the part most worth testing).

---

## 10. Frontend session handling

**Decision**: Session state in `@crm/core` as signals: the access credential in memory only, plus
the current user. The existing `authTokenInterceptor` gains the attach-and-renew behaviour with
**single-flight** renewal - concurrent requests meeting an expired credential await one renewal
rather than starting several (FR-032). On renewal failure the session is cleared once, and the user
is routed to sign-in with their intended destination preserved.

**Rationale**: Feature 001 built the interceptor chain precisely so this could land in one file.
Single-flight matters as soon as a screen issues two calls at once, which the diagnostics page
already does.

**Alternatives**: Renew on a timer before expiry (rejected as the only mechanism - clock skew and
sleeping laptops make it unreliable; it can be added later as an optimisation on top of
renew-on-401); retry each failed request independently (rejected - a stampede of renewals, and the
rotation rule would treat the second one as reuse and revoke the session).

---

## 11. Provider sign-out

**Decision**: Sign-out always revokes the CRM session and clears the cookie, and returns the
provider's end-session address when one is discovered and the user asked for it. The frontend then
navigates there. Phrased in the interface as ending access on this computer.

**Rationale**: The clarified answer. Returning the address rather than redirecting server-side keeps
the API's response shape ordinary and lets the SPA finish its own cleanup first.

**Alternatives**: Always redirect through the provider (rejected - hostile on a personal machine
with other corporate applications open); never offer it (rejected - leaves the shared-workstation
case unsafe, which is the case the story exists for).

---

## Open items carried into implementation

| Item | Handling |
|------|----------|
| Which OIDC provider production will use | Deliberately open (spec FR-001). Configuration-driven; the fake provider and any standards-compliant provider both work. First real provider integration should be run through the pre-release check in decision 9 |
| Signing key supply | Through the `ISecretsSource` seam; operations still choose the store, as recorded in feature 001 |
| Same-domain deployment for the SPA and API | A constraint this design introduces (decision 3). Must be added to `docs/production-configuration.md` during implementation, not discovered at deployment |
| Authentication-event retention | Left to the audit-log feature, as the specification's clarification session recorded |
