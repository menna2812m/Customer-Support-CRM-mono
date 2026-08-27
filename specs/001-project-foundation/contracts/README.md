# Contracts: Project Foundation

These documents define the interfaces this feature establishes. They are binding on every future
CRM feature - a feature that needs a different shape amends the contract here first, rather than
inventing a local variant.

| File | Scope | Binding on |
|------|-------|------------|
| [error-contract.md](./error-contract.md) | The single failure response shape, status codes, and stable error codes | Every endpoint, every version |
| [pagination-contract.md](./pagination-contract.md) | Paging, sorting, and filtering conventions for collections | Every list endpoint |
| [foundation-api.yaml](./foundation-api.yaml) | OpenAPI description of the endpoints this feature actually ships (health + removable reference slice) | This feature only |
| [frontend-contracts.md](./frontend-contracts.md) | Library public surfaces, error mapping, language/direction behavior, and the lint-enforced rules | Every Angular feature |

## Versioned surface versus operational endpoints

`/api/v1/**` is the application surface and is versioned. The health probes (`/health/live`,
`/health/ready`) and the development-only OpenAPI document are **operational** endpoints: they are
consumed by hosting, tooling, and monitoring rather than by application clients, so they sit
outside the version segment deliberately (spec FR-015). Nothing that carries business data may use
this exemption.

## What ships here, and what does not

Shipped: two anonymous health endpoints, and a reference vertical slice under
`/api/v1/diagnostics` that exercises validation, the error contract, pagination, permission
enforcement, and population admission. The slice is deliberately removable (FR-051) - its presence
in `foundation-api.yaml` is not a commitment to keep it.

Not shipped: any customer, ticket, SLA, reporting, or communication endpoint. Those arrive as
independent vertical features and must conform to the two contracts above.

## Verification

`foundation-api.yaml` is the intended surface, not generated output. The backend produces its own
OpenAPI document at runtime in development; an integration test compares the live document's paths,
status codes, and security requirements against this file, so drift is a test failure rather than a
documentation problem.
