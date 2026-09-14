-- Pre-batch customer search (individual):
--   1. individual_search — one row per candidate search request for an individual customer
--      (a row per identity document held, plus a name/DOB/gender/relation fallback).
--   2. status_master seeds 12 SRP (PendingSearch), 13 SRD (Searched), 14 SRF (SearchFound).
--   3. activity_type seed 'Search' (retryable).
-- Run once against databases created from the pre-change schema. Every step is guarded, so
-- the script is safe to re-run and safe on a database that already has the new objects.
--
-- The application expects these at startup (SqlServerDatabase.cs), so run this before
-- deploying the matching build.

SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- ---------------------------------------------------------------------------
-- Table + indexes
-- ---------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.individual_search', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.individual_search (
        Id                       BIGINT IDENTITY(1,1) PRIMARY KEY,
        MasterRecordId           BIGINT,
        CustomerId               NVARCHAR(50),
        ClientType               NVARCHAR(1),
        SearchOption             INT,
        IdentityTypeAndNumber    NVARCHAR(2000),
        FirstName                NVARCHAR(33),
        MiddleName               NVARCHAR(33),
        LastName                 NVARCHAR(33),
        DateOfBirth              NVARCHAR(10),
        LegalEntityName          NVARCHAR(99),
        DateOfIncorporation      NVARCHAR(10),
        Gender                   NVARCHAR(1),
        PhotoReferenceNumber     NVARCHAR(40),
        Relation                 NVARCHAR(50),
        RelationFirstName        NVARCHAR(33),
        RelationMiddleName       NVARCHAR(33),
        RelationLastName         NVARCHAR(33),
        MobileNumber             NVARCHAR(10),
        VerifiableCredential     NVARCHAR(50),
        Constitution             NVARCHAR(1),
        RawRequestJson           NVARCHAR(MAX),
        ProcessingStatus         INT,
        ClaimToken               NVARCHAR(36),
        ClaimedAt                DATETIME2,
        ProcessedAt              DATETIME2,
        Outcome                  NVARCHAR(20),
        SearchKey                NVARCHAR(20),
        CkycReferenceNumber      NVARCHAR(15),
        ResponseRemark           NVARCHAR(250),
        ResponseReadAt           DATETIME2,
        RawResponseJson          NVARCHAR(MAX),
        LastError                NVARCHAR(2000),
        CreatedAt                DATETIME2,
        UpdatedAt                DATETIME2
    );
    CREATE INDEX ix_individual_search_status   ON dbo.individual_search (ProcessingStatus, Id);
    CREATE INDEX ix_individual_search_master   ON dbo.individual_search (MasterRecordId);
    CREATE INDEX ix_individual_search_customer ON dbo.individual_search (CustomerId);
    CREATE INDEX ix_individual_search_claim    ON dbo.individual_search (ClaimToken);
END

-- ---------------------------------------------------------------------------
-- Backfill master_record.ClientType. The daily fetch historically left it NULL;
-- in this pipeline NULL means an individual record (legal entities are always
-- inserted with 'L' via insert-legal). The stage queries filter on ClientType.
-- ---------------------------------------------------------------------------
UPDATE dbo.master_record SET ClientType = 'I' WHERE ClientType IS NULL;

-- ---------------------------------------------------------------------------
-- Status master (append-only)
-- ---------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM dbo.status_master WHERE StatusValue = 12)
    INSERT INTO dbo.status_master (StatusValue, Code, Name, Description, IsTerminal, IsActive, CreatedAt)
    VALUES (12,'SRP','PendingSearch','Individual details are saved and the record is awaiting the pre-batch customer search.',0,1,SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM dbo.status_master WHERE StatusValue = 13)
    INSERT INTO dbo.status_master (StatusValue, Code, Name, Description, IsTerminal, IsActive, CreatedAt)
    VALUES (13,'SRD','Searched','Customer search completed without a match; the API search key is written to record 20 and the record is ready to batch.',0,1,SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM dbo.status_master WHERE StatusValue = 14)
    INSERT INTO dbo.status_master (StatusValue, Code, Name, Description, IsTerminal, IsActive, CreatedAt)
    VALUES (14,'SRF','SearchFound','Customer search found an existing CKYC record; the customer already exists and is not pushed through creation again.',1,1,SYSUTCDATETIME());

-- ---------------------------------------------------------------------------
-- Activity type master (retryable search)
-- ---------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM dbo.activity_type WHERE Code = 'Search')
    INSERT INTO dbo.activity_type (Code, Name, IsRetryable, MaxAttempts, BackoffBaseHours, BackoffMultiplier, IsActive, Remarks, CreatedAt)
    VALUES ('Search','Pre-batch customer search against the CKYCR search API',1,3,24,2.0,1,
            'Retryable: the search API can fail transiently; exponential backoff 24h, max 3 tries.',SYSUTCDATETIME());

COMMIT TRANSACTION;
GO
