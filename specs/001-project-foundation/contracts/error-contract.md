# Contract: API Error Responses

**Feature**: 001-project-foundation | Applies to: every endpoint, every version, forever

Every failure response from the CRM API uses RFC 9457 problem details with three CRM extensions.
There is exactly one producer (the central exception handler plus the validation filter); no
controller writes an error body of its own. Spec: FR-016, FR-017, FR-018, LR-003, SC-003.

## Media type

`application/problem+json`

## Shape

```json
{
  "type": "https://crm.example/errors/validation-failed",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "instance": "/api/v1/diagnostics/echo",
  "code": "validation_failed",
  "correlationId": "0af7651916cd43dd8448eb211c80319c",
  "errors": [
    { "field": "message", "code": "required", "message": "Message is required." },
    { "field": "repeatCount", "code": "range", "message": "Repeat count must be between 1 and 10." }
  ]
}
```

| Member | Required | Meaning |
|--------|----------|---------|
| `type` | yes | Stable URI identifying the error class. Documentation anchor, not a live endpoint. |
| `title` | yes | Short, developer-facing English summary. **Never displayed to end users** - see Localization. |
| `status` | yes | Matches the HTTP status code. |
| `instance` | yes | Request path. Never includes query strings that may carry sensitive values. |
| `code` | yes | Stable machine-readable identifier. Snake_case. The frontend switches on this. |
| `correlationId` | yes | The request's correlation identifier, matching the `X-Correlation-Id` response header and the server logs. |
| `errors` | only for validation failures | One entry per offending field. |

`errors[]` entries: `field` (client-facing member path in camelCase, dotted for nesting, indexed
for collections, e.g. `contacts[1].email`), `code` (stable rule identifier such as `required`,
`range`, `max_length`, `not_unique`), `message` (developer-facing English text).

## Status codes and codes

| Situation | Status | `code` |
|-----------|--------|--------|
| Request payload or query failed validation | 400 | `validation_failed` |
| Malformed JSON, depth or size limit exceeded | 400 | `malformed_request` |
| Unknown or unsupported API version | 400 | `unsupported_api_version` |
| No credentials, or credentials invalid/expired | 401 | `unauthenticated` |
| Authenticated but lacking the required permission | 403 | `forbidden` |
| Authenticated but from a population the endpoint does not admit | 403 | `forbidden` |
| Origin not on the CORS allowlist | 403 | `origin_not_allowed` |
| Resource does not exist, or the caller may not know it exists | 404 | `not_found` |
| Concurrency or state conflict | 409 | `conflict` |
| Anything unhandled | 500 | `unexpected_error` |

## Rules

1. **No leakage.** For 500 responses, `title` is a fixed generic sentence. Stack traces, exception
   types, SQL, connection strings, file paths, and framework internals never appear in any member.
   The correlation id is the only handle given to the caller (FR-018).
2. **No existence disclosure.** A caller lacking permission on an existing resource and a caller
   requesting a non-existent resource must not be able to tell the two apart from the response
   body (AR-003, FR-026).
3. **Localization.** The body is language-neutral. Clients map `code` (and `errors[].code`) to a
   translated message; `title` and `message` exist for developers and logs only (LR-003).
4. **Correlation always present.** Every error response carries `correlationId`, including
   responses produced before authentication or routing has run.
5. **Success responses never use this shape.** A successful call returns its resource
   representation directly - there is no envelope.

## Verification

Integration tests assert one case per row of the status table, checking media type, `code`,
presence of `correlationId`, and absence of forbidden substrings (`Exception`, `at Crm.`, `Server=`,
`Password=`) in the serialized body.
