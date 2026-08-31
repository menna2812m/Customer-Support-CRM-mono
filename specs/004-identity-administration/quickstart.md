# Quickstart: Identity Administration

**Feature**: 004-identity-administration | **Date**: 2026-08-30

How to run, exercise, and verify this feature locally. Assumes the environment from
[getting-started.md](../../docs/getting-started.md) is working - the `crm-sql` and `crm-idp`
containers running, user secrets set, staff sign-in enabled - and that feature 003's structure
exists, because this feature places people into it and creates no units of its own.

Build a structure first if you have none: follow
[feature 003's quickstart](../003-organization/quickstart.md) as far as a branch, two departments,
and a couple of teams. Everything below assumes Riyadh, Technical Support, Billing, Tier 1 and
Tier 2.

---

## Before the first run: check the migration is safe

This migration widens a column and replaces two unique indexes with filtered ones. Every change is
strictly *less* restrictive than what it replaces, so unlike feature 003 there is no pre-migration
safety script and no data migration - nothing that exists can violate a rule that permits more than
the rule it replaces.

One thing is worth confirming anyway, because it is cheap and the failure is otherwise puzzling:

```sql
-- Two live users sharing an email would block the new filtered index.
-- Impossible today (the current index is unfiltered and unique), so expect zero rows.
SELECT Email, COUNT(*) FROM [User] WHERE IsDeleted = 0 GROUP BY Email HAVING COUNT(*) > 1;
```

## Run it

```powershell
dotnet run --project backend/src/Crm.Api      # http://localhost:5233
npm --prefix frontend start                   # http://localhost:4200
```

The migration applies at startup. Sign in as a user holding the `Administrator` role -
`identity.manage` is granted to Administrator by the seed migration and to nothing else, so an Agent
can see no part of this feature.

---

## Place somebody who already exists

Navigate to **People**. Your own account is there, because you have signed in.

1. **Grant a role.** Open a person and tick a second role. Their effective permissions below grow to
   the union of both. Untick it and they shrink again.
2. **Place them in a branch.** Riyadh. Independent of everything else.
3. **Place them in a team.** Choose Tier 2. Watch the department fill in as Technical Support and
   become read-only - you never chose it, and you cannot now change it while the team stands.
4. **Clear the team.** The department becomes selectable again. Choose Billing directly, with no
   team. This is a person in a department but on no team, which is legitimate.
5. **Deactivate a department in feature 003, then come back.** It is no longer offered in the
   chooser, while anyone already placed there stays placed.

## Prepare somebody who does not exist yet

1. **Add a person** by an email address that has never signed in. Give them Agent and put them in
   Tier 1.
2. Note they appear as **Invited**. Filter the list by "never signed in" and they are the only one.
3. **Sign in as that person** in a private window, with a Keycloak account whose email matches and
   whose email is verified. Without the verified flag the sign-in is refused rather than claiming -
   the address belongs to the prepared record, so there is nowhere to put a second account.
4. Return to the list as an administrator. There is **one** record, now **Active**, still holding
   Agent and still in Tier 1. The preparation survived, and no duplicate was created.

---

## Verify the rules that matter

Each of these should fail, and say why:

| Try this | Expect |
|----------|--------|
| Remove your own administrator role | Refused - another administrator must do it |
| Deactivate or delete your own account | Refused, for the same reason |
| Remove administrator from the only other administrator, leaving none | Refused - the system keeps at least one |
| Pre-provision an address that already belongs to someone | Refused - the address is in use |
| Place a person in a deactivated department | Refused - only active units are offered, and the server refuses it too |

And these should succeed:

| Try this | Expect |
|----------|--------|
| Delete a prepared person who never signed in | Deleted, and their address is free again |
| Pre-provision that same address afresh | Accepted - this is why the email index is filtered |
| Deactivate a person, then reactivate them | Their roles and placement are untouched |

## Verify that access really ends

Deactivation and deletion promise immediacy, and the interesting question is whether they deliver it
before the access credential expires. They do, because token validation resolves the session on
every request - but prove it rather than believe it:

1. Sign in as a second person in a private window and leave them on a page that loads data.
2. As an administrator, **deactivate** them.
3. Have that window make its next request - refresh the list.

It must be refused **now**, not within fifteen minutes (`AccessCredentialMinutes: 15`). A refusal
that arrives on the next request is the feature working; one that arrives only after the credential
expires means the session check has been bypassed somewhere.

## Verify the invariant

INV-2 is shared with feature 003 - this feature derives a person's department from their team, and
feature 003 resyncs members when a team moves. Neither half is sufficient, so scan for the
disagreement after exercising both:

```sql
-- INV-2: a user's department must match their team's department
SELECT u.Id, u.DepartmentId, t.DepartmentId AS TeamDepartmentId
FROM [User] u
JOIN Team t ON t.Id = u.TeamId
WHERE u.DepartmentId <> t.DepartmentId;
```

Place several people on Tier 2, move Tier 2 to Billing through feature 003's move dialog, then run
the scan. Zero rows, always (SC-002).

## Verify the claim rules fail closed

The claim is the one decision in this feature whose failure is silent - a wrongly claimed record
looks exactly like a correctly claimed one. Each of these must **not** claim:

| Set up | Sign in as | Expect |
|--------|-----------|--------|
| A prepared address, and a Keycloak user whose email is **not** verified | that user | No claim, and sign-in is **refused** with `identity_email_not_verified`. The preparation is untouched, no second account appears, and the refusal is audited |
| A prepared address matching a person who has **already signed in** | a new Keycloak user with that email | Sign-in **refused**. The collision is audited and nothing is rebound |

In Keycloak (`http://localhost:8080`, realm `crm`), the email-verified flag is on the user's Details
tab. Turn it off to exercise the first row, and remember to turn it back on.

---

## Automated verification

```powershell
./scripts/verify-backend.ps1     # build + tests + format + publish
./scripts/verify-frontend.ps1    # lint + format + i18n + css + tests + build
```

Stop `ng serve` before the frontend script, and check nothing else holds a lock on `node_modules` -
its `npm ci` deletes the tree and a running dev server or a stray `esbuild.exe` leaves it broken
part-way.

| Test area | Covers |
|-----------|--------|
| `Crm.UnitTests/Identity` | The claim matrix, both lockout guards, placement derivation |
| `Crm.IntegrationTests/Identity` | Every endpoint, both permissions, delete atomicity, sessions ending |
| `Crm.IntegrationTests/Auth` | **Updated** - sign-in becomes match-then-create |
| `Crm.IntegrationTests/Contracts` | The published YAML matches the live document, once markers are removed |
| `features/identity/*.spec.ts` | The list, the detail screen, both forms |

---

## What this feature does not do

Roles are those the deployment seeds; nothing here creates one or edits the permissions behind it.
There is no bulk import, no restore screen, and no delegated administration - an administrator sees
everybody, because scoping the people list by the placement this feature exists to assign would be
circular.
