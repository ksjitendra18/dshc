-- ============================================================================
-- Centralized CKYC — SQL Server schema v2 (production reference + LocalDB dev)
--
-- v2 changes (SQLite -> SQL Server migration):
--   1. individual_record_20..70   (renamed from kyc_record_20..70)
--   2. Documents split per client type:
--        customer_document -> individual_document + legal_entity_document
--      (shared dedup table file_content stays)
--   3. master_record.StatusCode NVARCHAR(3) — 2-3 char status code kept in sync
--      with the numeric Status by the application on every status update
--      (PND CRM SAV BAT FVP FVF FLD UPL RSP RCN REJ + new DTF).
--   4. status_master gains StatusValue=11 / DTF (Data Fetch Failed). The
--      pipeline still treats a failed CBS fetch as retryable Pending; DTF is
--      available for operators/reports. Append-only — never renumber.
--   5. Every table carries the denormalized CustomerId alongside MasterRecordId
--      (kept as-is per design).
--   6. Record-40 address match classifications use NVARCHAR(13), as required for
--      Exact Match / No Match / Partial Match by the individual workbooks.
--
-- Column definitions use ONLY a length (NVARCHAR(n)) plus the identity primary
-- key: no NOT NULL / UNIQUE / CHECK / FK constraints — except the document and
-- file-content tables where binary integrity is enforced deliberately.
--
-- Usage: sqlcmd -S "(localdb)\MSSQLLocalDB" -d CkycCentral -i schema.sql
-- ============================================================================

