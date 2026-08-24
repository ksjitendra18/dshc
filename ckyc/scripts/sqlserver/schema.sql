-- ============================================================================
-- Centralized CKYC — SQL Server schema (production reference)
-- Mirrors the SQLite DDL in CKYC.Data/Schema/Ddl.cs. Columns use ONLY a length
-- plus the identity primary key: no NOT NULL / UNIQUE / CHECK / FK constraints,
-- exactly as required ("length validation yes, other validation no").
--
-- Usage: point the app at SQL Server by setting database.provider=sqlserver and
-- supplying a matching connection string, and run this script on the target DB.
-- ============================================================================

CREATE TABLE master_record (
    Id                           BIGINT IDENTITY(1,1) PRIMARY KEY,
    SourceCustomerId             NVARCHAR(50),
    BusinessDate                 DATE,
    Status                       INT,
    Remarks                      NVARCHAR(500),
    RetryCount                   INT NOT NULL CONSTRAINT DF_master_retry DEFAULT (0),
    LastError                    NVARCHAR(1000),
    LastAttemptAt                DATETIME2,
    LastActivity                 NVARCHAR(50),
    NextRetryAt                  DATETIME2,
    NeedsReconcile               INT,
    ReattemptCount               INT,
    ReattemptedAt                DATETIME2,
    BatchFile                    NVARCHAR(260),
    BatchRecordLine              INT,
    IsCrmFetched                 INT,
    IsSaved                      INT,
    IsBatched                    INT,
    IsUploaded                   INT,
    IsResponseRead               INT,
    IsReconciled                 INT,
    IsRejected                   INT,
    CrmFetchedAt                 DATETIME2,
    SavedAt                      DATETIME2,
    BatchedAt                    DATETIME2,
    UploadedAt                   DATETIME2,
    FirstResponseAt              DATETIME2,
    ReconciledAt                 DATETIME2,
    LastResponseFileNumber       INT,
    LastResponseFileName         NVARCHAR(260),
    LastResponseAckNumber        NVARCHAR(10),
    LastResponseStatus           NVARCHAR(2),
    LastResponseCkycReference    NVARCHAR(15),
    LastResponseCkycNumber       NVARCHAR(15),
    LastResponseRejectionRemark  NVARCHAR(500),
    LastResponseReadAt           DATETIME2,
    LastResponseRemarks          NVARCHAR(1000),
    ReconStatus                  NVARCHAR(50),
    ReconRemarks                 NVARCHAR(1000),
    CreatedAt                    DATETIME2,
    UpdatedAt                    DATETIME2
);
CREATE INDEX ix_master_customer   ON master_record (SourceCustomerId);
CREATE INDEX ix_master_status     ON master_record (Status);
CREATE INDEX ix_master_batchline  ON master_record (BatchFile, BatchRecordLine);
GO

CREATE TABLE kyc_record_20 (
    Id                              BIGINT IDENTITY(1,1) PRIMARY KEY,
    MasterRecordId                  BIGINT,
    SourceCustomerId                NVARCHAR(50),
    SearchKey                       NVARCHAR(20),
    KycType                         NVARCHAR(1),
    NameTitle                       NVARCHAR(4),
    NameFirst                       NVARCHAR(33),
    NameMiddle                      NVARCHAR(33),
    NameLast                        NVARCHAR(33),
    MaidenTitle                     NVARCHAR(4),
    MaidenFirst                     NVARCHAR(33),
    MaidenMiddle                    NVARCHAR(33),
    MaidenLast                      NVARCHAR(33),
    MotherTitle                     NVARCHAR(4),
    MotherFirst                     NVARCHAR(33),
    MotherMiddle                    NVARCHAR(33),
    MotherLast                      NVARCHAR(33),
    FatherTitle                     NVARCHAR(4),
    FatherFirst                     NVARCHAR(33),
    FatherMiddle                    NVARCHAR(33),
    FatherLast                      NVARCHAR(33),
    SpouseTitle                     NVARCHAR(4),
    SpouseFirst                     NVARCHAR(33),
    SpouseMiddle                    NVARCHAR(33),
    SpouseLast                      NVARCHAR(33),
    DateOfBirth                     NVARCHAR(10),
    Gender                          NVARCHAR(1),
    ResidentialStatus               NVARCHAR(50),
    ResidentialSupportedByDocument  NVARCHAR(1),
    Nationality                     NVARCHAR(2),
    NationalitySupportedByDocument  NVARCHAR(1),
    DifferentlyAbledStatus          NVARCHAR(1),
    DifferentlyAbledType            NVARCHAR(50),
    Pan                             NVARCHAR(125),
    PanVerified                     NVARCHAR(1),
    PhotoOfIndividual               NVARCHAR(125),
    CreatedAt                       DATETIME2,
    UpdatedAt                       DATETIME2
);
GO

