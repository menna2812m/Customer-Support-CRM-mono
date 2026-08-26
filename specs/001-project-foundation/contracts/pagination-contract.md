# Contract: Pagination, Filtering, and Sorting

**Feature**: 001-project-foundation | Applies to: every list endpoint in the CRM

One contract, used everywhere, so that a client written against one collection works against all
of them. Spec: FR-020, Constitution III.

## Request

Query string parameters on any collection endpoint:

| Parameter | Type | Default | Rules |
|-----------|------|---------|-------|
| `page` | integer | 1 | 1-based. `page=0` or negative is a validation failure (`code: range`). |
| `pageSize` | integer | 25 | Maximum 100. Exceeding the maximum is a **validation failure**, not a silent clamp, so a client never believes it received more than it did. |
| `sort` | string | endpoint-defined | `field` ascending, `-field` descending. The field must be on the endpoint's documented allow-list, otherwise `code: not_sortable`. |
| filters | varies | none | Explicit named parameters per endpoint (e.g. `status=open`, `createdFrom=2026-01-01`). No generic expression language. |

Example: `GET /api/v1/tickets?page=2&pageSize=50&sort=-createdAt&status=open`

## Response

```json
{
  "items": [],
  "page": 2,
  "pageSize": 50,
  "totalCount": 128,
  "totalPages": 3
}
```

| Member | Type | Rules |
|--------|------|-------|
| `items` | array | Never null. Empty when the page is beyond the last page - this is a 200 with an empty array, not a 404. |
| `page` | integer | Echoes the effective page. |
| `pageSize` | integer | Echoes the effective page size. |
| `totalCount` | integer | Total matching rows before paging, after filtering and after authorization scoping. |
| `totalPages` | integer | Derived. `0` when `totalCount` is `0`. |

## Rules

1. **Deterministic ordering.** Every list endpoint applies a stable tiebreaker (typically the
   primary key) after the requested sort, so paging cannot skip or duplicate rows.
2. **Authorization first.** `totalCount` reflects only rows the caller is permitted to see. Paging
   is applied after organizational scoping, never before.
3. **Filtering is allow-listed.** Unknown filter parameters are rejected with
   `code: unknown_parameter` rather than ignored, so a typo does not silently return everything.
4. **No unbounded reads.** There is no `pageSize=all`. Bulk export, when a feature needs it, is a
   separate endpoint with its own authorization.
5. **Empty is not an error.** Frontends distinguish `items: []` (empty state) from a failure
   (error state) - both are mandated UI states.

## Verification

Integration tests on the reference slice cover: default paging, explicit paging, out-of-range page,
`pageSize` above maximum, descending sort, unsortable field, and unknown filter parameter.
