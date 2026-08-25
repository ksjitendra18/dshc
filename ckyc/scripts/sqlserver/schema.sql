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
    CustomerId             NVARCHAR(50),
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
CREATE INDEX ix_master_customer   ON master_record (CustomerId);
CREATE INDEX ix_master_status     ON master_record (Status);
CREATE INDEX ix_master_batchline  ON master_record (BatchFile, BatchRecordLine);
GO

CREATE TABLE kyc_record_20 (
    Id                              BIGINT IDENTITY(1,1) PRIMARY KEY,
    MasterRecordId                  BIGINT,
    CustomerId                NVARCHAR(50),
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
GO

CREATE TABLE kyc_record_40 (
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
    CustomerId                  NVARCHAR(50),
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
    CustomerId                  NVARCHAR(50),
    Record20LineNumber          INT,
    RelatedPersonType           NVARCHAR(1),
    CkycNumberOfRelatedPerson   NVARCHAR(14)
);
GO

CREATE TABLE kyc_record_70 (
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
GO

CREATE INDEX ix_kyc_record20_customer ON kyc_record_20 (CustomerId);
CREATE INDEX ix_kyc_record30_customer ON kyc_record_30 (CustomerId);
CREATE INDEX ix_kyc_record40_customer ON kyc_record_40 (CustomerId);
CREATE INDEX ix_kyc_record50_customer ON kyc_record_50 (CustomerId);
CREATE INDEX ix_kyc_record60_customer ON kyc_record_60 (CustomerId);
CREATE INDEX ix_kyc_record70_customer ON kyc_record_70 (CustomerId);
GO

CREATE TABLE legal_entity_record_20 (
    Id                         BIGINT IDENTITY(1,1) PRIMARY KEY,
    MasterRecordId             BIGINT,
    CustomerId                 NVARCHAR(50),
    SearchKey                  NVARCHAR(20),
    EntityName                 NVARCHAR(99),
    EntityConstitution         NVARCHAR(2),
    ListedCompany              NVARCHAR(1),
    RegisteredFirm             NVARCHAR(1),
    RegisteredTrust            NVARCHAR(1),
    DateOfIncorporation        NVARCHAR(10),
    DateOfCommencement         NVARCHAR(10),
    PlaceOfIncorporation       NVARCHAR(60),
    CountryOfIncorporation     NVARCHAR(2),
    TinIssuingCountry          NVARCHAR(2),
    Pan                        NVARCHAR(10),
    Form97                     NVARCHAR(1),
    TinGstNumber               NVARCHAR(20),
    PanDocument                NVARCHAR(125),
    PanVerified                NVARCHAR(1),
    TinGstnDocument            NVARCHAR(125),
    CreatedAt                  DATETIME2,
    UpdatedAt                  DATETIME2
);
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
    UnincorporatedPowerOfAttorney     NVARCHAR(125),
    InfoEstablishExistence           NVARCHAR(125),
    OthersUnincorporated             NVARCHAR(125),
    SupportingDocumentsPoi           NVARCHAR(125),
    OtherTypeRegistrationNumber      NVARCHAR(50),
    OtherTypeRegistrationCertificate NVARCHAR(125),
    OtherTypePowerOfAttorney          NVARCHAR(125),
    ActivityProof1                    NVARCHAR(125),
    ActivityProof2                    NVARCHAR(125),
    OthersOtherType                   NVARCHAR(125)
);
GO

CREATE TABLE legal_entity_record_40 (
    Id                       BIGINT IDENTITY(1,1) PRIMARY KEY,
    MasterRecordId           BIGINT,
    CustomerId               NVARCHAR(50),
    Record20LineNumber       INT,
    RegLine1                 NVARCHAR(60), RegLine2 NVARCHAR(60), RegLine3 NVARCHAR(60),
    RegCity                  NVARCHAR(60), RegState NVARCHAR(2), RegDistrict NVARCHAR(2),
    RegPinCode               NVARCHAR(6), RegPinOthers NVARCHAR(6), RegDigipin NVARCHAR(10),
    RegCountry               NVARCHAR(2), RegProofOfAddress NVARCHAR(1),
    RegOtherDocumentName     NVARCHAR(50), RegDocument NVARCHAR(125),
    SameAsRegistered         NVARCHAR(1),
    PrinLine1                NVARCHAR(60), PrinLine2 NVARCHAR(60), PrinLine3 NVARCHAR(60),
    PrinCity                 NVARCHAR(60), PrinState NVARCHAR(2), PrinDistrict NVARCHAR(2),
    PrinPinCode              NVARCHAR(6), PrinPinOthers NVARCHAR(6), PrinDigipin NVARCHAR(10),
    PrinCountry              NVARCHAR(2), PrinProofOfAddress NVARCHAR(1),
    PrinOtherDocumentName    NVARCHAR(50), PrinDocument NVARCHAR(125)
);
GO

CREATE TABLE legal_entity_record_50 (
    Id                 BIGINT IDENTITY(1,1) PRIMARY KEY,
    MasterRecordId     BIGINT,
    CustomerId         NVARCHAR(50),
    Record20LineNumber INT,
    CountryCode1       NVARCHAR(6), MobileNumber1 NVARCHAR(15),
    CountryCode2       NVARCHAR(6), MobileNumber2 NVARCHAR(15),
    EmailId1           NVARCHAR(254), EmailId2 NVARCHAR(254),
    Telephone          NVARCHAR(12), Fax NVARCHAR(12)
);
GO

CREATE TABLE legal_entity_record_60 (
    Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
    MasterRecordId      BIGINT,
    CustomerId          NVARCHAR(50),
    Record20LineNumber  INT,
    Relation            NVARCHAR(60),
    CkycNumber          NVARCHAR(14),
    ControllingInterest NVARCHAR(50),
    PercentageOwnership NVARCHAR(10),
    OtherRelationName   NVARCHAR(33),
    Din                 NVARCHAR(8)
);
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
GO

CREATE INDEX ix_le_record20_customer ON legal_entity_record_20 (CustomerId);
CREATE INDEX ix_le_record30_customer ON legal_entity_record_30 (CustomerId);
CREATE INDEX ix_le_record40_customer ON legal_entity_record_40 (CustomerId);
CREATE INDEX ix_le_record50_customer ON legal_entity_record_50 (CustomerId);
CREATE INDEX ix_le_record60_customer ON legal_entity_record_60 (CustomerId);
CREATE INDEX ix_le_record70_customer ON legal_entity_record_70 (CustomerId);
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

CREATE TABLE master_record_batch (
    Id                 BIGINT IDENTITY(1,1) PRIMARY KEY,
    MasterRecordId     BIGINT,
    CustomerId         NVARCHAR(50),
    BatchFile          NVARCHAR(260),
    Record20LineNumber INT,
    BatchedAt          DATETIME2
);
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
GO

CREATE TABLE master_record_response (
    Id                    BIGINT IDENTITY(1,1) PRIMARY KEY,
    MasterRecordId        BIGINT,
    CustomerId      NVARCHAR(50),
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

CREATE TABLE master_record_attempt (
    Id             BIGINT IDENTITY(1,1) PRIMARY KEY,
    MasterRecordId BIGINT,
    CustomerId NVARCHAR(50),
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
    CustomerId                NVARCHAR(50),
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

-- CKYCR individual search request queue. ProcessingStatus:
-- 0 Pending, 1 Processing (claimed), 2 SRC generated, 3 Failed.
CREATE TABLE search_request (
    Id                       BIGINT IDENTITY(1,1) PRIMARY KEY,
    ExternalRequestId        NVARCHAR(50),
    CustomerId         NVARCHAR(50),
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
CREATE INDEX ix_search_request_claim ON search_request (ClaimToken);
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
CREATE INDEX ix_search_response_file_hash ON search_response_file (SourceHash);
GO

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
    SourceEntryPath          NVARCHAR(1000),
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