CREATE TABLE kyc_record_30 (
    Id                           BIGINT IDENTITY(1,1) PRIMARY KEY,
    MasterRecordId               BIGINT,
    Record20LineNumber           INT,
    OvdType                      NVARCHAR(1),
    ModeOfAadhaarVerification    NVARCHAR(50),
    PassportExpiryDate           NVARCHAR(10),
    DrivingLicenseExpiryDate     NVARCHAR(10),
    LengthOfAadhaar              NVARCHAR(1),
    IdNumber                     NVARCHAR(100),
    CertifiedCopyWithOriginal    NVARCHAR(1),
    EquivalentEDoc               NVARCHAR(1),
    VerifiedFromDigiLocker       NVARCHAR(1),
    PresenceInMeaRepository      NVARCHAR(1),
    PresenceInEciRepository      NVARCHAR(1),
    PresenceInRtoRepository      NVARCHAR(1),
    PresenceInNregaRepository    NVARCHAR(1),
    PresenceInNprRecords         NVARCHAR(1),
    DataFromOfflineVerification  NVARCHAR(1),
    ModeOfAuthentication         NVARCHAR(1),
    EkycDataFromUidai            NVARCHAR(1),
    CopyOfOvd                    NVARCHAR(125)
);
GO

CREATE TABLE kyc_record_40 (
    Id                         BIGINT IDENTITY(1,1) PRIMARY KEY,
    MasterRecordId             BIGINT,
    Record20LineNumber         INT,
    PermLine1                  NVARCHAR(60),
    PermLine2                  NVARCHAR(60),
    PermLine3                  NVARCHAR(60),
    PermCountry                NVARCHAR(2),
    PermState                  NVARCHAR(2),
    PermDistrict               NVARCHAR(6),
    PermCity                   NVARCHAR(60),
    PermPinCode                NVARCHAR(6),
    PermPinOthers              NVARCHAR(6),
    PermDigipin                NVARCHAR(10),
    PermSupportedDocument      NVARCHAR(1),
    PermMatchOvd               NVARCHAR(1),
    CurrLine1                  NVARCHAR(60),
    CurrLine2                  NVARCHAR(60),
    CurrLine3                  NVARCHAR(60),
    CurrCountry                NVARCHAR(2),
    CurrState                  NVARCHAR(2),
    CurrDistrict               NVARCHAR(6),
    CurrCity                   NVARCHAR(60),
    CurrPinCode                NVARCHAR(6),
    CurrPinOthers              NVARCHAR(6),
    CurrDigipin                NVARCHAR(10),
    CurrSupportedDocument      NVARCHAR(1),
    CurrMatchOvd               NVARCHAR(1)
);
GO

CREATE TABLE kyc_record_50 (
    Id                          BIGINT IDENTITY(1,1) PRIMARY KEY,
    MasterRecordId              BIGINT,
    Record20LineNumber          INT,
    EmailAddress                NVARCHAR(254),
    CountryCode                 NVARCHAR(4),
    MobileNumber                NVARCHAR(15),
    MobileValidatedViaOtp       NVARCHAR(1),
    EmailValidatedViaOtp        NVARCHAR(1),
    MobileValidatedViaThirdParty NVARCHAR(1)
);
GO

CREATE TABLE kyc_record_60 (
    Id                          BIGINT IDENTITY(1,1) PRIMARY KEY,
    MasterRecordId              BIGINT,
    Record20LineNumber          INT,
    RelatedPersonType           NVARCHAR(1),
    CkycNumberOfRelatedPerson   NVARCHAR(14)
);
GO

CREATE TABLE kyc_record_70 (
    Id                         BIGINT IDENTITY(1,1) PRIMARY KEY,
    MasterRecordId             BIGINT,
    Record20LineNumber         INT,
    Remarks                    NVARCHAR(200),
    VideoKycWithoutOfficial    NVARCHAR(1),
    VideoKycWithReOfficial     NVARCHAR(1),
    FaceToFaceWithReOfficial   NVARCHAR(1),
    NonFaceToFace              NVARCHAR(1),
    FaceToFaceWithNonOfficial  NVARCHAR(1),
    AttestationDate            NVARCHAR(10),
    EmployeeName               NVARCHAR(50),
    EmployeeCode               NVARCHAR(50),
    EmployeeDesignation        NVARCHAR(50),
    EmployeeBranch             NVARCHAR(50),
    EmployeeCkycId             NVARCHAR(50),
    InstitutionName            NVARCHAR(50),
    InstitutionCode            NVARCHAR(50),
    DeclarationDocument        NVARCHAR(125),
    DeclarationFlag            NVARCHAR(1),
    ClientConsent              NVARCHAR(125),
    Place                      NVARCHAR(40),
    DeclarationDate            NVARCHAR(10)
);
GO

