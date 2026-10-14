-- ============================================================================
-- Phase 1.2/1.3: McpOAuth tables for the Hub OAuth server (Agent Access spec v4, D1)
-- Target: the Hub MASTER database (the one MasterSQLConnection points at), NOT a tenant DB.
-- SCHEMA ONLY. Four new tables, their keys and indexes. No data is read, changed or inserted.
-- Generated from the Hub model itself (AgentAccessOpenIddictDbContext on OpenIddict.EntityFrameworkCore
-- 7.7.2 / Microsoft.EntityFrameworkCore.SqlServer 10.0.12, Database.GenerateCreateScript), so it matches
-- what the Hub loads.
--
-- Conventions (same as the rest of the Master DB):
--   * Tables are singular PascalCase with the McpOAuth prefix: McpOAuthApplication, McpOAuthAuthorization,
--     McpOAuthScope, McpOAuthToken.
--   * Ids and foreign keys are uniqueidentifier (Guid).
--   * Every datetime column holds UTC and ends in Utc (CreationDateUtc, ExpirationDateUtc, RedemptionDateUtc),
--     stored as datetime2.
--
-- Run ONCE, by hand, on each Master database (staging first). It is not idempotent: it fails
-- if the tables already exist. Nothing in the Hub reads these tables until the OAuth endpoints
-- are enabled behind the agent-access gate, so applying this early is safe.
-- If you already ran the earlier version (tables named OpenIddictApplications, OpenIddictAuthorizations,
-- OpenIddictScopes, OpenIddictTokens), tell me before running this one: those empty tables should be
-- dropped first.
-- If any statement errors, run ROLLBACK and send me the message; do not re-run piecemeal.
--
-- The two unique indexes are filtered. That is fine on these brand-new tables (no legacy
-- procedure or QUOTED_IDENTIFIER OFF session touches them); every session that writes to
-- them must keep QUOTED_IDENTIFIER ON, which EF Core / SqlClient do by default.
-- ============================================================================
SET QUOTED_IDENTIFIER ON
SET ANSI_NULLS ON
SET XACT_ABORT ON
GO

BEGIN TRANSACTION
GO

CREATE TABLE [McpOAuthApplication] (
    [Id] uniqueidentifier NOT NULL,
    [ApplicationType] nvarchar(50) NULL,
    [ClientId] nvarchar(100) NULL,
    [ClientSecret] nvarchar(max) NULL,
    [ClientType] nvarchar(50) NULL,
    [ConcurrencyToken] nvarchar(50) NULL,
    [ConsentType] nvarchar(50) NULL,
    [DisplayName] nvarchar(max) NULL,
    [DisplayNames] nvarchar(max) NULL,
    [JsonWebKeySet] nvarchar(max) NULL,
    [Permissions] nvarchar(max) NULL,
    [PostLogoutRedirectUris] nvarchar(max) NULL,
    [Properties] nvarchar(max) NULL,
    [RedirectUris] nvarchar(max) NULL,
    [Requirements] nvarchar(max) NULL,
    [Settings] nvarchar(max) NULL,
    CONSTRAINT [PK_McpOAuthApplication] PRIMARY KEY ([Id])
);
GO

CREATE TABLE [McpOAuthScope] (
    [Id] uniqueidentifier NOT NULL,
    [ConcurrencyToken] nvarchar(50) NULL,
    [Description] nvarchar(max) NULL,
    [Descriptions] nvarchar(max) NULL,
    [DisplayName] nvarchar(max) NULL,
    [DisplayNames] nvarchar(max) NULL,
    [Name] nvarchar(200) NULL,
    [Properties] nvarchar(max) NULL,
    [Resources] nvarchar(max) NULL,
    CONSTRAINT [PK_McpOAuthScope] PRIMARY KEY ([Id])
);
GO

CREATE TABLE [McpOAuthAuthorization] (
    [Id] uniqueidentifier NOT NULL,
    [ApplicationId] uniqueidentifier NULL,
    [ConcurrencyToken] nvarchar(50) NULL,
    [CreationDateUtc] datetime2 NULL,
    [Properties] nvarchar(max) NULL,
    [Scopes] nvarchar(max) NULL,
    [Status] nvarchar(50) NULL,
    [Subject] nvarchar(400) NULL,
    [Type] nvarchar(50) NULL,
    CONSTRAINT [PK_McpOAuthAuthorization] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_McpOAuthAuthorization_McpOAuthApplication_ApplicationId] FOREIGN KEY ([ApplicationId]) REFERENCES [McpOAuthApplication] ([Id])
);
GO

CREATE TABLE [McpOAuthToken] (
    [Id] uniqueidentifier NOT NULL,
    [ApplicationId] uniqueidentifier NULL,
    [AuthorizationId] uniqueidentifier NULL,
    [ConcurrencyToken] nvarchar(50) NULL,
    [CreationDateUtc] datetime2 NULL,
    [ExpirationDateUtc] datetime2 NULL,
    [Payload] nvarchar(max) NULL,
    [Properties] nvarchar(max) NULL,
    [RedemptionDateUtc] datetime2 NULL,
    [ReferenceId] nvarchar(100) NULL,
    [Status] nvarchar(50) NULL,
    [Subject] nvarchar(400) NULL,
    [Type] nvarchar(150) NULL,
    CONSTRAINT [PK_McpOAuthToken] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_McpOAuthToken_McpOAuthApplication_ApplicationId] FOREIGN KEY ([ApplicationId]) REFERENCES [McpOAuthApplication] ([Id]),
    CONSTRAINT [FK_McpOAuthToken_McpOAuthAuthorization_AuthorizationId] FOREIGN KEY ([AuthorizationId]) REFERENCES [McpOAuthAuthorization] ([Id])
);
GO

CREATE UNIQUE INDEX [IX_McpOAuthApplication_ClientId] ON [McpOAuthApplication] ([ClientId]) WHERE [ClientId] IS NOT NULL;
GO

CREATE INDEX [IX_McpOAuthAuthorization_ApplicationId_Status_Subject_Type] ON [McpOAuthAuthorization] ([ApplicationId], [Status], [Subject], [Type]);
GO

CREATE UNIQUE INDEX [IX_McpOAuthScope_Name] ON [McpOAuthScope] ([Name]) WHERE [Name] IS NOT NULL;
GO

CREATE INDEX [IX_McpOAuthToken_ApplicationId_Status_Subject_Type] ON [McpOAuthToken] ([ApplicationId], [Status], [Subject], [Type]);
GO

CREATE INDEX [IX_McpOAuthToken_AuthorizationId] ON [McpOAuthToken] ([AuthorizationId]);
GO

CREATE UNIQUE INDEX [IX_McpOAuthToken_ReferenceId] ON [McpOAuthToken] ([ReferenceId]) WHERE [ReferenceId] IS NOT NULL;
GO

COMMIT TRANSACTION
GO

-- Read-only check after running: expect 4 rows, and the three Utc date columns plus CreationDateUtc.
SELECT name, create_date FROM sys.tables WHERE name LIKE N'McpOAuth%' ORDER BY name;
SELECT t.name AS TableName, c.name AS ColumnName, ty.name AS TypeName
FROM sys.columns c
JOIN sys.tables t ON t.object_id = c.object_id
JOIN sys.types ty ON ty.user_type_id = c.user_type_id
WHERE t.name LIKE N'McpOAuth%' AND (c.name = N'Id' OR c.name LIKE N'%Utc')
ORDER BY t.name, c.column_id;
