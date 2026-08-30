-- Pre-migration safety check for 003-organization.
--
-- Run this against a target database BEFORE applying the `Organization` migration. It is read-only:
-- it selects and it does not repair, delete, or alter anything. Repairing organization data
-- automatically is deliberately not offered - a placement that points nowhere is a question for a
-- person, not something a migration should silently resolve.
--
-- WHY ANY NON-NULL VALUE IS A PROBLEM, NOT ONLY A DANGLING ONE
--
-- The migration creates Branch, Department, and Team empty and, in the same migration, adds three
-- foreign keys from User to them. At the moment those constraints are created there are no rows to
-- reference, so *every* non-null placement value is an orphan by definition. That is why this check
-- does not join to the new tables: before the migration they do not exist, and after it they start
-- empty.
--
-- WHAT HAPPENS IF ROWS ARE FOUND AND THE MIGRATION IS RUN ANYWAY
--
-- EF Core emits ALTER TABLE [User] ADD CONSTRAINT ... FOREIGN KEY ... with no WITH NOCHECK, so SQL
-- Server validates the existing rows as the constraint is created. A violating row raises error 547
-- ("The ALTER TABLE statement conflicted with the FOREIGN KEY constraint"), the statement fails, and
-- because the migration carries no SuppressTransaction it runs inside a transaction that rolls back
-- in full: the three tables are not created, no constraint is added, and no row is written to
-- __EFMigrationsHistory. The database is left exactly as it was and the deployment stops with an
-- error naming the constraint. That is the intended failure - loud, complete, and reversible.
--
-- USAGE
--   sqlcmd -S <server> -d <database> -i scripts/check-organization-migration-safety.sql
-- or paste into any query window. Expect Verdict = 'SAFE'.

SET NOCOUNT ON;

-- 1. The verdict, and the counts behind it.
SELECT
    CASE
        WHEN COUNT(*) = 0 THEN 'SAFE - no user carries a placement, so the foreign keys will apply cleanly'
        ELSE 'BLOCKED - see the rows listed below; do not apply the migration until they are resolved'
    END                                                                       AS Verdict,
    COUNT(*)                                                                  AS OffendingUsers,
    SUM(CASE WHEN DepartmentId IS NOT NULL THEN 1 ELSE 0 END)                 AS WithDepartment,
    SUM(CASE WHEN BranchId     IS NOT NULL THEN 1 ELSE 0 END)                 AS WithBranch,
    SUM(CASE WHEN TeamId       IS NOT NULL THEN 1 ELSE 0 END)                 AS WithTeam
FROM [User]
WHERE DepartmentId IS NOT NULL
   OR BranchId     IS NOT NULL
   OR TeamId       IS NOT NULL;

-- 2. The offending rows themselves, so a person can decide what each one meant.
--    Empty on a healthy database.
SELECT
    Id,
    Email,
    DepartmentId,
    BranchId,
    TeamId,
    IsDeleted
FROM [User]
WHERE DepartmentId IS NOT NULL
   OR BranchId     IS NOT NULL
   OR TeamId       IS NOT NULL
ORDER BY Email;

-- 3. Context: whether the migration has already run here.
SELECT
    (SELECT COUNT(*) FROM sys.tables
      WHERE name IN ('Branch', 'Department', 'Team'))                          AS OrganizationTables,
    (SELECT COUNT(*) FROM [__EFMigrationsHistory]
      WHERE MigrationId LIKE '%_Organization')                                 AS OrganizationMigrationApplied;

-- IF ROWS ARE FOUND
--
-- Placement could only have been written by feature 002's sign-in, which read department, branch,
-- and team claims from the identity provider and expected them to carry CRM identifiers. Nothing
-- ever created units for those identifiers to point at, so any value found here references a unit
-- that has never existed. Feature 003 retires that claim reading precisely because of this.
--
-- The resolution is a decision, not a script. Either:
--   * clear the placement columns for the listed users, accepting that the values were never
--     meaningful and that an administrator will set placement properly afterwards; or
--   * create the organizational units those identifiers refer to first, if the identifiers came
--     from a directory whose structure is being reproduced deliberately.
--
-- Whichever is chosen, do it as an explicit, reviewed change with its own record - not as part of
-- applying a schema migration.