CREATE TABLE batch (
    Id             BIGINT IDENTITY(1,1) PRIMARY KEY,
    BatchKey       NVARCHAR(60),
    UploadFileName NVARCHAR(260),
    UploadFilePath NVARCHAR(1000),
    ZipPath        NVARCHAR(1000),
    RecordCount    INT,
    CreatedAt      DATETIME2
);
GO

CREATE TABLE fvu_run (
    Id            BIGINT IDENTITY(1,1) PRIMARY KEY,
    BatchKey      NVARCHAR(60),
    Executed      INT,
    ExitCode      INT,
    Passed        INT,
    SummaryJson   NVARCHAR(MAX),
    OutputZipPath NVARCHAR(1000),
    HashValue     NVARCHAR(128),
    ErrorMessage  NVARCHAR(2000),
    CreatedAt     DATETIME2
);
GO

CREATE TABLE master_record_response (
    Id                    BIGINT IDENTITY(1,1) PRIMARY KEY,
    MasterRecordId        BIGINT,
    SourceCustomerId      NVARCHAR(50),
    BatchFile             NVARCHAR(260),
    ResponseFileNumber    INT,
    ResponseFileName      NVARCHAR(260),
    LineNumber            INT,
    InputRecordLineNumber INT,
    AckNumber             NVARCHAR(10),
    RecordStatus          NVARCHAR(2),
    CkycReferenceNumber   NVARCHAR(15),
    CkycNumber            NVARCHAR(15),
    RejectionRemark       NVARCHAR(500),
    ReadAt                DATETIME2,
    Remarks               NVARCHAR(1000),
    RawData               NVARCHAR(4000),
    CreatedAt             DATETIME2
);
CREATE INDEX ix_master_response_master ON master_record_response (MasterRecordId);
GO

CREATE TABLE master_record_attempt (
    Id             BIGINT IDENTITY(1,1) PRIMARY KEY,
    MasterRecordId BIGINT,
    SourceCustomerId NVARCHAR(50),
    Stage          NVARCHAR(50),
    ActivityTypeId BIGINT,
    Attempt        INT,
    Status         INT,
    Success        INT,
    Error          NVARCHAR(1000),
    Remarks        NVARCHAR(1000),
    AttemptedAt    DATETIME2,
    NextRetryAt    DATETIME2,
    CreatedAt      DATETIME2
);
CREATE INDEX ix_master_attempt_master ON master_record_attempt (MasterRecordId);
GO

CREATE TABLE activity_type (
    Id                 BIGINT IDENTITY(1,1) PRIMARY KEY,
    Code               NVARCHAR(50),
    Name               NVARCHAR(100),
    IsRetryable        INT,
    MaxAttempts        INT,
    BackoffBaseHours   INT,
    BackoffMultiplier  FLOAT,
    IsActive           INT,
    Remarks            NVARCHAR(500),
    CreatedAt          DATETIME2
);
CREATE INDEX ix_activity_code ON activity_type (Code);
GO

CREATE TABLE status_master (
    Id             BIGINT IDENTITY(1,1) PRIMARY KEY,
    StatusValue    INT,
    Code           NVARCHAR(3),
    Name           NVARCHAR(50),
    Description    NVARCHAR(500),
    IsTerminal     INT,
    IsActive       INT,
    CreatedAt      DATETIME2
);
CREATE INDEX ix_status_value ON status_master (StatusValue);
CREATE INDEX ix_status_code   ON status_master (Code);
GO

CREATE TABLE master_record_reattempt (
    Id                              BIGINT IDENTITY(1,1) PRIMARY KEY,
    MasterRecordId                  BIGINT,
    SourceCustomerId                NVARCHAR(50),
    Reason                          NVARCHAR(1000),
    PreviousStatus                  INT,
    PreviousReconStatus             NVARCHAR(50),
    PreviousResponseStatus          NVARCHAR(2),
    PreviousResponseAckNumber       NVARCHAR(10),
    PreviousResponseCkycReference   NVARCHAR(15),
    PreviousResponseCkycNumber      NVARCHAR(15),
    PreviousResponseRejectionRemark NVARCHAR(500),
    PreviousResponseReadAt          DATETIME2,
    PreviousRetryCount              INT,
    ReattemptCount                  INT,
    ReattemptedAt                   DATETIME2,
    CreatedAt                       DATETIME2
);
CREATE INDEX ix_master_reattempt_master ON master_record_reattempt (MasterRecordId);
GO
