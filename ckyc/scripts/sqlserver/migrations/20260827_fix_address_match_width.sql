-- Correct SQLite-to-SQL Server width regression against the supplied CKYC workbooks.
-- individual format sheets 40 define the match classifications as:
--   Exact Match / No Match / Partial Match (maximum length 13).
-- Run once against databases created from the earlier NVARCHAR(1) schema.

SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.individual_record_40', N'PermMatchOvd') IS NOT NULL
    ALTER TABLE dbo.individual_record_40 ALTER COLUMN PermMatchOvd NVARCHAR(13) NULL;

IF COL_LENGTH(N'dbo.individual_record_40', N'CurrMatchOvd') IS NOT NULL
    ALTER TABLE dbo.individual_record_40 ALTER COLUMN CurrMatchOvd NVARCHAR(13) NULL;

IF COL_LENGTH(N'dbo.individual_record_40', N'CurrAddressExactlyMatch') IS NOT NULL
    ALTER TABLE dbo.individual_record_40 ALTER COLUMN CurrAddressExactlyMatch NVARCHAR(13) NULL;

COMMIT TRANSACTION;

-- Deliberately do not infer a classification from legacy Y/N values. Review and
-- update every returned row to Exact Match, No Match or Partial Match.
SELECT Id, MasterRecordId, CustomerId, PermMatchOvd, CurrAddressExactlyMatch
  FROM dbo.individual_record_40
 WHERE (PermMatchOvd IS NOT NULL
        AND PermMatchOvd NOT IN (N'Exact Match', N'No Match', N'Partial Match'))
    OR (CurrAddressExactlyMatch IS NOT NULL
        AND CurrAddressExactlyMatch NOT IN (N'Exact Match', N'No Match', N'Partial Match'));