CREATE TABLE master_record (
    Id                           BIGINT IDENTITY(1,1) PRIMARY KEY,
    CustomerId                   NVARCHAR(50),
    ClientType                   NVARCHAR(1),
    BusinessDate                 DATE,
    Status                       INT,
    StatusCode                   NVARCHAR(3),
    Remarks                      NVARCHAR(500),
    RetryCount                   INT NOT NULL CONSTRAINT DF_master_record_retry DEFAULT (0),
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
CREATE INDEX ix_master_customer_id  ON master_record (CustomerId);
CREATE INDEX ix_master_status_id    ON master_record (Status, Id);
CREATE INDEX ix_master_batchline    ON master_record (BatchFile, BatchRecordLine);
CREATE INDEX ix_master_retry_picker ON master_record (Status, RetryCount, LastActivity, NextRetryAt);
GO

-- ============================================================================
-- INDIVIDUAL record tables (client type I). Renamed from kyc_record_* in v2.
-- ============================================================================

CREATE TABLE individual_record_20 (
    Id                              BIGINT IDENTITY(1,1) PRIMARY KEY,
    MasterRecordId                  BIGINT,
    CustomerId                      NVARCHAR(50),
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
    Minor                           NVARCHAR(1),
    DoBMatchWithOvd                 NVARCHAR(1),
    NameMatchWithOvd                NVARCHAR(1),
    PhotoMatchWithOvd               NVARCHAR(1),
    GenderProvidedInOvd             NVARCHAR(1),
    GenderMatchWithOvd              NVARCHAR(1),
    Form97Provided                  NVARCHAR(1),
    Form61Provided                  NVARCHAR(1),
    PanDocument                     NVARCHAR(125),
    OtherTypeOfImpairment           NVARCHAR(150),
    DisabilityReferenceNumber       NVARCHAR(18),
    PermanentDisability             NVARCHAR(1),
    DisabilityDate                  NVARCHAR(10),
    PercentageOfImpairment          NVARCHAR(3),
    DifferentlyAbledSupportedByDocument NVARCHAR(1),
    CreatedAt                       DATETIME2,
    UpdatedAt                       DATETIME2
);
CREATE INDEX ix_individual_record20_customer ON individual_record_20 (CustomerId);
CREATE INDEX ix_individual_record20_master   ON individual_record_20 (MasterRecordId);
GO

CREATE TABLE individual_record_30 (
    Id                           BIGINT IDENTITY(1,1) PRIMARY KEY,
    MasterRecordId               BIGINT,
    CustomerId                   NVARCHAR(50),
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
CREATE INDEX ix_individual_record30_customer ON individual_record_30 (CustomerId);
CREATE INDEX ix_individual_record30_master   ON individual_record_30 (MasterRecordId);
GO

CREATE TABLE individual_record_40 (
    Id                         BIGINT IDENTITY(1,1) PRIMARY KEY,
    MasterRecordId             BIGINT,
    CustomerId                 NVARCHAR(50),
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
    PermMatchOvd               NVARCHAR(13),
    CurrSameAsPermanent        NVARCHAR(1),
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
    CurrMatchOvd               NVARCHAR(13),
    CurrProofOfAddress         NVARCHAR(1),
    CurrProofOfAddressType     NVARCHAR(1),
    CurrLengthOfAadhaar        NVARCHAR(1),
    CurrIdNumber               NVARCHAR(100),
    CurrAadhaarVerification    NVARCHAR(1),
    CurrOvdExpiryDate          NVARCHAR(10),
    CurrDeemedPoa              NVARCHAR(2),
    CurrDeemedPoaVerified      NVARCHAR(1),
    CurrCertifiedCopy          NVARCHAR(1),
    CurrEquivalentEDoc         NVARCHAR(1),
    CurrDigiLockerVerified     NVARCHAR(1),
    CurrRemoteGeoTagging       NVARCHAR(1),
    CurrAddressExactlyMatch    NVARCHAR(13),
    CurrPositiveVerification   NVARCHAR(1),
    CurrPhysicalThirdParty     NVARCHAR(1),
    CurrPhysicalReOfficial     NVARCHAR(1),
    CurrPresenceInRepository   NVARCHAR(1),
    CurrForeignGovDocument     NVARCHAR(125),
    CurrCopyOfOvd              NVARCHAR(125)
);
CREATE INDEX ix_individual_record40_customer ON individual_record_40 (CustomerId);
CREATE INDEX ix_individual_record40_master   ON individual_record_40 (MasterRecordId);
GO

CREATE TABLE individual_record_50 (
    Id                          BIGINT IDENTITY(1,1) PRIMARY KEY,
    MasterRecordId              BIGINT,
    CustomerId                  NVARCHAR(50),
    Record20LineNumber          INT,
    EmailAddress                NVARCHAR(254),
    CountryCode                 NVARCHAR(4),
    MobileNumber                NVARCHAR(15),
    MobileValidatedViaOtp       NVARCHAR(1),
    EmailValidatedViaOtp        NVARCHAR(1),
    MobileValidatedViaThirdParty NVARCHAR(1)
);
CREATE INDEX ix_individual_record50_customer ON individual_record_50 (CustomerId);
CREATE INDEX ix_individual_record50_master   ON individual_record_50 (MasterRecordId);
GO

CREATE TABLE individual_record_60 (
    Id                          BIGINT IDENTITY(1,1) PRIMARY KEY,
    MasterRecordId              BIGINT,
    CustomerId                  NVARCHAR(50),
    Record20LineNumber          INT,
    RelatedPersonType           NVARCHAR(30),
    CkycNumberOfRelatedPerson   NVARCHAR(14)
);
CREATE INDEX ix_individual_record60_customer ON individual_record_60 (CustomerId);
CREATE INDEX ix_individual_record60_master   ON individual_record_60 (MasterRecordId);
GO

CREATE TABLE individual_record_70 (
    Id                         BIGINT IDENTITY(1,1) PRIMARY KEY,
    MasterRecordId             BIGINT,
    CustomerId                 NVARCHAR(50),
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
CREATE INDEX ix_individual_record70_customer ON individual_record_70 (CustomerId);
CREATE INDEX ix_individual_record70_master   ON individual_record_70 (MasterRecordId);
GO

-- ============================================================================
-- LEGAL ENTITY record tables (client type L). Deliberately separate from the
-- individual individual_record_* tables — a legal entity never shares a row
-- with a retail customer.
-- ============================================================================

CREATE TABLE legal_entity_record_20 (
    Id                              BIGINT IDENTITY(1,1) PRIMARY KEY,
    MasterRecordId                  BIGINT,
    CustomerId                      NVARCHAR(50),
    SearchKey                       NVARCHAR(20),
    EntityName                      NVARCHAR(99),
    EntityConstitution              NVARCHAR(2),
    ListedCompany                   NVARCHAR(1),
    RegisteredFirm                  NVARCHAR(1),
    RegisteredTrust                 NVARCHAR(1),
    DateOfIncorporation             NVARCHAR(10),
    DateOfCommencement              NVARCHAR(10),
    PlaceOfIncorporation            NVARCHAR(50),
    CountryOfIncorporation          NVARCHAR(2),
    TinIssuingCountry               NVARCHAR(2),
    Pan                             NVARCHAR(10),
    Form97                          NVARCHAR(1),
    TinGstNumber                    NVARCHAR(15),
    PanDocument                     NVARCHAR(125),
    PanVerified                     NVARCHAR(1),
    TinGstnDocument                 NVARCHAR(125),
    CreatedAt                       DATETIME2,
    UpdatedAt                       DATETIME2
);
CREATE INDEX ix_le_record20_customer ON legal_entity_record_20 (CustomerId);
CREATE INDEX ix_le_record20_master   ON legal_entity_record_20 (MasterRecordId);
GO

CREATE TABLE legal_entity_record_30 (
    Id                               BIGINT IDENTITY(1,1) PRIMARY KEY,
    MasterRecordId                   BIGINT,
    CustomerId                       NVARCHAR(50),
    Record20LineNumber               INT,
    CertificateOfIncorporation       NVARCHAR(125),
    Cin                              NVARCHAR(21),
    MemorandumAndArticles            NVARCHAR(125),
    ResolutionBoardPoA               NVARCHAR(125),
    NamesSeniorManagement            NVARCHAR(125),
    CertificateOfCommencement        NVARCHAR(125),
    OthersCompany                    NVARCHAR(125),
    RegistrationCertificate          NVARCHAR(125),
    RegistrationNumber               NVARCHAR(50),
    LlpinCertificate                 NVARCHAR(125),
    Llpin                            NVARCHAR(7),
    PartnershipDeed                  NVARCHAR(125),
    NamesAllPartners                 NVARCHAR(125),
    OthersPartnership                NVARCHAR(125),
    TrustRegistrationCertificate     NVARCHAR(125),
    TrustRegistrationNumber          NVARCHAR(50),
    TrustDeed                        NVARCHAR(125),
    NamesBeneficiariesTrustees       NVARCHAR(125),
    TrustPowerOfAttorney             NVARCHAR(125),
    OthersTrust                      NVARCHAR(125),
    UnincorporatedRegCertificate     NVARCHAR(125),
    UnincorporatedRegNumber          NVARCHAR(50),
    ResolutionManagingBody           NVARCHAR(125),
    UnincorporatedPowerOfAttorney    NVARCHAR(125),
    InfoEstablishExistence           NVARCHAR(125),
    OthersUnincorporated             NVARCHAR(125),
    SupportingDocumentsPoi           NVARCHAR(125),
    OtherTypeRegistrationNumber      NVARCHAR(50),
    OtherTypeRegistrationCertificate NVARCHAR(125),
    OtherTypePowerOfAttorney         NVARCHAR(125),
    ActivityProof1                   NVARCHAR(125),
    ActivityProof2                   NVARCHAR(125),
    OthersOtherType                  NVARCHAR(125)
);
CREATE INDEX ix_le_record30_customer ON legal_entity_record_30 (CustomerId);
CREATE INDEX ix_le_record30_master   ON legal_entity_record_30 (MasterRecordId);
GO

CREATE TABLE legal_entity_record_40 (
    Id                        BIGINT IDENTITY(1,1) PRIMARY KEY,
    MasterRecordId            BIGINT,
    CustomerId                NVARCHAR(50),
    Record20LineNumber        INT,
    RegLine1                  NVARCHAR(60),
    RegLine2                  NVARCHAR(60),
    RegLine3                  NVARCHAR(60),
    RegCity                   NVARCHAR(60),
    RegState                  NVARCHAR(2),
    RegDistrict               NVARCHAR(4),
    RegPinCode                NVARCHAR(6),
    RegPinOthers              NVARCHAR(6),
    RegDigipin                NVARCHAR(10),
    RegCountry                NVARCHAR(2),
    RegProofOfAddress         NVARCHAR(1),
    RegOtherDocumentName      NVARCHAR(50),
    RegDocument               NVARCHAR(125),
    SameAsRegistered          NVARCHAR(1),
    PrinLine1                 NVARCHAR(60),
    PrinLine2                 NVARCHAR(60),
    PrinLine3                 NVARCHAR(60),
    PrinCity                  NVARCHAR(60),
    PrinState                 NVARCHAR(2),
    PrinDistrict              NVARCHAR(4),
    PrinPinCode               NVARCHAR(6),
    PrinPinOthers             NVARCHAR(6),
    PrinDigipin               NVARCHAR(10),
    PrinCountry               NVARCHAR(2),
    PrinProofOfAddress        NVARCHAR(1),
    PrinOtherDocumentName     NVARCHAR(50),
    PrinDocument              NVARCHAR(125)
);
CREATE INDEX ix_le_record40_customer ON legal_entity_record_40 (CustomerId);
CREATE INDEX ix_le_record40_master   ON legal_entity_record_40 (MasterRecordId);
GO

CREATE TABLE legal_entity_record_50 (
    Id                          BIGINT IDENTITY(1,1) PRIMARY KEY,
    MasterRecordId              BIGINT,
    CustomerId                  NVARCHAR(50),
    Record20LineNumber          INT,
    CountryCode1                NVARCHAR(6),
    MobileNumber1               NVARCHAR(15),
    CountryCode2                NVARCHAR(6),
    MobileNumber2               NVARCHAR(15),
    EmailId1                    NVARCHAR(254),
    EmailId2                    NVARCHAR(254),
    Telephone                   NVARCHAR(12),
    Fax                         NVARCHAR(12)
);
CREATE INDEX ix_le_record50_customer ON legal_entity_record_50 (CustomerId);
CREATE INDEX ix_le_record50_master   ON legal_entity_record_50 (MasterRecordId);
GO

CREATE TABLE legal_entity_record_60 (
    Id                         BIGINT IDENTITY(1,1) PRIMARY KEY,
    MasterRecordId             BIGINT,
    CustomerId                 NVARCHAR(50),
    Record20LineNumber         INT,
    NumberOfRelatedPersons     INT,
    NumberOfBeneficialOwners   INT,
    Relation                   NVARCHAR(60),
    CkycNumber                 NVARCHAR(14),
    ControllingInterest        NVARCHAR(50),
    PercentageOwnership        NVARCHAR(10),
    OtherRelationName          NVARCHAR(33),
    Din                        NVARCHAR(8)
);
CREATE INDEX ix_le_record60_customer ON legal_entity_record_60 (CustomerId);
CREATE INDEX ix_le_record60_master   ON legal_entity_record_60 (MasterRecordId);
GO

CREATE TABLE legal_entity_record_70 (
    Id                         BIGINT IDENTITY(1,1) PRIMARY KEY,
    MasterRecordId             BIGINT,
    CustomerId                 NVARCHAR(50),
    Record20LineNumber         INT,
    Remarks                    NVARCHAR(200),
    CertifiedCopies            NVARCHAR(1),
    EquivalentEDoc             NVARCHAR(1),
    VerificationFromDigiLocker NVARCHAR(1),
    AttestationDate            NVARCHAR(10),
    EmployeeName               NVARCHAR(99),
    EmployeeCode               NVARCHAR(50),
    EmployeeDesignation        NVARCHAR(50),
    EmployeeBranch             NVARCHAR(50),
    EmployeeCkycId             NVARCHAR(14),
    InstitutionName            NVARCHAR(99),
    InstitutionCode            NVARCHAR(50),
    DeclarationDocument        NVARCHAR(125),
    DeclarationFlag            NVARCHAR(1),
    ConsentDocument            NVARCHAR(125),
    Place                      NVARCHAR(40),
    DeclarationDate            NVARCHAR(10)
);
CREATE INDEX ix_le_record70_customer ON legal_entity_record_70 (CustomerId);
CREATE INDEX ix_le_record70_master   ON legal_entity_record_70 (MasterRecordId);
GO

-- ============================================================================
-- Batch ledger + FVU run ledger
-- ============================================================================

CREATE TABLE batch (
    Id             BIGINT IDENTITY(1,1) PRIMARY KEY,
    BatchKey       NVARCHAR(60),
    UploadFileName NVARCHAR(260),
    UploadFilePath NVARCHAR(1000),
    ZipPath        NVARCHAR(1000),
    RecordCount    INT,
    CreatedAt      DATETIME2
);
CREATE INDEX ix_batch_key ON batch (BatchKey);
GO

CREATE TABLE master_record_batch (
    Id                 BIGINT IDENTITY(1,1) PRIMARY KEY,
    MasterRecordId     BIGINT,
    CustomerId         NVARCHAR(50),
    BatchFile          NVARCHAR(260),
    Record20LineNumber INT,
    BatchedAt          DATETIME2
);
CREATE INDEX ix_master_record_batch_master   ON master_record_batch (MasterRecordId);
CREATE INDEX ix_master_record_batch_customer ON master_record_batch (CustomerId, BatchedAt);
CREATE INDEX ix_master_record_batch_fileline ON master_record_batch (BatchFile, Record20LineNumber);
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
CREATE INDEX ix_fvu_run_key ON fvu_run (BatchKey);
GO

-- CERSAI reply history: one row per (record, response-file-number)
CREATE TABLE master_record_response (
    Id                    BIGINT IDENTITY(1,1) PRIMARY KEY,
    MasterRecordId        BIGINT,
    CustomerId            NVARCHAR(50),
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

CREATE TABLE upload_response_file (
    Id                    BIGINT IDENTITY(1,1) PRIMARY KEY,
    BatchFile             NVARCHAR(260),
    ResponseFileName      NVARCHAR(260),
    ResponseFileNumber    INT,
    TotalRecords          INT,
    TotalProcessed        INT,
    UnderProcessing       INT,
    Failed                INT,
    ResponseTimestamp     NVARCHAR(30),
    RawHeaderData         NVARCHAR(MAX),
    SourceArchiveName     NVARCHAR(260),
    SourceHash            NVARCHAR(128),
    CreatedAt             DATETIME2
);
CREATE INDEX ix_upload_response_file_identity ON upload_response_file (SourceHash, ResponseFileName);
GO

-- Stage attempt / retry audit trail
CREATE TABLE master_record_attempt (
    Id             BIGINT IDENTITY(1,1) PRIMARY KEY,
    MasterRecordId BIGINT,
    CustomerId     NVARCHAR(50),
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

-- Activity type master: which processes are retryable + their retry policy
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

-- Status master: maps the master_record.Status integer (0-11) to a 2-3 char
-- code + readable description. Status stays INT; StatusCode is the denormalized
-- copy persisted on master_record itself.
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
CREATE INDEX ix_status_code  ON status_master (Code);
GO

-- Re-push (reattempt) history: one row per manual re-push of a rejected record
CREATE TABLE master_record_reattempt (
    Id                              BIGINT IDENTITY(1,1) PRIMARY KEY,
    MasterRecordId                  BIGINT,
    CustomerId                      NVARCHAR(50),
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

-- ============================================================================
-- CKYCR individual search: JSON intake, atomic processing and response
-- ProcessingStatus: 0 Pending, 1 Processing (claimed), 2 SRC generated, 3 Failed.
-- ============================================================================

CREATE TABLE search_request (
    Id                       BIGINT IDENTITY(1,1) PRIMARY KEY,
    ExternalRequestId        NVARCHAR(50),
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
    OutputFileName           NVARCHAR(260),
    OutputLineNumber         INT,
    ResponseStatus           NVARCHAR(50),
    LastSearchKey            NVARCHAR(20),
    LastCkycReference        NVARCHAR(15),
    LastResponseRemark       NVARCHAR(250),
    ResponseReadAt           DATETIME2,
    LastError                NVARCHAR(2000),
    CreatedAt                DATETIME2,
    UpdatedAt                DATETIME2
);
CREATE INDEX ix_search_request_status ON search_request (ProcessingStatus, Id);
CREATE INDEX ix_search_request_claim  ON search_request (ClaimToken);
CREATE INDEX ix_search_request_output ON search_request (OutputFileName, OutputLineNumber);
GO

CREATE TABLE search_batch (
    Id             BIGINT IDENTITY(1,1) PRIMARY KEY,
    BusinessDate   DATE,
    FileSequence   INT,
    ClaimToken     NVARCHAR(36),
    RecordCount    INT,
    Status         INT,
    FileName       NVARCHAR(260),
    FilePath       NVARCHAR(1000),
    FvuZipPath     NVARCHAR(1000),
    FvuHash        NVARCHAR(128),
    Error          NVARCHAR(2000),
    CreatedAt      DATETIME2,
    CompletedAt    DATETIME2
);
CREATE INDEX ix_search_batch_date ON search_batch (BusinessDate, FileSequence);
CREATE INDEX ix_search_batch_file ON search_batch (FileName);
GO

CREATE TABLE search_response (
    Id                          BIGINT IDENTITY(1,1) PRIMARY KEY,
    SearchRequestId             BIGINT,
    ResponseFileName            NVARCHAR(260),
    ResponseFileNumber          INT,
    LineNumber                  INT,
    InputRecordLineNumber       INT,
    ClientType                  NVARCHAR(1),
    SearchByOvdType             NVARCHAR(1),
    SearchByOvdNumber           NVARCHAR(15),
    SearchKey                   NVARCHAR(20),
    CkycReferenceNumber         NVARCHAR(15),
    FirstName                   NVARCHAR(33),
    MiddleName                  NVARCHAR(33),
    LastName                    NVARCHAR(33),
    Gender                      NVARCHAR(1),
    MobileNumber                NVARCHAR(12),
    EmailAddress                NVARCHAR(99),
    LastUpdatedDate             NVARCHAR(10),
    Cin                         NVARCHAR(40),
    LegalEntityName             NVARCHAR(150),
    PhotoReference              NVARCHAR(20),
    RegistrationDate            NVARCHAR(12),
    DeactivationReason          NVARCHAR(100),
    Remark                      NVARCHAR(250),
    PanDocument                 NVARCHAR(1),
    AadhaarDocument             NVARCHAR(1),
    PassportDocument            NVARCHAR(1),
    DrivingLicenseDocument      NVARCHAR(1),
    VoterIdDocument             NVARCHAR(1),
    NregaDocument               NVARCHAR(1),
    DisabilityDocument          NVARCHAR(1),
    Form6061Document            NVARCHAR(1),
    ForeignJurisdictionDocument NVARCHAR(1),
    NprDocument                 NVARCHAR(1),
    UtilityBillDocument         NVARCHAR(1),
    IncorporationDocument       NVARCHAR(1),
    MemorandumDocument          NVARCHAR(1),
    RegistrationCertificate     NVARCHAR(1),
    PartnershipDeed             NVARCHAR(1),
    TrustDeed                   NVARCHAR(1),
    SupportingPoiDocument       NVARCHAR(1),
    OtherDocument               NVARCHAR(1),
    Filler1                     NVARCHAR(1),
    Filler2                     NVARCHAR(1),
    Filler3                     NVARCHAR(1),
    Filler4                     NVARCHAR(1),
    Filler5                     NVARCHAR(1),
    Filler6                     NVARCHAR(1),
    Filler7                     NVARCHAR(1),
    Filler8                     NVARCHAR(1),
    RecordLevelHash             NVARCHAR(128),
    RawResponseData             NVARCHAR(MAX),
    CreatedAt                   DATETIME2
);
CREATE INDEX ix_search_response_request ON search_response (SearchRequestId);
GO

CREATE TABLE search_response_file (
    Id                     BIGINT IDENTITY(1,1) PRIMARY KEY,
    SearchBatchId          BIGINT,
    ResponseFileName       NVARCHAR(260),
    ResponseFileNumber     INT,
    FiCode                 NVARCHAR(6),
    RegionCode             NVARCHAR(11),
    TotalRecords           INT,
    TotalProcessed         INT,
    RecordsUnderProcessing INT,
    RecordsFailed          INT,
    ResponseTimestamp      NVARCHAR(20),
    Filler                 NVARCHAR(50),
    RawHeaderData          NVARCHAR(MAX),
    SourceArchiveName      NVARCHAR(260),
    SourceHash             NVARCHAR(128),
    CreatedAt              DATETIME2
);
CREATE INDEX ix_search_response_file_batch ON search_response_file (SearchBatchId);
CREATE INDEX ix_search_response_file_hash  ON search_response_file (SourceHash);
GO

-- ============================================================================
-- CKYCR download response: immutable file, record lines and ZIP artifacts
-- ============================================================================

CREATE TABLE download_response_file (
    Id                 BIGINT IDENTITY(1,1) PRIMARY KEY,
    ResponseFileName   NVARCHAR(260),
    ResponseFileNumber INT,
    FiCode             NVARCHAR(6),
    RegionCode         NVARCHAR(11),
    ClientType         NVARCHAR(1),
    TotalRecords       INT,
    Version            NVARCHAR(20),
    ResponseDate       NVARCHAR(30),
    RawHeaderData      NVARCHAR(MAX),
    SourceArchiveName  NVARCHAR(260),
    SourceHash         NVARCHAR(128),
    CreatedAt          DATETIME2
);
CREATE INDEX ix_download_response_file_hash ON download_response_file (SourceHash, ResponseFileName);
GO

CREATE TABLE download_response_line (
    Id                      BIGINT IDENTITY(1,1) PRIMARY KEY,
    DownloadResponseFileId  BIGINT,
    SourceEntryPath         NVARCHAR(1000),
    RecordType              NVARCHAR(2),
    LineNumber              INT,
    InputRecord20LineNumber INT,
    CkycNumber              NVARCHAR(15),
    RawData                 NVARCHAR(MAX),
    CreatedAt               DATETIME2
);
CREATE INDEX ix_download_response_line_file ON download_response_line (DownloadResponseFileId);
GO

CREATE TABLE download_response_artifact (
    Id                     BIGINT IDENTITY(1,1) PRIMARY KEY,
    DownloadResponseFileId BIGINT,
    EntryPath              NVARCHAR(1000),
    FileName               NVARCHAR(260),
    Size                   BIGINT,
    Sha256                 NVARCHAR(128),
    CreatedAt              DATETIME2
);
CREATE INDEX ix_download_response_artifact_file ON download_response_artifact (DownloadResponseFileId);
GO

-- ============================================================================
-- CKYCR bulk update: JSON intake, per-client-type claiming and .UPD.RESm
-- responses. Request rows mirror search_request; batches are generated
-- separately per client type ("I" individual / "L" legal entity).
-- ============================================================================

CREATE TABLE update_request (
    Id                      BIGINT IDENTITY(1,1) PRIMARY KEY,
    ExternalRequestId       NVARCHAR(50),
    CustomerId              NVARCHAR(50),
    ClientType              NVARCHAR(1),
    CkycNumber              NVARCHAR(14),
    ProcessingStatus        INT,
    ClaimToken              NVARCHAR(36),
    ClaimedAt               DATETIME2,
    ProcessedAt             DATETIME2,
    OutputFileName          NVARCHAR(260),
    OutputLineNumber        INT,
    OutputBatchKey          NVARCHAR(60),
    ResponseStatus          NVARCHAR(20),
    LastAckNumber           NVARCHAR(20),
    LastResponseStatusCode  NVARCHAR(2),
    LastResponseRemark      NVARCHAR(500),
    ResponseReadAt          DATETIME2,
    LastError               NVARCHAR(2000),
    RawRequestJson          NVARCHAR(MAX),
    CreatedAt               DATETIME2,
    UpdatedAt               DATETIME2
);
CREATE INDEX ix_update_request_status ON update_request (ProcessingStatus, Id);
CREATE INDEX ix_update_request_picker ON update_request (ClientType, ProcessingStatus, Id) INCLUDE (ClaimedAt);
CREATE INDEX ix_update_request_claim  ON update_request (ClaimToken);
CREATE INDEX ix_update_request_output ON update_request (OutputFileName, OutputLineNumber);
GO

CREATE TABLE update_batch (
    Id             BIGINT IDENTITY(1,1) PRIMARY KEY,
    BusinessDate   DATE,
    FileSequence   INT,
    ClientType     NVARCHAR(1),
    ClaimToken     NVARCHAR(36),
    RecordCount    INT,
    Status         INT,
    FileName       NVARCHAR(260),
    FilePath       NVARCHAR(1000),
    FvuZipPath     NVARCHAR(1000),
    FvuHash        NVARCHAR(128),
    Error          NVARCHAR(2000),
    CreatedAt      DATETIME2,
    CompletedAt    DATETIME2
);
CREATE INDEX ix_update_batch_date ON update_batch (BusinessDate, ClientType, FileSequence);
CREATE INDEX ix_update_batch_file ON update_batch (FileName);
GO

CREATE TABLE update_response_file (
    Id                     BIGINT IDENTITY(1,1) PRIMARY KEY,
    UpdateBatchId          BIGINT,
    ResponseFileName       NVARCHAR(260),
    ResponseFileNumber     INT,
    ClientType             NVARCHAR(1),
    FiCode                 NVARCHAR(6),
    RegionCode             NVARCHAR(11),
    TotalRecords           INT,
    TotalProcessed         INT,
    RecordsUnderProcessing INT,
    RecordsFailed          INT,
    ResponseTimestamp      NVARCHAR(30),
    Filler1                NVARCHAR(50),
    Filler2                NVARCHAR(50),
    RawHeaderData          NVARCHAR(MAX),
    SourceArchiveName      NVARCHAR(260),
    SourceHash             NVARCHAR(128),
    CreatedAt              DATETIME2
);
CREATE INDEX ix_update_response_file_hash ON update_response_file (SourceHash);
GO

CREATE TABLE update_response (
    Id                      BIGINT IDENTITY(1,1) PRIMARY KEY,
    UpdateRequestId         BIGINT,
    ResponseFileName        NVARCHAR(260),
    ResponseFileNumber      INT,
    LineNumber              INT,
    InputRecord20LineNumber INT,
    AckNumber               NVARCHAR(20),
    RecordStatus            NVARCHAR(2),
    CkycNumber              NVARCHAR(15),
    RejectionRemark         NVARCHAR(150),
    RawResponseData         NVARCHAR(MAX),
    CreatedAt               DATETIME2
);
GO

-- ============================================================================
-- Document store (v2: split per client type). file_content dedups binary
-- content by SHA-256; individual_document / legal_entity_document associate a
-- canonical-named file with a master record. Integrity is enforced here
-- because binary content must never become orphaned or ambiguously associated.
-- ============================================================================

CREATE TABLE file_content (
    Id          BIGINT IDENTITY(1,1) PRIMARY KEY,
    Sha256      CHAR(64) NOT NULL UNIQUE,
    Content     VARBINARY(MAX) NOT NULL,
    ByteLength  BIGINT NOT NULL CHECK (ByteLength > 0),
    CreatedAt   DATETIME2 NOT NULL
);
GO

CREATE TABLE individual_document (
    Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
    MasterRecordId      BIGINT NOT NULL,
    FileContentId       BIGINT NOT NULL,
    OriginalFileName    NVARCHAR(255) NOT NULL,
    CanonicalFileName   NVARCHAR(255) NOT NULL,
    MediaType           NVARCHAR(100) NOT NULL,
    DocumentKind        NVARCHAR(50),
    SourceType          NVARCHAR(30) NOT NULL,
    SourceReference     NVARCHAR(500),
    CreatedAt           DATETIME2 NOT NULL,
    UpdatedAt           DATETIME2 NOT NULL,
    CONSTRAINT fk_individual_document_master  FOREIGN KEY (MasterRecordId) REFERENCES master_record(Id) ON DELETE CASCADE,
    CONSTRAINT fk_individual_document_content FOREIGN KEY (FileContentId)  REFERENCES file_content(Id),
    CONSTRAINT uq_individual_document_name    UNIQUE (MasterRecordId, CanonicalFileName)
);
CREATE INDEX ix_individual_document_content ON individual_document (FileContentId);
GO

CREATE TABLE legal_entity_document (
    Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
    MasterRecordId      BIGINT NOT NULL,
    FileContentId       BIGINT NOT NULL,
    OriginalFileName    NVARCHAR(255) NOT NULL,
    CanonicalFileName   NVARCHAR(255) NOT NULL,
    MediaType           NVARCHAR(100) NOT NULL,
    DocumentKind        NVARCHAR(50),
    SourceType          NVARCHAR(30) NOT NULL,
    SourceReference     NVARCHAR(500),
    CreatedAt           DATETIME2 NOT NULL,
    UpdatedAt           DATETIME2 NOT NULL,
    CONSTRAINT fk_legal_entity_document_master  FOREIGN KEY (MasterRecordId) REFERENCES master_record(Id) ON DELETE CASCADE,
    CONSTRAINT fk_legal_entity_document_content FOREIGN KEY (FileContentId)  REFERENCES file_content(Id),
    CONSTRAINT uq_legal_entity_document_name    UNIQUE (MasterRecordId, CanonicalFileName)
);
CREATE INDEX ix_legal_entity_document_content ON legal_entity_document (FileContentId);
GO

-- ============================================================================
-- Seed data (idempotent)
-- ============================================================================

INSERT INTO activity_type (Code, Name, IsRetryable, MaxAttempts, BackoffBaseHours, BackoffMultiplier, IsActive, Remarks, CreatedAt)
SELECT 'CbsFetch','Fetch daily customer ids from the Core Banking System (CBS)', 1, 3, 24, 2.0, 1,
       'Retryable: the CBS source call can fail transiently; exponential backoff 24h, max 3 tries.', SYSUTCDATETIME()
WHERE NOT EXISTS (SELECT 1 FROM activity_type WHERE Code='CbsFetch');

INSERT INTO activity_type (Code, Name, IsRetryable, MaxAttempts, BackoffBaseHours, BackoffMultiplier, IsActive, Remarks, CreatedAt)
SELECT 'Crm','Enrich the record from the CRM', 1, 3, 24, 2.0, 1,
       'Retryable: CRM enrich + save can fail; exponential backoff 24h, max 3 tries.', SYSUTCDATETIME()
WHERE NOT EXISTS (SELECT 1 FROM activity_type WHERE Code='Crm');

INSERT INTO activity_type (Code, Name, IsRetryable, MaxAttempts, BackoffBaseHours, BackoffMultiplier, IsActive, Remarks, CreatedAt)
SELECT 'Store','Persist the individual details to the record tables', 1, 3, 24, 2.0, 1,
       'Retryable: the persistence step can fail; exponential backoff 24h, max 3 tries.', SYSUTCDATETIME()
WHERE NOT EXISTS (SELECT 1 FROM activity_type WHERE Code='Store');

INSERT INTO activity_type (Code, Name, IsRetryable, MaxAttempts, BackoffBaseHours, BackoffMultiplier, IsActive, Remarks, CreatedAt)
SELECT 'BuildZip','Generate the .UPL file + zip', 0, 3, 24, 2.0, 1,
       'Not retryable: deterministic generation; a failure needs manual intervention.', SYSUTCDATETIME()
WHERE NOT EXISTS (SELECT 1 FROM activity_type WHERE Code='BuildZip');

INSERT INTO activity_type (Code, Name, IsRetryable, MaxAttempts, BackoffBaseHours, BackoffMultiplier, IsActive, Remarks, CreatedAt)
SELECT 'FvuUpload','Submit the batch to the FVU', 0, 3, 24, 2.0, 1,
       'Not retryable automatically: a validation failure is surfaced to the operator.', SYSUTCDATETIME()
WHERE NOT EXISTS (SELECT 1 FROM activity_type WHERE Code='FvuUpload');

INSERT INTO activity_type (Code, Name, IsRetryable, MaxAttempts, BackoffBaseHours, BackoffMultiplier, IsActive, Remarks, CreatedAt)
SELECT 'Response','Read the CERSAI response file', 0, 3, 24, 2.0, 1,
       'Not retryable automatically: an unmatched/rejected reply needs manual intervention.', SYSUTCDATETIME()
WHERE NOT EXISTS (SELECT 1 FROM activity_type WHERE Code='Response');

INSERT INTO activity_type (Code, Name, IsRetryable, MaxAttempts, BackoffBaseHours, BackoffMultiplier, IsActive, Remarks, CreatedAt)
SELECT 'Reconciliation','Manual intervention / reconciliation review', 0, 3, 24, 2.0, 1,
       'Not retryable: human-in-the-loop step.', SYSUTCDATETIME()
WHERE NOT EXISTS (SELECT 1 FROM activity_type WHERE Code='Reconciliation');
GO

INSERT INTO status_master (StatusValue, Code, Name, Description, IsTerminal, IsActive, CreatedAt)
SELECT 0,'PND','Pending','Newly fetched daily customer; awaiting CRM enrichment.',0,1,SYSUTCDATETIME()
WHERE NOT EXISTS (SELECT 1 FROM status_master WHERE StatusValue=0);

INSERT INTO status_master (StatusValue, Code, Name, Description, IsTerminal, IsActive, CreatedAt)
SELECT 1,'CRM','CrmFetched','CRM data fetched successfully for this customer; awaiting save to record tables.',0,1,SYSUTCDATETIME()
WHERE NOT EXISTS (SELECT 1 FROM status_master WHERE StatusValue=1);

INSERT INTO status_master (StatusValue, Code, Name, Description, IsTerminal, IsActive, CreatedAt)
SELECT 2,'SAV','Saved','Individual details persisted to the record tables; awaiting batch generation.',0,1,SYSUTCDATETIME()
WHERE NOT EXISTS (SELECT 1 FROM status_master WHERE StatusValue=2);

INSERT INTO status_master (StatusValue, Code, Name, Description, IsTerminal, IsActive, CreatedAt)
SELECT 3,'BAT','Batched','Record enqueued into the generated .UPL batch file; awaiting upload.',0,1,SYSUTCDATETIME()
WHERE NOT EXISTS (SELECT 1 FROM status_master WHERE StatusValue=3);

INSERT INTO status_master (StatusValue, Code, Name, Description, IsTerminal, IsActive, CreatedAt)
SELECT 4,'FVP','FvuPassed','Batch submitted to the FVU and passed validation; ready for CERSAI upload.',1,1,SYSUTCDATETIME()
WHERE NOT EXISTS (SELECT 1 FROM status_master WHERE StatusValue=4);

INSERT INTO status_master (StatusValue, Code, Name, Description, IsTerminal, IsActive, CreatedAt)
SELECT 5,'FVF','FvuFailed','Batch submitted to the FVU and failed validation; needs operator attention.',0,1,SYSUTCDATETIME()
WHERE NOT EXISTS (SELECT 1 FROM status_master WHERE StatusValue=5);

INSERT INTO status_master (StatusValue, Code, Name, Description, IsTerminal, IsActive, CreatedAt)
SELECT 6,'FLD','Failed','Permanent failure (e.g. could not be saved after retries); needs manual intervention.',1,1,SYSUTCDATETIME()
WHERE NOT EXISTS (SELECT 1 FROM status_master WHERE StatusValue=6);

INSERT INTO status_master (StatusValue, Code, Name, Description, IsTerminal, IsActive, CreatedAt)
SELECT 7,'UPL','Uploaded','Batch uploaded/submitted to CERSAI; awaiting a response file.',0,1,SYSUTCDATETIME()
WHERE NOT EXISTS (SELECT 1 FROM status_master WHERE StatusValue=7);

INSERT INTO status_master (StatusValue, Code, Name, Description, IsTerminal, IsActive, CreatedAt)
SELECT 8,'RSP','ResponseRead','At least one CERSAI response file has been read for this record.',0,1,SYSUTCDATETIME()
WHERE NOT EXISTS (SELECT 1 FROM status_master WHERE StatusValue=8);

INSERT INTO status_master (StatusValue, Code, Name, Description, IsTerminal, IsActive, CreatedAt)
SELECT 9,'RCN','Reconciled','Record reconciled (matched/resolved against the CERSAI reply).',1,1,SYSUTCDATETIME()
WHERE NOT EXISTS (SELECT 1 FROM status_master WHERE StatusValue=9);

INSERT INTO status_master (StatusValue, Code, Name, Description, IsTerminal, IsActive, CreatedAt)
SELECT 10,'REJ','Rejected','Record permanently rejected by CERSAI.',1,1,SYSUTCDATETIME()
WHERE NOT EXISTS (SELECT 1 FROM status_master WHERE StatusValue=10);

-- v2: Data Fetch Failed. Available to operators/reports; the pipeline still
-- treats a failed CBS fetch as retryable Pending, so nothing renumbers.
INSERT INTO status_master (StatusValue, Code, Name, Description, IsTerminal, IsActive, CreatedAt)
SELECT 11,'DTF','DataFetchFailed','Daily customer-id fetch from the CBS failed; awaiting a retry or manual re-run.',0,1,SYSUTCDATETIME()
WHERE NOT EXISTS (SELECT 1 FROM status_master WHERE StatusValue=11);
GO
