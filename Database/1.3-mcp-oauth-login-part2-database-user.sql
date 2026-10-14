-- ============================================================================
-- Phase 1.3: least-privilege login for the Hub OAuth store, PART 2 of 2: database user and permissions.
-- Run in the Hub MASTER database (the one MasterSQLConnection points at, where the McpOAuth tables live),
-- AFTER part 1 and AFTER 1.2-openiddict-master-schema.sql. Run ONCE, staging first.
--
-- PERMISSIONS ONLY. No table, row or data is created, read or changed by this script.
--
-- What the login can do (through the role McpOAuthStoreRole):
--   SELECT, INSERT, UPDATE, DELETE on dbo.McpOAuthApplication, dbo.McpOAuthAuthorization, dbo.McpOAuthToken.
--   DELETE is there for the token pruning job (expired tokens) and OpenIddict's own cleanup; drop that one grant
--   if you want to hold it back until pruning exists.
-- What it cannot do: read or write any other table, run any procedure, change schema, create objects, or use
-- any other database. It is a member of no built-in role (no db_datareader, no db_datawriter).
--
-- Permissions are granted to a role and the user is added to the role, so a future second service account
-- gets the same access by joining the role.
-- ============================================================================
SET QUOTED_IDENTIFIER ON
SET ANSI_NULLS ON
SET XACT_ABORT ON
GO

-- One batch: the guards stop everything before anything is created.
IF OBJECT_ID(N'dbo.McpOAuthToken') IS NULL
    THROW 50010, N'dbo.McpOAuthToken not found. Run this in the Hub Master database after 1.2-openiddict-master-schema.sql.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'McpOAuthStore')
    THROW 50011, N'Login McpOAuthStore not found. Run part 1 first.', 1;

IF DATABASE_PRINCIPAL_ID(N'McpOAuthStore') IS NOT NULL OR DATABASE_PRINCIPAL_ID(N'McpOAuthStoreRole') IS NOT NULL
    THROW 50012, N'McpOAuthStore or McpOAuthStoreRole already exists in this database. Nothing was changed.', 1;

BEGIN TRANSACTION;

CREATE ROLE [McpOAuthStoreRole] AUTHORIZATION [dbo];

GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[McpOAuthApplication] TO [McpOAuthStoreRole];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[McpOAuthAuthorization] TO [McpOAuthStoreRole];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[McpOAuthToken] TO [McpOAuthStoreRole];

CREATE USER [McpOAuthStore] FOR LOGIN [McpOAuthStore] WITH DEFAULT_SCHEMA = [dbo];
ALTER ROLE [McpOAuthStoreRole] ADD MEMBER [McpOAuthStore];

COMMIT TRANSACTION;
GO

-- ---------------------------------------------------------------------------
-- Read-only checks.
-- 1. The role's permissions: expect 12 rows (4 permissions x 3 tables), all GRANT.
SELECT p.name AS RoleName, dp.permission_name, dp.state_desc, OBJECT_NAME(dp.major_id) AS ObjectName
FROM sys.database_permissions dp
JOIN sys.database_principals p ON p.principal_id = dp.grantee_principal_id
WHERE p.name = N'McpOAuthStoreRole' AND dp.class = 1
ORDER BY ObjectName, dp.permission_name;

-- 2. Membership: expect McpOAuthStoreRole -> McpOAuthStore only, and no built-in roles for the user.
SELECT r.name AS RoleName, m.name AS MemberName
FROM sys.database_role_members rm
JOIN sys.database_principals r ON r.principal_id = rm.role_principal_id
JOIN sys.database_principals m ON m.principal_id = rm.member_principal_id
WHERE m.name = N'McpOAuthStore' OR r.name = N'McpOAuthStoreRole';

-- 3. Behaviour as the login: allowed on the three tables, denied on everything else.
--    (Existence of dbo.Tenant is checked BEFORE switching user: the restricted user cannot see other objects.)
DECLARE @tenantTableExists bit = IIF(OBJECT_ID(N'dbo.Tenant') IS NOT NULL, 1, 0);
EXECUTE AS USER = N'McpOAuthStore';
    SELECT COUNT(*) AS McpOAuthToken_rows_visible FROM dbo.McpOAuthToken;             -- must succeed (count only)
    SELECT COUNT(*) AS McpOAuthApplication_rows_visible FROM dbo.McpOAuthApplication; -- must succeed
    BEGIN TRY
        IF @tenantTableExists = 1
        BEGIN
            EXEC (N'SELECT TOP 1 1 AS should_not_run FROM dbo.Tenant');
            SELECT N'PROBLEM: McpOAuthStore could read dbo.Tenant' AS tenant_access_check;
        END
        ELSE SELECT N'dbo.Tenant not present in this database; denial not tested on it' AS tenant_access_check;
    END TRY
    BEGIN CATCH
        SELECT N'OK: dbo.Tenant is denied (' + ERROR_MESSAGE() + N')' AS tenant_access_check;
    END CATCH
REVERT;
GO
