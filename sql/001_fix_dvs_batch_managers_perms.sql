-- =============================================================================
-- Fix permissions for EXCELL\DVS Batch Managers Windows group on EXCEL DB
-- =============================================================================
-- Run as a sysadmin in SSMS. Idempotent.
--
-- Problem: members of the group whose ONLY login is the group (i.e. not also
-- individual sysadmins) can't run the app. The group has:
--   * DENY SELECT/INSERT/UPDATE/DELETE on SCHEMA::dbo
--   * EXECUTE GRANT on usp_GetSOPDocumentsByBatch and usp_CreateSOPBatchAndAssignDocs
-- but is missing:
--   * SELECT on SY00500 (needed by the app to list pending DVS7 batches)
--   * EXECUTE on usp_SplitSOPBatchIntoChunks (the actual proc the app calls)
-- and DENY at the schema level beats any object-level GRANT, so we have to
-- remove the schema-level DENY on SELECT before SY00500 access works.
--
-- What this script does:
--   1. REVOKE the schema-level DENY on SELECT (keeps DENY on I/U/D — those are
--      fine because the proc reaches the underlying tables via ownership
--      chaining, so the user never needs direct write perms on dbo).
--   2. GRANT SELECT on SY00500 specifically (minimal — not the whole schema).
--   3. GRANT EXECUTE on dbo.usp_SplitSOPBatchIntoChunks.
-- =============================================================================

USE EXCEL;
GO

DECLARE @group sysname = N'EXCELL\DVS Batch Managers';

PRINT '--- Before ---';
SELECT
    Permission = perm.permission_name,
    State      = perm.state_desc,
    OnObject   =
        CASE perm.class
            WHEN 0 THEN '(database)'
            WHEN 1 THEN OBJECT_SCHEMA_NAME(perm.major_id) + '.' + OBJECT_NAME(perm.major_id)
            WHEN 3 THEN 'schema:' + SCHEMA_NAME(perm.major_id)
            ELSE perm.class_desc + ':' + CAST(perm.major_id AS VARCHAR(50))
        END
FROM sys.database_permissions perm
JOIN sys.database_principals pr ON pr.principal_id = perm.grantee_principal_id
WHERE pr.name = @group
ORDER BY perm.class, OnObject, perm.permission_name;

-- 1. Remove the schema-level DENY on SELECT.
--    Keep the DENY on INSERT/UPDATE/DELETE — the proc accesses tables via
--    ownership chain, so the user does not need direct write perms.
IF EXISTS (
    SELECT 1
    FROM sys.database_permissions p
    JOIN sys.database_principals pr ON pr.principal_id = p.grantee_principal_id
    JOIN sys.schemas sc ON sc.schema_id = p.major_id
    WHERE pr.name = N'EXCELL\DVS Batch Managers'
      AND p.class = 3
      AND sc.name = N'dbo'
      AND p.permission_name = 'SELECT'
      AND p.state_desc = 'DENY'
)
BEGIN
    PRINT 'Revoking DENY SELECT ON SCHEMA::dbo from group';
    REVOKE SELECT ON SCHEMA::dbo FROM [EXCELL\DVS Batch Managers];
END
ELSE
    PRINT 'No schema-level DENY SELECT on dbo to revoke';

-- 2. Grant SELECT on the one table the app reads directly.
PRINT 'Granting SELECT on dbo.SY00500';
GRANT SELECT ON dbo.SY00500 TO [EXCELL\DVS Batch Managers];

-- 3. Grant EXECUTE on the actual proc the app calls.
--    Ownership chaining handles all internal table access by the proc.
PRINT 'Granting EXECUTE on dbo.usp_SplitSOPBatchIntoChunks';
GRANT EXECUTE ON dbo.usp_SplitSOPBatchIntoChunks TO [EXCELL\DVS Batch Managers];

PRINT '';
PRINT '--- After ---';
SELECT
    Permission = perm.permission_name,
    State      = perm.state_desc,
    OnObject   =
        CASE perm.class
            WHEN 0 THEN '(database)'
            WHEN 1 THEN OBJECT_SCHEMA_NAME(perm.major_id) + '.' + OBJECT_NAME(perm.major_id)
            WHEN 3 THEN 'schema:' + SCHEMA_NAME(perm.major_id)
            ELSE perm.class_desc + ':' + CAST(perm.major_id AS VARCHAR(50))
        END
FROM sys.database_permissions perm
JOIN sys.database_principals pr ON pr.principal_id = perm.grantee_principal_id
WHERE pr.name = @group
ORDER BY perm.class, OnObject, perm.permission_name;
GO
