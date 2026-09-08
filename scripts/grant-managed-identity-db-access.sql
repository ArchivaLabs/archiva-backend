-- Grant the webapi Container App's managed identity access to ArchivaDb.
--
-- Why
-- ---
-- The Aspire AppHost model declares Azure SQL with managed-identity auth, so
-- `azd deploy` always regenerates the connection string as
--     Server=tcp:...;Encrypt=True;Authentication="Active Directory Default";Database=ArchivaDb
-- overwriting any SQL-auth string set by hand. Rather than re-patch that after
-- every deploy, this makes the generated string actually work.
--
-- The equivalent of this script is normally applied by
-- infra/webapi-roles-dbserver/webapi-roles-dbserver.module.bicep, but that
-- deployment script was never provisioned (no deploymentScripts resource exists
-- in the resource group), which is why the identity has no database login.
--
-- How to run
-- ----------
--   1. Azure Portal -> ArchivaDb -> Query editor.
--   2. Sign in with "Active Directory authentication" (NOT the SQL admin login).
--      This works because your account is now the server's Entra admin.
--   3. Run this whole script against the ArchivaDb database.
--
-- Idempotent: re-running it is a no-op once the user exists.

DECLARE @identityName SYSNAME = N'webapi_identity-hwli5d65dhudk';

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @identityName)
BEGIN
    DECLARE @createUser NVARCHAR(MAX) =
        N'CREATE USER [' + @identityName + N'] FROM EXTERNAL PROVIDER;';
    EXEC (@createUser);
    PRINT 'Created database user for the managed identity.';
END
ELSE
    PRINT 'Database user already exists.';

-- db_owner matches what the generated bicep grants. The app creates and reads
-- its own schema, so it needs DDL rights as well as read/write.
IF IS_ROLEMEMBER('db_owner', @identityName) = 0
BEGIN
    DECLARE @addRole NVARCHAR(MAX) =
        N'ALTER ROLE db_owner ADD MEMBER [' + @identityName + N'];';
    EXEC (@addRole);
    PRINT 'Granted db_owner.';
END
ELSE
    PRINT 'Already a member of db_owner.';

-- Confirm.
SELECT
    PrincipalName = dp.name,
    PrincipalType = dp.type_desc,
    IsDbOwner     = IS_ROLEMEMBER('db_owner', dp.name)
FROM sys.database_principals dp
WHERE dp.name = @identityName;
