-- ============================================================================
-- Adds the SFTP transport activities used by the `sftp push` / `sftp pull`
-- commands. Idempotent; safe to run more than once. The application logs these
-- activity types on master_record_attempt (Stage = SftpUpload / SftpDownload).
--
-- Apply against the CkycCentral database:
--   sqlcmd -S "(localdb)\MSSQLLocalDB" -d CkycCentral -b -i .\scripts\sqlserver\migrations\20260924_add_sftp_activity_types.sql
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM activity_type WHERE Code = 'SftpUpload')
    INSERT INTO activity_type (Code, Name, IsRetryable, MaxAttempts, BackoffBaseHours, BackoffMultiplier, IsActive, Remarks, CreatedAt)
    SELECT 'SftpUpload','Push validated batches to CERSAI over SFTP', 0, 3, 24, 2.0, 1,
           'Not retryable automatically: the transport is driven by the sftp push command.', SYSUTCDATETIME();
GO

IF NOT EXISTS (SELECT 1 FROM activity_type WHERE Code = 'SftpDownload')
    INSERT INTO activity_type (Code, Name, IsRetryable, MaxAttempts, BackoffBaseHours, BackoffMultiplier, IsActive, Remarks, CreatedAt)
    SELECT 'SftpDownload','Pull processed response files from CERSAI over SFTP', 0, 3, 24, 2.0, 1,
           'Not retryable automatically: the transport is driven by the sftp pull command.', SYSUTCDATETIME();
GO
