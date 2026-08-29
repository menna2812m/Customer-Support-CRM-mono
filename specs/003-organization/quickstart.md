# Quickstart: Organization Structure

**Feature**: 003-organization | **Date**: 2026-08-30

How to run, exercise, and verify this feature locally. Assumes the environment from
[getting-started.md](../../docs/getting-started.md) is already working - the `crm-sql` and `crm-idp`
containers running, user secrets set, and staff sign-in enabled.

---

## Run it

```powershell
dotnet run --project backend/src/Crm.Api      # http://localhost:5233
npm --prefix frontend start                   # http://localhost:4200
```

The migration applies at startup, so the three tables and the three foreign keys appear on first
run. No manual `dotnet ef database update` step.

Sign in as a user holding the `Administrator` role. `organization.manage` is granted to
Administrator by the seed migration and to nothing else - an Agent can neither see nor change the
structure, which is the first thing worth confirming.

---

## Build a structure by hand

Navigate to **Organization** in the shell. The intended first run:

1. **Create a branch.** Riyadh / الرياض, code `RUH`. Both names are required on the same form; try
   saving with one blank and confirm the form refuses rather than the server.
2. **Create a department.** Technical Support / الدعم الفني, code `TS`.
3. **Add two teams to it** from inside the department: Tier 1 and Tier 2. Note that the department
   is not a dropdown - it comes from the context you are in, which is what makes it impossible to
   create a team without one.
4. **Create a second department**, Billing / الفوترة, code `BIL`, and give it a team also named
   Tier 1. This is allowed, and it is the clarified rule: team names are unique only within their
   department.
5. **Switch the interface to Arabic.** The lists re-sort by the Arabic name rather than staying in
   English order, and the hierarchy indents from the right.

---

## Verify the rules that matter

Each of these should fail, and the message should say why:

| Try this | Expect |
|----------|--------|
| Create a second department with code `ts` | Refused - codes are compared ignoring case |
| Create a second department named Billing | Refused - department names are unique among departments |
| Create a second Tier 1 **inside** Technical Support | Refused - unique within the department |
| Delete Technical Support while it has teams | Refused, and the message names the dependent teams |
| Deactivate Billing, then move a team into it | Refused - a team cannot move into an inactive department |

And these should succeed:

| Try this | Expect |
|----------|--------|
| Delete a department created by mistake with no teams and nobody in it | Deleted, and its code becomes available again |
| Recreate a department using that same code | Accepted - this is why the unique indexes are filtered |
| Deactivate a branch | Stays visible in administration, marked inactive |
| Move Tier 2 to Billing | Moved, and the response reports how many members were reassigned |

---

## Verify the invariant

The team move is the one operation that writes to more than one table, and SC-003 is the check that
it did so correctly. Until feature 004 exists there is no interface for placing people, so seed a
placement directly to test it:

```sql
-- Put a user on a team, as feature 004 will
UPDATE [User]
SET TeamId = (SELECT Id FROM Team WHERE Code = 'T2'),
    DepartmentId = (SELECT DepartmentId FROM Team WHERE Code = 'T2')
WHERE Email = 'you@example.com';
```

Move that team to another department through the interface, then run the invariant check. It must
return zero rows - always, not merely usually:

```sql
-- INV-2: a user's department must match their team's department
SELECT u.Id, u.DepartmentId, t.DepartmentId AS TeamDepartmentId
FROM [User] u
JOIN Team t ON t.Id = u.TeamId
WHERE u.DepartmentId <> t.DepartmentId;
```

A non-empty result means the move did not resync members, which is the failure FR-015 exists to
prevent and the one this feature is most likely to get wrong.

---

## Verify the retirement

FR-018 stops the identity provider writing placement. To prove it rather than assume it, make the
provider assert placement and confirm nothing happens.

In Keycloak (`http://localhost:8080`, realm `crm`), add a hardcoded claim mapper on the `crm-api`
client called `department` with any value, then sign out and back in.

Expected: the stored placement is unchanged, and the claim is ignored rather than merely unused. The
distinction matters - before this feature, a configured claim would have overwritten placement on
every sign-in.

Remember to remove the mapper afterwards.

---

## Automated verification

```powershell
./scripts/verify-backend.ps1     # build + tests + format + publish
./scripts/verify-frontend.ps1    # lint + format + i18n + css + tests + build
```

Stop `ng serve` before running `verify-frontend.ps1` - its `npm ci` corrupts `node_modules` while
the dev server holds file locks.

The tests that specifically cover this feature:

| Test area | Covers |
|-----------|--------|
| `Crm.UnitTests/Organization` | Containment, the move-and-resync, dependent refusal, code immutability |
| `Crm.IntegrationTests/Organization` | Every endpoint, both permissions, uniqueness under concurrent creation and after soft deletion |
| `Crm.IntegrationTests/Contracts` | The published YAML matches the live OpenAPI document in both directions |
| `Crm.IntegrationTests/Auth` | **Updated** - sign-in no longer reads placement claims |
| `features/organization/*.spec.ts` | The three list screens, the forms, and the move dialog |

---

## What this feature does not do

Nobody can be placed in any of these units through the interface - that is feature 004. Until then
the structure is real, correct, and unused, which is the deliberate cost of splitting the
administration surface in two.
