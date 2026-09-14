-- Rename the CKYCR search/download tables and their indexes to the bulk_* naming:
--   search_request/batch/response/response_file -> bulk_search_*
--   download_response_file/line/artifact       -> bulk_download_response_*
-- Run once against databases created from the pre-rename schema. Every step is
-- guarded by an existence check, so the script is safe to re-run and safe on a
-- database that already uses the new names.
--
-- The application expects the new names at startup (SqlServerDatabase.cs), so run
-- this before deploying the matching build.

SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- ---------------------------------------------------------------------------
-- Tables
-- ---------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.search_request', N'U') IS NOT NULL
    EXEC sp_rename N'dbo.search_request', N'bulk_search_request';

IF OBJECT_ID(N'dbo.search_batch', N'U') IS NOT NULL
    EXEC sp_rename N'dbo.search_batch', N'bulk_search_batch';

IF OBJECT_ID(N'dbo.search_response', N'U') IS NOT NULL
    EXEC sp_rename N'dbo.search_response', N'bulk_search_response';

IF OBJECT_ID(N'dbo.search_response_file', N'U') IS NOT NULL
    EXEC sp_rename N'dbo.search_response_file', N'bulk_search_response_file';

IF OBJECT_ID(N'dbo.download_response_file', N'U') IS NOT NULL
    EXEC sp_rename N'dbo.download_response_file', N'bulk_download_response_file';

IF OBJECT_ID(N'dbo.download_response_line', N'U') IS NOT NULL
    EXEC sp_rename N'dbo.download_response_line', N'bulk_download_response_line';

IF OBJECT_ID(N'dbo.download_response_artifact', N'U') IS NOT NULL
    EXEC sp_rename N'dbo.download_response_artifact', N'bulk_download_response_artifact';

-- ---------------------------------------------------------------------------
-- Indexes. Renaming a table keeps its existing index names, so bring them in
-- line with scripts/sqlserver/schema.sql and the EF Core model.
-- ---------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.indexes
            WHERE name = N'ix_search_request_status'
              AND object_id = OBJECT_ID(N'dbo.bulk_search_request'))
    EXEC sp_rename N'dbo.bulk_search_request.ix_search_request_status', N'ix_bulk_search_request_status', N'INDEX';

IF EXISTS (SELECT 1 FROM sys.indexes
            WHERE name = N'ix_search_request_claim'
              AND object_id = OBJECT_ID(N'dbo.bulk_search_request'))
    EXEC sp_rename N'dbo.bulk_search_request.ix_search_request_claim', N'ix_bulk_search_request_claim', N'INDEX';

IF EXISTS (SELECT 1 FROM sys.indexes
            WHERE name = N'ix_search_request_output'
              AND object_id = OBJECT_ID(N'dbo.bulk_search_request'))
    EXEC sp_rename N'dbo.bulk_search_request.ix_search_request_output', N'ix_bulk_search_request_output', N'INDEX';

IF EXISTS (SELECT 1 FROM sys.indexes
            WHERE name = N'ix_search_batch_date'
              AND object_id = OBJECT_ID(N'dbo.bulk_search_batch'))
    EXEC sp_rename N'dbo.bulk_search_batch.ix_search_batch_date', N'ix_bulk_search_batch_date', N'INDEX';

IF EXISTS (SELECT 1 FROM sys.indexes
            WHERE name = N'ix_search_batch_file'
              AND object_id = OBJECT_ID(N'dbo.bulk_search_batch'))
    EXEC sp_rename N'dbo.bulk_search_batch.ix_search_batch_file', N'ix_bulk_search_batch_file', N'INDEX';

IF EXISTS (SELECT 1 FROM sys.indexes
            WHERE name = N'ix_search_response_request'
              AND object_id = OBJECT_ID(N'dbo.bulk_search_response'))
    EXEC sp_rename N'dbo.bulk_search_response.ix_search_response_request', N'ix_bulk_search_response_request', N'INDEX';

IF EXISTS (SELECT 1 FROM sys.indexes
            WHERE name = N'ix_search_response_file_batch'
              AND object_id = OBJECT_ID(N'dbo.bulk_search_response_file'))
    EXEC sp_rename N'dbo.bulk_search_response_file.ix_search_response_file_batch', N'ix_bulk_search_response_file_batch', N'INDEX';

IF EXISTS (SELECT 1 FROM sys.indexes
            WHERE name = N'ix_search_response_file_hash'
              AND object_id = OBJECT_ID(N'dbo.bulk_search_response_file'))
    EXEC sp_rename N'dbo.bulk_search_response_file.ix_search_response_file_hash', N'ix_bulk_search_response_file_hash', N'INDEX';

IF EXISTS (SELECT 1 FROM sys.indexes
            WHERE name = N'ix_download_response_file_hash'
              AND object_id = OBJECT_ID(N'dbo.bulk_download_response_file'))
    EXEC sp_rename N'dbo.bulk_download_response_file.ix_download_response_file_hash', N'ix_bulk_download_response_file_hash', N'INDEX';

IF EXISTS (SELECT 1 FROM sys.indexes
            WHERE name = N'ix_download_response_line_file'
              AND object_id = OBJECT_ID(N'dbo.bulk_download_response_line'))
    EXEC sp_rename N'dbo.bulk_download_response_line.ix_download_response_line_file', N'ix_bulk_download_response_line_file', N'INDEX';

IF EXISTS (SELECT 1 FROM sys.indexes
            WHERE name = N'ix_download_response_artifact_file'
              AND object_id = OBJECT_ID(N'dbo.bulk_download_response_artifact'))
    EXEC sp_rename N'dbo.bulk_download_response_artifact.ix_download_response_artifact_file', N'ix_bulk_download_response_artifact_file', N'INDEX';

COMMIT TRANSACTION;
