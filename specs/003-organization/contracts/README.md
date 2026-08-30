# Contracts: Organization Structure

| File | Scope | Binding on |
|------|-------|------------|
| [organization-api.yaml](./organization-api.yaml) | The nineteen endpoints this feature adds | This feature, and feature 004 as a consumer |

Only one contract file, because this feature introduces no cross-cutting convention. It consumes
conventions rather than adding them - which is the first time that has been true of a feature here.

## What feature 004 depends on

Two things in this contract outlive the feature and should not change without considering the
consumer:

- **`activeOnly=true`** on the three list endpoints. FR-009 exists so that a placement chooser never
  has to filter inactive units out for itself. If this parameter went away, every consumer would
  reimplement the rule, and they would eventually disagree about it.
- **`TeamResponse` carrying its department.** A team is meaningless without one, so the response
  includes `departmentId` and both department names rather than forcing a second call. This is what
  lets a placement chooser show "Technical Support / Tier 1" in one request.

## Relationship to features 001 and 002

This feature adds no new convention. It is the first real consumer of two that already existed:

| Convention from feature 001 | First used here |
|---|---|
| `PageRequest` / `PagedResult` | Feature 002 recorded "no collection endpoint in this feature, so pagination does not arise". Here it arises - three list endpoints, used as built, not extended |
| The `diagnostics` reference slice | The list screens follow its shape rather than inventing a second one |

| Seam from feature 002 | Used here |
|---|---|
| `RequirePermission` | Two new permissions, discovered by the existing reflection scan |
| `IAuditRecorder` | Every mutation records an entry; the team move carries both departments and the affected count |
| Soft delete and the auditing interceptor | Applied to all three entities with no new code |
| The organizational placement columns | **Their foreign keys are added here**, closing the exception feature 002 recorded against Constitution VIII |

## What this feature removes

`organization-api.yaml` describes what is added. The retirement in FR-018 has no API surface, but it
does change a published behaviour of feature 002 and is recorded here so the change is not invisible:

| Removed | Consequence |
|---|---|
| Provider `department`, `branch`, and `team` claim reading | Placement is never written by sign-in. `ICurrentUser.Scope` is unchanged in shape and meaning; it now sources from the user record, which becomes the only writer |
| `ProviderClaimNames.Department/Branch/Team` configuration | The keys are removed from `appsettings.Development.json`. A provider still asserting them is ignored rather than merely inactive |

The CRM's **own** placement claims - `crm_department`, `crm_branch`, `crm_team` in the token it
issues - are untouched. Both are called "claims"; only one direction is retired.

## Drift check

The existing contract test in `Crm.IntegrationTests/Contracts` compares the live OpenAPI document's
`/api/` paths against the published YAML in both directions. Adding an endpoint without publishing
it here - or publishing one that is never implemented - fails the build. That test is the reason
this file is written before the controllers rather than after them.
