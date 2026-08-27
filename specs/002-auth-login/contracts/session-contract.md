# Contract: Sessions, Credentials, and Claims

**Feature**: 002-auth-login | Binding on every feature that reads `ICurrentUser`

This is what a session *is* in the CRM, and what every later feature can rely on being true about
the caller.

## The two credentials

| | Access credential | Renewal credential |
|---|---|---|
| Form | Signed token, sent as `Authorization: Bearer` | Opaque value in a cookie |
| Held by the browser | In memory only, never in storage | HttpOnly cookie - script cannot read it |
| Lifetime | 15 minutes (configurable) | 8 hours of inactivity, 12 hours absolute (configurable) |
| Sent to | Every API call | Only the renewal and sign-out endpoints (path-scoped) |
| On theft | Expires quickly; revocation of the session ends it at next validation | Single-use, so a stolen one either fails or reveals itself as reuse and kills the session |

The split is the point: the credential a page script can reach is short-lived and low-value, and the
credential worth stealing is one a script can never reach.

## Claims in the access credential

| Claim | Meaning |
|---|---|
| `sub` | The CRM user identifier - **not** the provider's subject |
| `crm_session` | Session identifier, so an audit record and a request can be tied together |
| `crm_population` | `Staff` or `Portal`. Stamped at issuance, never read from an inbound token |
| `permission` | Repeated once per effective permission |
| `crm_department`, `crm_branch`, `crm_team` | Organizational placement, when known |
| `name`, `email` | For display only. Authorization never reads them |
| `exp`, `iat`, `iss`, `aud` | Standard, validated by the existing scheme |

`ICurrentUser` reads exactly these, and its shape does not change - feature 001 defined it, this
feature populates it.

## Rules every feature can rely on

1. **A caller with `IsAuthenticated` true has a live, unrevoked session.** Revocation takes effect
   at the next validation, and the access credential lives at most 15 minutes.
2. **Permissions in the session are at most one renewal cycle old.** A role change lands within 15
   minutes without the user signing out.
3. **Population cannot be forged.** It is stamped at issuance from the scheme that authenticated the
   sign-in, and re-stamped at every renewal.
4. **Absent organizational scope means "sees nothing extra".** A feature that scopes by department
   must not read a null scope as permission to see everything.
5. **`sub` is stable for the life of the person's CRM record**, across provider email and name
   changes, and across an identity collision refusal - because a refusal creates nothing.

## Renewal and rotation

```text
sign-in ──► session created ──► renewal #1 issued (cookie)
                                     │
        POST /auth/session ──────────┤ #1 spent, #2 issued, access credential returned
                                     │
        POST /auth/session ──────────┤ #2 spent, #3 issued
                                     │
        #2 presented again ──────────► session revoked, credential.reused recorded
```

Reuse is treated as compromise rather than as a race, because the legitimate client never presents
a spent credential: it holds exactly one, and replaces it atomically on each renewal. Concurrent
requests in the application coordinate through a single-flight renewal, so a burst of calls
produces one rotation, not several.

## What ends a session

| Trigger | Effect |
|---|---|
| Sign-out | This session revoked; cookie cleared |
| Sign out everywhere | Every session for the user revoked |
| Reuse of a spent renewal credential | Session revoked, event recorded as suspected compromise |
| Inactivity beyond the limit | Renewal refused |
| Absolute lifetime reached | Renewal refused regardless of activity |
| User deactivated | All their sessions revoked |

Provider sign-out is a *separate* choice, offered at sign-out. Ending the CRM session does not end
the provider session, which is why the choice exists: on a shared workstation, ending only the CRM
session lets the next person sign straight back in as the previous one.

## Error codes added

| Code | Status | When |
|---|---|---|
| `sign_in_failed` | 400 | The handshake could not be completed - state mismatch, invalid code, failed validation |
| `provider_unavailable` | 503 | The identity provider could not be reached or errored |
| `no_access` | 403 | Authenticated at the provider, but the CRM grants this person nothing |
| `identity_collision` | 403 | A new provider subject arrived with an email already belonging to another user |
| `session_expired` | 401 | Renewal refused - expired, revoked, or spent |
| `rate_limited` | 429 | Too many requests from this source; `Retry-After` says when |

All carry `correlationId`, all are language-neutral, and the client maps each to a translated
message - consistent with the error contract from feature 001.
