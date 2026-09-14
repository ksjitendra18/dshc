-- Add master_record.Source — the intake channel that created the record.
-- Values are lowercase text and append-only: 'app', 'beckyc' (more later).
-- Existing rows are backfilled to 'beckyc', the default for records created
-- before the column existed. Run once against databases created from the
-- pre-change schema; the step is guarded so the script is safe to re-run.
--
-- The application expects the column at startup (SqlServerDatabase.cs), so run
-- this before deploying the matching build.

SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.master_record', N'Source') IS NULL
BEGIN
    ALTER TABLE dbo.master_record
        ADD Source NVARCHAR(20)
            CONSTRAINT DF_master_record_source DEFAULT ('beckyc') WITH VALUES;
END

COMMIT TRANSACTION;
GO
