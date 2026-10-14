-- ============================================================================
-- Phase 1.3: least-privilege login for the Hub OAuth store, PART 1 of 2: the server login.
-- Run in the [master] database of the SQL Server that hosts the Hub Master database (staging first).
-- Run ONCE. It creates a login only; it grants nothing. PART 2 creates the database user and permissions.
--
-- WHY: Hub keeps its OAuth state (McpOAuthApplication, McpOAuthAuthorization, McpOAuthToken) in the Master
-- database. This login can touch only those three tables, so a flaw in the OAuth store can never read or
-- change the rest of the Master database (tenants, users, connection strings).
--
-- BEFORE RUNNING: replace the password placeholder below with a long random password. Never commit it, paste
-- it in chat or put it in a ticket. After PART 2, store the full connection string as a secret and set it as the
-- Hub environment variable AgentAccessOAuthSQLConnection (same server and Master database as MasterSQLConnection,
-- with this login). Until that variable is set, Hub keeps using MasterSQLConnection for the OAuth store.
-- ============================================================================
SET NOCOUNT ON;

DECLARE @password nvarchar(128) = N'<<REPLACE-WITH-A-LONG-RANDOM-PASSWORD>>';

IF @password LIKE N'<<%'
    THROW 50000, N'Replace the password placeholder before running this script.', 1;

IF EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'McpOAuthStore')
    THROW 50001, N'Login McpOAuthStore already exists. Nothing was changed.', 1;

-- CHECK_POLICY ON enforces the server password policy. CHECK_EXPIRATION OFF because this is a service
-- account: an expiring password would silently take the OAuth store down.
DECLARE @sql nvarchar(max) =
    N'CREATE LOGIN [McpOAuthStore] WITH PASSWORD = N''' + REPLACE(@password, N'''', N'''''') +
    N''', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;';
EXEC (@sql);

-- Read-only check: expect one row, is_disabled = 0.
SELECT name, type_desc, is_disabled, create_date
FROM sys.server_principals
WHERE name = N'McpOAuthStore';
