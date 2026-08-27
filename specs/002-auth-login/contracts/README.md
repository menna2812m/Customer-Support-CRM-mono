# Contracts: Authentication and Login

| File | Scope | Binding on |
|------|-------|------------|
| [auth-api.yaml](./auth-api.yaml) | The five endpoints this feature adds | This feature |
| [session-contract.md](./session-contract.md) | What a session is, what claims it carries, what ends it | **Every feature that reads `ICurrentUser`** |
| [frontend-contracts.md](./frontend-contracts.md) | `AuthService`, the guard, renewal behaviour, new screens | Every Angular feature |

The session contract is the one that outlives this feature. Everything built afterwards assumes its
five rules - live session, permissions at most one cycle old, unforgeable population, absent scope
means "sees nothing extra", and a stable subject.

## Relationship to feature 001

This feature adds no new convention. It fills seams:

| Seam from feature 001 | Filled by |
|---|---|
| `Staff` bearer scheme (registered, disabled) | Enabled, validating CRM-issued credentials |
| `Portal` bearer scheme | **Untouched** - still registered and disabled |
| `RequirePermission` / `RequirePopulation` | Now have real permissions and a real population to check |
| `ICurrentUser` | Populated from session claims instead of returning nulls |
| `IAuditRecorder` | Receives authentication events |
| `authGuard`, `authTokenInterceptor` | Implemented |
| Rate limiting (recorded as deferred, FR-056) | Delivered as a reusable named-policy capability |

## Drift check

The existing contract test in `Crm.IntegrationTests/Contracts` compares the live OpenAPI document's
`/api/` paths against the published YAML in both directions. Adding these endpoints without adding
them to `auth-api.yaml` - or removing one and leaving it published - fails the build, exactly as it
did for feature 001's diagnostics slice.
