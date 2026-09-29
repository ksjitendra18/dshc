-- ============================================================================
-- Adds the pre-search image/document fetch step:
--   * master_record.DocumentKey   — the step-1 document key (dockey)
--   * master_record.IsImageFetched / ImageFetchedAt
--   * status_master 15 IMP (ImagePending) and 16 IMF (ImageFailed)
--   * activity_type 'ImageFetch' (retryable)
-- Idempotent; safe to run more than once.
--
-- Apply against the CkycCentral database:
--   sqlcmd -S "(localdb)\MSSQLLocalDB" -d CkycCentral -b -i .\scripts\sqlserver\migrations\20260928_add_document_fetch.sql
-- ============================================================================

IF COL_LENGTH(N'dbo.master_record', N'DocumentKey') IS NULL
    ALTER TABLE master_record ADD DocumentKey NVARCHAR(200);
GO

IF COL_LENGTH(N'dbo.master_record', N'IsImageFetched') IS NULL
    ALTER TABLE master_record ADD IsImageFetched INT;
GO

IF COL_LENGTH(N'dbo.master_record', N'ImageFetchedAt') IS NULL
    ALTER TABLE master_record ADD ImageFetchedAt DATETIME2;
GO

IF NOT EXISTS (SELECT 1 FROM status_master WHERE StatusValue = 15)
    INSERT INTO status_master (StatusValue, Code, Name, Description, IsTerminal, IsActive, CreatedAt)
    SELECT 15,'IMP','ImagePending','Individual details are saved and the record is awaiting its supporting image/document from the intake channel source.',0,1,SYSUTCDATETIME();
GO

IF NOT EXISTS (SELECT 1 FROM status_master WHERE StatusValue = 16)
    INSERT INTO status_master (StatusValue, Code, Name, Description, IsTerminal, IsActive, CreatedAt)
    SELECT 16,'IMF','ImageFailed','The supporting image/document could not be fetched; the record is blocked from batching and is retryable.',0,1,SYSUTCDATETIME();
GO

IF NOT EXISTS (SELECT 1 FROM activity_type WHERE Code = 'ImageFetch')
    INSERT INTO activity_type (Code, Name, IsRetryable, MaxAttempts, BackoffBaseHours, BackoffMultiplier, IsActive, Remarks, CreatedAt)
    SELECT 'ImageFetch','Fetch the record''s image/document from the channel source (beckyc SFTP)', 1, 3, 24, 2.0, 1,
           'Retryable: the image/document may not yet be on the source; exponential backoff 24h, max 3 tries.', SYSUTCDATETIME();
GO
