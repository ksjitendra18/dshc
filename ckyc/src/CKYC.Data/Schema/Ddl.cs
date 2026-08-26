namespace CKYC.Data.Schema;

/// <summary>
/// DDL for the centralized CKYC store. Column definitions use ONLY a length
/// (VARCHAR(n)) plus the primary key — no NOT NULL, UNIQUE, CHECK or FK constraints —
/// exactly as requested ("length validation yes, other validation no").
///
/// The same logical schema for SQL Server is shipped in scripts/sqlserver/schema.sql.
/// </summary>
public static class Ddl
{
    public static readonly string[] CreateStatements =
    {
        // ---- Master table (step 1 : daily incoming customer ids) ----
        // Single source of truth for the record's current stage. `Status` holds the
        // current stage; `Is*`/`*At`/`LastResponse*`/`Recon*` columns let one row answer
        // "where is this record right now, when did each stage happen, what did CERSAI say".
        """
        CREATE TABLE IF NOT EXISTS master_record (
            Id                           INTEGER PRIMARY KEY AUTOINCREMENT,
            CustomerId             VARCHAR(50),
            ClientType                   VARCHAR(1),
            BusinessDate                 TEXT,
            Status                       INTEGER,
            Remarks                      VARCHAR(500),
            RetryCount                   INTEGER,
            LastError                    VARCHAR(1000),
            LastAttemptAt                TEXT,
            LastActivity                 VARCHAR(50),
            NextRetryAt                  TEXT,
            NeedsReconcile               INTEGER,
            ReattemptCount               INTEGER,
            ReattemptedAt                TEXT,
            BatchFile                    VARCHAR(260),
            BatchRecordLine              INTEGER,
            IsCrmFetched                 INTEGER,
            IsSaved                      INTEGER,
            IsBatched                    INTEGER,
            IsUploaded                   INTEGER,
            IsResponseRead               INTEGER,
            IsReconciled                 INTEGER,
            IsRejected                   INTEGER,
            CrmFetchedAt                 TEXT,
            SavedAt                      TEXT,
            BatchedAt                    TEXT,
            UploadedAt                   TEXT,
            FirstResponseAt              TEXT,
            ReconciledAt                 TEXT,
            LastResponseFileNumber       INTEGER,
            LastResponseFileName         VARCHAR(260),
            LastResponseAckNumber        VARCHAR(10),
            LastResponseStatus           VARCHAR(2),
            LastResponseCkycReference    VARCHAR(15),
            LastResponseCkycNumber       VARCHAR(15),
            LastResponseRejectionRemark  VARCHAR(500),
            LastResponseReadAt           TEXT,
            LastResponseRemarks          VARCHAR(1000),
            ReconStatus                  VARCHAR(50),
            ReconRemarks                 VARCHAR(1000),
            CreatedAt                    TEXT,
            UpdatedAt                    TEXT
        )
        """,

        // ---- Record 20 : Demographics ----
        """
        CREATE TABLE IF NOT EXISTS kyc_record_20 (
            Id                              INTEGER PRIMARY KEY AUTOINCREMENT,
            MasterRecordId                  INTEGER,
            CustomerId                VARCHAR(50),
            SearchKey                       VARCHAR(20),
            KycType                         VARCHAR(1),
            NameTitle                       VARCHAR(4),
            NameFirst                       VARCHAR(33),
            NameMiddle                      VARCHAR(33),
            NameLast                        VARCHAR(33),
            MaidenTitle                     VARCHAR(4),
            MaidenFirst                     VARCHAR(33),
            MaidenMiddle                    VARCHAR(33),
            MaidenLast                      VARCHAR(33),
            MotherTitle                     VARCHAR(4),
            MotherFirst                     VARCHAR(33),
            MotherMiddle                    VARCHAR(33),
            MotherLast                      VARCHAR(33),
            FatherTitle                     VARCHAR(4),
            FatherFirst                     VARCHAR(33),
            FatherMiddle                    VARCHAR(33),
            FatherLast                      VARCHAR(33),
            SpouseTitle                     VARCHAR(4),
            SpouseFirst                     VARCHAR(33),
            SpouseMiddle                    VARCHAR(33),
            SpouseLast                      VARCHAR(33),
            DateOfBirth                     VARCHAR(10),
            Gender                          VARCHAR(1),
            ResidentialStatus               VARCHAR(50),
            ResidentialSupportedByDocument  VARCHAR(1),
            Nationality                     VARCHAR(2),
            NationalitySupportedByDocument  VARCHAR(1),
            DifferentlyAbledStatus          VARCHAR(1),
            DifferentlyAbledType            VARCHAR(50),
            Pan                             VARCHAR(125),
            PanVerified                     VARCHAR(1),
            PhotoOfIndividual               VARCHAR(125),
            Minor                           VARCHAR(1),
            DoBMatchWithOvd                 VARCHAR(1),
            NameMatchWithOvd                VARCHAR(1),
            PhotoMatchWithOvd               VARCHAR(1),
            GenderProvidedInOvd             VARCHAR(1),
            GenderMatchWithOvd              VARCHAR(1),
            Form97Provided                  VARCHAR(1),
            Form61Provided                  VARCHAR(1),
            PanDocument                     VARCHAR(125),
            OtherTypeOfImpairment           VARCHAR(150),
            DisabilityReferenceNumber       VARCHAR(18),
            PermanentDisability             VARCHAR(1),
            DisabilityDate                  VARCHAR(10),
            PercentageOfImpairment          VARCHAR(3),
            DifferentlyAbledSupportedByDocument VARCHAR(1),
            CreatedAt                       TEXT,
            UpdatedAt                       TEXT
        )
        """,

        // ---- Record 30 : Proof of Identity & Address ----
        """
        CREATE TABLE IF NOT EXISTS kyc_record_30 (
            Id                           INTEGER PRIMARY KEY AUTOINCREMENT,
            MasterRecordId               INTEGER,
            CustomerId                   VARCHAR(50),
            Record20LineNumber           INTEGER,
            OvdType                      VARCHAR(1),
            ModeOfAadhaarVerification    VARCHAR(50),
            PassportExpiryDate           VARCHAR(10),
            DrivingLicenseExpiryDate     VARCHAR(10),
            LengthOfAadhaar              VARCHAR(1),
            IdNumber                     VARCHAR(100),
            CertifiedCopyWithOriginal    VARCHAR(1),
            EquivalentEDoc               VARCHAR(1),
            VerifiedFromDigiLocker       VARCHAR(1),
            PresenceInMeaRepository      VARCHAR(1),
            PresenceInEciRepository      VARCHAR(1),
            PresenceInRtoRepository      VARCHAR(1),
            PresenceInNregaRepository    VARCHAR(1),
            PresenceInNprRecords         VARCHAR(1),
            DataFromOfflineVerification  VARCHAR(1),
            ModeOfAuthentication         VARCHAR(1),
            EkycDataFromUidai            VARCHAR(1),
            CopyOfOvd                    VARCHAR(125)
        )
        """,

        // ---- Record 40 : Address (permanent + current) ----
        """
        CREATE TABLE IF NOT EXISTS kyc_record_40 (
            Id                         INTEGER PRIMARY KEY AUTOINCREMENT,
            MasterRecordId             INTEGER,
            CustomerId                 VARCHAR(50),
            Record20LineNumber         INTEGER,
            PermLine1                  VARCHAR(60),
            PermLine2                  VARCHAR(60),
            PermLine3                  VARCHAR(60),
            PermCountry                VARCHAR(2),
            PermState                  VARCHAR(2),
            PermDistrict               VARCHAR(6),
            PermCity                   VARCHAR(60),
            PermPinCode                VARCHAR(6),
            PermPinOthers              VARCHAR(6),
            PermDigipin                VARCHAR(10),
            PermSupportedDocument      VARCHAR(1),
            PermMatchOvd               VARCHAR(1),
            CurrLine1                  VARCHAR(60),
            CurrLine2                  VARCHAR(60),
            CurrLine3                  VARCHAR(60),
            CurrCountry                VARCHAR(2),
            CurrState                  VARCHAR(2),
            CurrDistrict               VARCHAR(6),
            CurrCity                   VARCHAR(60),
            CurrPinCode                VARCHAR(6),
            CurrPinOthers              VARCHAR(6),
            CurrDigipin                VARCHAR(10),
            CurrSupportedDocument      VARCHAR(1),
            CurrMatchOvd               VARCHAR(1)
        )
        """,

        // ---- Record 50 : Contact ----
        """
        CREATE TABLE IF NOT EXISTS kyc_record_50 (
            Id                          INTEGER PRIMARY KEY AUTOINCREMENT,
            MasterRecordId              INTEGER,
            CustomerId                  VARCHAR(50),
            Record20LineNumber          INTEGER,
            EmailAddress                VARCHAR(254),
            CountryCode                 VARCHAR(4),
            MobileNumber                VARCHAR(15),
            MobileValidatedViaOtp       VARCHAR(1),
            EmailValidatedViaOtp        VARCHAR(1),
            MobileValidatedViaThirdParty VARCHAR(1)
        )
        """,

        // ---- Record 60 : Related Party ----
        """
        CREATE TABLE IF NOT EXISTS kyc_record_60 (
            Id                          INTEGER PRIMARY KEY AUTOINCREMENT,
            MasterRecordId              INTEGER,
            CustomerId                  VARCHAR(50),
            Record20LineNumber          INTEGER,
            RelatedPersonType           VARCHAR(1),
            CkycNumberOfRelatedPerson   VARCHAR(14)
        )
        """,

        // ---- Record 70 : Other Details & Attestation ----
        """
        CREATE TABLE IF NOT EXISTS kyc_record_70 (
            Id                         INTEGER PRIMARY KEY AUTOINCREMENT,
            MasterRecordId             INTEGER,
            CustomerId                 VARCHAR(50),
            Record20LineNumber         INTEGER,
            Remarks                    VARCHAR(200),
            VideoKycWithoutOfficial    VARCHAR(1),
            VideoKycWithReOfficial     VARCHAR(1),
            FaceToFaceWithReOfficial   VARCHAR(1),
            NonFaceToFace              VARCHAR(1),
            FaceToFaceWithNonOfficial  VARCHAR(1),
            AttestationDate            VARCHAR(10),
            EmployeeName               VARCHAR(50),
            EmployeeCode               VARCHAR(50),
            EmployeeDesignation        VARCHAR(50),
            EmployeeBranch             VARCHAR(50),
            EmployeeCkycId             VARCHAR(50),
            InstitutionName            VARCHAR(50),
            InstitutionCode            VARCHAR(50),
            DeclarationDocument        VARCHAR(125),
            DeclarationFlag            VARCHAR(1),
            ClientConsent              VARCHAR(125),
            Place                      VARCHAR(40),
            DeclarationDate            VARCHAR(10)
        )
        """,

        // ---- Batch ledger + FVU run ledger ----
        """
        CREATE TABLE IF NOT EXISTS batch (
            Id             INTEGER PRIMARY KEY AUTOINCREMENT,
            BatchKey       VARCHAR(60),
            UploadFileName VARCHAR(260),
            UploadFilePath VARCHAR(1000),
            ZipPath        VARCHAR(1000),
            RecordCount    INTEGER,
            CreatedAt      TEXT
        )
        """,

        // ---- Append-only customer membership in generated upload batches ----
        """
        CREATE TABLE IF NOT EXISTS master_record_batch (
            Id                 INTEGER PRIMARY KEY AUTOINCREMENT,
            MasterRecordId     INTEGER,
            CustomerId         VARCHAR(50),
            BatchFile          VARCHAR(260),
            Record20LineNumber INTEGER,
            BatchedAt          TEXT
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS fvu_run (
            Id            INTEGER PRIMARY KEY AUTOINCREMENT,
            BatchKey      VARCHAR(60),
            Executed      INTEGER,
            ExitCode      INTEGER,
            Passed        INTEGER,
            SummaryJson   TEXT,
            OutputZipPath VARCHAR(1000),
            HashValue     VARCHAR(128),
            ErrorMessage  VARCHAR(2000),
            CreatedAt     TEXT
        )
        """,

        // ---- CERSAI reply history: one row per (record, response-file-number) ----
        """
        CREATE TABLE IF NOT EXISTS master_record_response (
            Id                     INTEGER PRIMARY KEY AUTOINCREMENT,
            MasterRecordId         INTEGER,
            CustomerId       VARCHAR(50),
            BatchFile              VARCHAR(260),
            ResponseFileNumber     INTEGER,
            ResponseFileName       VARCHAR(260),
            LineNumber             INTEGER,
            InputRecordLineNumber  INTEGER,
            AckNumber              VARCHAR(10),
            RecordStatus           VARCHAR(2),
            CkycReferenceNumber    VARCHAR(15),
            CkycNumber             VARCHAR(15),
            RejectionRemark        VARCHAR(500),
            ReadAt                 TEXT,
            Remarks                VARCHAR(1000),
            RawData                VARCHAR(4000),
            CreatedAt              TEXT
        )
        """,

        """
        CREATE TABLE IF NOT EXISTS upload_response_file (
            Id                    INTEGER PRIMARY KEY AUTOINCREMENT,
            BatchFile             VARCHAR(260),
            ResponseFileName      VARCHAR(260),
            ResponseFileNumber    INTEGER,
            TotalRecords          INTEGER,
            TotalProcessed        INTEGER,
            UnderProcessing       INTEGER,
            Failed                INTEGER,
            ResponseTimestamp     VARCHAR(30),
            RawHeaderData         TEXT,
            SourceArchiveName     VARCHAR(260),
            SourceHash            VARCHAR(128),
            CreatedAt             TEXT
        )
        """,

        // ---- Stage attempt / retry audit trail ----
        """
        CREATE TABLE IF NOT EXISTS master_record_attempt (
            Id                INTEGER PRIMARY KEY AUTOINCREMENT,
            MasterRecordId    INTEGER,
            CustomerId  VARCHAR(50),
            Stage             VARCHAR(50),
            ActivityTypeId    INTEGER,
            Attempt           INTEGER,
            Status            INTEGER,
            Success           INTEGER,
            Error             VARCHAR(1000),
            Remarks           VARCHAR(1000),
            AttemptedAt       TEXT,
            NextRetryAt       TEXT,
            CreatedAt         TEXT
        )
        """,

        // ---- Activity type master: which processes are retryable + their retry policy ----
        """
        CREATE TABLE IF NOT EXISTS activity_type (
            Id                 INTEGER PRIMARY KEY AUTOINCREMENT,
            Code               VARCHAR(50),
            Name               VARCHAR(100),
            IsRetryable        INTEGER,
            MaxAttempts        INTEGER,
            BackoffBaseHours   INTEGER,
            BackoffMultiplier  REAL,
            IsActive           INTEGER,
            Remarks            VARCHAR(500),
            CreatedAt          TEXT
        )
        """,

        // ---- Status master: compacts the master_record.Status integer (0-10) to a 2-3 char
        //      code + readable description. Status stays INTEGER; this table only maps it. ----
        """
        CREATE TABLE IF NOT EXISTS status_master (
            Id             INTEGER PRIMARY KEY AUTOINCREMENT,
            StatusValue    INTEGER,
            Code           VARCHAR(3),
            Name           VARCHAR(50),
            Description    VARCHAR(500),
            IsTerminal     INTEGER,
            IsActive       INTEGER,
            CreatedAt      TEXT
        )
        """,

        // ---- Re-push (reattempt) history: one row per manual re-push of a rejected record ----
        """
        CREATE TABLE IF NOT EXISTS master_record_reattempt (
            Id                              INTEGER PRIMARY KEY AUTOINCREMENT,
            MasterRecordId                  INTEGER,
            CustomerId                VARCHAR(50),
            Reason                          VARCHAR(1000),
            PreviousStatus                  INTEGER,
            PreviousReconStatus             VARCHAR(50),
            PreviousResponseStatus          VARCHAR(2),
            PreviousResponseAckNumber       VARCHAR(10),
            PreviousResponseCkycReference   VARCHAR(15),
            PreviousResponseCkycNumber      VARCHAR(15),
            PreviousResponseRejectionRemark VARCHAR(500),
            PreviousResponseReadAt          TEXT,
            PreviousRetryCount              INTEGER,
            ReattemptCount                  INTEGER,
            ReattemptedAt                   TEXT,
            CreatedAt                       TEXT
        )
        """,
        // ---- CKYCR individual search: JSON intake, atomic processing and response ----
        """
        CREATE TABLE IF NOT EXISTS search_request (
            Id                       INTEGER PRIMARY KEY AUTOINCREMENT,
            ExternalRequestId        VARCHAR(50),
            CustomerId         VARCHAR(50),
            ClientType               VARCHAR(1),
            SearchOption             INTEGER,
            IdentityTypeAndNumber    VARCHAR(2000),
            FirstName                VARCHAR(33),
            MiddleName               VARCHAR(33),
            LastName                 VARCHAR(33),
            DateOfBirth              VARCHAR(10),
            LegalEntityName          VARCHAR(99),
            DateOfIncorporation      VARCHAR(10),
            Gender                   VARCHAR(1),
            PhotoReferenceNumber     VARCHAR(40),
            Relation                 VARCHAR(50),
            RelationFirstName        VARCHAR(33),
            RelationMiddleName       VARCHAR(33),
            RelationLastName         VARCHAR(33),
            MobileNumber             VARCHAR(10),
            VerifiableCredential     VARCHAR(50),
            Constitution             VARCHAR(1),
            RawRequestJson           TEXT,
            ProcessingStatus         INTEGER,
            ClaimToken               VARCHAR(36),
            ClaimedAt                TEXT,
            ProcessedAt              TEXT,
            OutputFileName           VARCHAR(260),
            OutputLineNumber         INTEGER,
            ResponseStatus           VARCHAR(50),
            LastSearchKey            VARCHAR(20),
            LastCkycReference        VARCHAR(15),
            LastResponseRemark       VARCHAR(250),
            ResponseReadAt           TEXT,
            LastError                VARCHAR(2000),
            CreatedAt                TEXT,
            UpdatedAt                TEXT
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS search_batch (
            Id             INTEGER PRIMARY KEY AUTOINCREMENT,
            BusinessDate   TEXT,
            FileSequence   INTEGER,
            ClaimToken     VARCHAR(36),
            RecordCount    INTEGER,
            Status         INTEGER,
            FileName       VARCHAR(260),
            FilePath       VARCHAR(1000),
            FvuZipPath     VARCHAR(1000),
            FvuHash        VARCHAR(128),
            Error          VARCHAR(2000),
            CreatedAt      TEXT,
            CompletedAt    TEXT
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS search_response (
            Id                         INTEGER PRIMARY KEY AUTOINCREMENT,
            SearchRequestId            INTEGER,
            ResponseFileName           VARCHAR(260),
            ResponseFileNumber         INTEGER,
            LineNumber                 INTEGER,
            InputRecordLineNumber      INTEGER,
            ClientType                 VARCHAR(1),
            SearchByOvdType            VARCHAR(1),
            SearchByOvdNumber          VARCHAR(15),
            SearchKey                  VARCHAR(20),
            CkycReferenceNumber        VARCHAR(15),
            FirstName                  VARCHAR(33),
            MiddleName                 VARCHAR(33),
            LastName                   VARCHAR(33),
            Gender                     VARCHAR(1),
            MobileNumber               VARCHAR(12),
            EmailAddress               VARCHAR(99),
            LastUpdatedDate            VARCHAR(10),
            Cin                        VARCHAR(40),
            LegalEntityName            VARCHAR(150),
            PhotoReference             VARCHAR(20),
            RegistrationDate           VARCHAR(12),
            DeactivationReason         VARCHAR(100),
            Remark                     VARCHAR(250),
            PanDocument                VARCHAR(1),
            AadhaarDocument            VARCHAR(1),
            PassportDocument           VARCHAR(1),
            DrivingLicenseDocument     VARCHAR(1),
            VoterIdDocument            VARCHAR(1),
            NregaDocument              VARCHAR(1),
            DisabilityDocument         VARCHAR(1),
            Form6061Document            VARCHAR(1),
            ForeignJurisdictionDocument VARCHAR(1),
            NprDocument                VARCHAR(1),
            UtilityBillDocument        VARCHAR(1),
            IncorporationDocument      VARCHAR(1),
            MemorandumDocument         VARCHAR(1),
            RegistrationCertificate    VARCHAR(1),
            PartnershipDeed            VARCHAR(1),
            TrustDeed                  VARCHAR(1),
            SupportingPoiDocument      VARCHAR(1),
            OtherDocument              VARCHAR(1),
            Filler1                    VARCHAR(1),
            Filler2                    VARCHAR(1),
            Filler3                    VARCHAR(1),
            Filler4                    VARCHAR(1),
            Filler5                    VARCHAR(1),
            Filler6                    VARCHAR(1),
            Filler7                    VARCHAR(1),
            Filler8                    VARCHAR(1),
            RecordLevelHash            VARCHAR(128),
            RawResponseData            TEXT,
            CreatedAt                  TEXT
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS search_response_file (
            Id                     INTEGER PRIMARY KEY AUTOINCREMENT,
            SearchBatchId          INTEGER,
            ResponseFileName       VARCHAR(260),
            ResponseFileNumber     INTEGER,
            FiCode                 VARCHAR(6),
            RegionCode             VARCHAR(11),
            TotalRecords           INTEGER,
            TotalProcessed         INTEGER,
            RecordsUnderProcessing INTEGER,
            RecordsFailed          INTEGER,
            ResponseTimestamp      VARCHAR(20),
            Filler                 VARCHAR(50),
            RawHeaderData          TEXT,
            SourceArchiveName      VARCHAR(260),
            SourceHash             VARCHAR(128),
            CreatedAt              TEXT
        )
        """,

        // ---- CKYCR download response: immutable file, record lines and ZIP artifacts ----
        """
        CREATE TABLE IF NOT EXISTS download_response_file (
            Id                 INTEGER PRIMARY KEY AUTOINCREMENT,
            ResponseFileName   VARCHAR(260),
            ResponseFileNumber INTEGER,
            FiCode             VARCHAR(6),
            RegionCode         VARCHAR(11),
            ClientType         VARCHAR(1),
            TotalRecords       INTEGER,
            Version            VARCHAR(20),
            ResponseDate       VARCHAR(30),
            RawHeaderData      TEXT,
            SourceArchiveName  VARCHAR(260),
            SourceHash         VARCHAR(128),
            CreatedAt          TEXT
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS download_response_line (
            Id                      INTEGER PRIMARY KEY AUTOINCREMENT,
            DownloadResponseFileId  INTEGER,
            SourceEntryPath          VARCHAR(1000),
            RecordType              VARCHAR(2),
            LineNumber              INTEGER,
            InputRecord20LineNumber INTEGER,
            CkycNumber              VARCHAR(15),
            RawData                 TEXT,
            CreatedAt               TEXT
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS download_response_artifact (
            Id                     INTEGER PRIMARY KEY AUTOINCREMENT,
            DownloadResponseFileId INTEGER,
            EntryPath              VARCHAR(1000),
            FileName               VARCHAR(260),
            Size                   INTEGER,
            Sha256                 VARCHAR(128),
            CreatedAt              TEXT
        )
        """,
        // ---- LEGAL ENTITY record tables (client type L). Deliberately separate from the
        //      individual kyc_record_* tables — a legal entity never shares a row with a
        //      retail customer. Same schema philosophy (length only, no NOT NULL/CHECK/FK). ----
        """
        CREATE TABLE IF NOT EXISTS legal_entity_record_20 (
            Id                              INTEGER PRIMARY KEY AUTOINCREMENT,
            MasterRecordId                  INTEGER,
            CustomerId                VARCHAR(50),
            SearchKey                       VARCHAR(20),
            EntityName                      VARCHAR(99),
            EntityConstitution              VARCHAR(2),
            ListedCompany                   VARCHAR(1),
            RegisteredFirm                  VARCHAR(1),
            RegisteredTrust                 VARCHAR(1),
            DateOfIncorporation             VARCHAR(10),
            DateOfCommencement              VARCHAR(10),
            PlaceOfIncorporation            VARCHAR(50),
            CountryOfIncorporation          VARCHAR(2),
            TinIssuingCountry               VARCHAR(2),
            Pan                             VARCHAR(10),
            Form97                          VARCHAR(1),
            TinGstNumber                    VARCHAR(15),
            PanDocument                     VARCHAR(125),
            PanVerified                     VARCHAR(1),
            TinGstnDocument                 VARCHAR(125),
            CreatedAt                       TEXT,
            UpdatedAt                       TEXT
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS legal_entity_record_30 (
            Id                           INTEGER PRIMARY KEY AUTOINCREMENT,
            MasterRecordId               INTEGER,
            CustomerId                   VARCHAR(50),
            Record20LineNumber           INTEGER,
            CertificateOfIncorporation   VARCHAR(125),
            Cin                          VARCHAR(21),
            MemorandumAndArticles        VARCHAR(125),
            ResolutionBoardPoA           VARCHAR(125),
            NamesSeniorManagement        VARCHAR(125),
            CertificateOfCommencement    VARCHAR(125),
            OthersCompany                VARCHAR(125),
            RegistrationCertificate      VARCHAR(125),
            RegistrationNumber           VARCHAR(50),
            LlpinCertificate             VARCHAR(125),
            Llpin                        VARCHAR(7),
            PartnershipDeed              VARCHAR(125),
            NamesAllPartners             VARCHAR(125),
            OthersPartnership            VARCHAR(125),
            TrustRegistrationCertificate VARCHAR(125),
            TrustRegistrationNumber      VARCHAR(50),
            TrustDeed                    VARCHAR(125),
            NamesBeneficiariesTrustees   VARCHAR(125),
            TrustPowerOfAttorney         VARCHAR(125),
            OthersTrust                  VARCHAR(125),
            UnincorporatedRegCertificate VARCHAR(125),
            UnincorporatedRegNumber      VARCHAR(50),
            ResolutionManagingBody       VARCHAR(125),
            UnincorporatedPowerOfAttorney VARCHAR(125),
            InfoEstablishExistence       VARCHAR(125),
            OthersUnincorporated         VARCHAR(125),
            SupportingDocumentsPoi       VARCHAR(125),
            OtherTypeRegistrationNumber  VARCHAR(50),
            OtherTypeRegistrationCertificate VARCHAR(125),
            OtherTypePowerOfAttorney     VARCHAR(125),
            ActivityProof1               VARCHAR(125),
            ActivityProof2               VARCHAR(125),
            OthersOtherType              VARCHAR(125)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS legal_entity_record_40 (
            Id                        INTEGER PRIMARY KEY AUTOINCREMENT,
            MasterRecordId            INTEGER,
            CustomerId                VARCHAR(50),
            Record20LineNumber        INTEGER,
            RegLine1                  VARCHAR(60),
            RegLine2                  VARCHAR(60),
            RegLine3                  VARCHAR(60),
            RegCity                   VARCHAR(60),
            RegState                  VARCHAR(2),
            RegDistrict               VARCHAR(4),
            RegPinCode                VARCHAR(6),
            RegPinOthers              VARCHAR(6),
            RegDigipin                VARCHAR(10),
            RegCountry                VARCHAR(2),
            RegProofOfAddress         VARCHAR(1),
            RegOtherDocumentName      VARCHAR(50),
            RegDocument               VARCHAR(125),
            SameAsRegistered          VARCHAR(1),
            PrinLine1                 VARCHAR(60),
            PrinLine2                 VARCHAR(60),
            PrinLine3                 VARCHAR(60),
            PrinCity                  VARCHAR(60),
            PrinState                 VARCHAR(2),
            PrinDistrict              VARCHAR(4),
            PrinPinCode               VARCHAR(6),
            PrinPinOthers             VARCHAR(6),
            PrinDigipin               VARCHAR(10),
            PrinCountry               VARCHAR(2),
            PrinProofOfAddress        VARCHAR(1),
            PrinOtherDocumentName     VARCHAR(50),
            PrinDocument              VARCHAR(125)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS legal_entity_record_50 (
            Id                          INTEGER PRIMARY KEY AUTOINCREMENT,
            MasterRecordId              INTEGER,
            CustomerId                  VARCHAR(50),
            Record20LineNumber          INTEGER,
            CountryCode1                VARCHAR(6),
            MobileNumber1               VARCHAR(15),
            CountryCode2                VARCHAR(6),
            MobileNumber2               VARCHAR(15),
            EmailId1                    VARCHAR(254),
            EmailId2                    VARCHAR(254),
            Telephone                  VARCHAR(12),
            Fax                         VARCHAR(12)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS legal_entity_record_60 (
            Id                         INTEGER PRIMARY KEY AUTOINCREMENT,
            MasterRecordId             INTEGER,
            CustomerId                 VARCHAR(50),
            Record20LineNumber         INTEGER,
            NumberOfRelatedPersons     INTEGER,
            NumberOfBeneficialOwners   INTEGER,
            Relation                   VARCHAR(60),
            CkycNumber                 VARCHAR(14),
            ControllingInterest        VARCHAR(50),
            PercentageOwnership        VARCHAR(10),
            OtherRelationName          VARCHAR(33),
            Din                        VARCHAR(8)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS legal_entity_record_70 (
            Id                         INTEGER PRIMARY KEY AUTOINCREMENT,
            MasterRecordId             INTEGER,
            CustomerId                 VARCHAR(50),
            Record20LineNumber         INTEGER,
            Remarks                    VARCHAR(200),
            CertifiedCopies            VARCHAR(1),
            EquivalentEDoc             VARCHAR(1),
            VerificationFromDigiLocker VARCHAR(1),
            AttestationDate            VARCHAR(10),
            EmployeeName               VARCHAR(99),
            EmployeeCode               VARCHAR(50),
            EmployeeDesignation        VARCHAR(50),
            EmployeeBranch             VARCHAR(50),
            EmployeeCkycId             VARCHAR(14),
            InstitutionName            VARCHAR(99),
            InstitutionCode            VARCHAR(50),
            DeclarationDocument        VARCHAR(125),
            DeclarationFlag            VARCHAR(1),
            ConsentDocument            VARCHAR(125),
            Place                      VARCHAR(40),
            DeclarationDate            VARCHAR(10)
        )
        """,
        "CREATE INDEX IF NOT EXISTS ix_le_record20_master ON legal_entity_record_20(MasterRecordId)",
        "CREATE INDEX IF NOT EXISTS ix_le_record30_master ON legal_entity_record_30(MasterRecordId)",
        "CREATE INDEX IF NOT EXISTS ix_le_record40_master ON legal_entity_record_40(MasterRecordId)",
        "CREATE INDEX IF NOT EXISTS ix_le_record50_master ON legal_entity_record_50(MasterRecordId)",
        "CREATE INDEX IF NOT EXISTS ix_le_record60_master ON legal_entity_record_60(MasterRecordId)",
        "CREATE INDEX IF NOT EXISTS ix_le_record70_master ON legal_entity_record_70(MasterRecordId)",
        "CREATE INDEX IF NOT EXISTS ix_master_status ON master_record(Status)",
        "CREATE INDEX IF NOT EXISTS ix_master_response_master ON master_record_response(MasterRecordId)",
        "CREATE INDEX IF NOT EXISTS ix_upload_response_file_identity ON upload_response_file(SourceHash, ResponseFileName)",
        "CREATE INDEX IF NOT EXISTS ix_master_attempt_master ON master_record_attempt(MasterRecordId)",
        "CREATE INDEX IF NOT EXISTS ix_master_record_batch_master ON master_record_batch(MasterRecordId)",
        "CREATE INDEX IF NOT EXISTS ix_master_reattempt_master ON master_record_reattempt(MasterRecordId)",
        "CREATE INDEX IF NOT EXISTS ix_activity_code ON activity_type(Code)",
        "CREATE INDEX IF NOT EXISTS ix_status_value ON status_master(StatusValue)",
        "CREATE INDEX IF NOT EXISTS ix_status_code ON status_master(Code)",
        "CREATE INDEX IF NOT EXISTS ix_search_request_status ON search_request(ProcessingStatus, Id)",
        "CREATE INDEX IF NOT EXISTS ix_search_request_claim ON search_request(ClaimToken)",
        "CREATE INDEX IF NOT EXISTS ix_search_batch_date ON search_batch(BusinessDate, FileSequence)",
        "CREATE INDEX IF NOT EXISTS ix_search_response_request ON search_response(SearchRequestId)",
        "CREATE INDEX IF NOT EXISTS ix_search_response_file_batch ON search_response_file(SearchBatchId)",
        "CREATE INDEX IF NOT EXISTS ix_download_response_file_hash ON download_response_file(SourceHash, ResponseFileName)",
        "CREATE INDEX IF NOT EXISTS ix_download_response_line_file ON download_response_line(DownloadResponseFileId)",
        "CREATE INDEX IF NOT EXISTS ix_download_response_artifact_file ON download_response_artifact(DownloadResponseFileId)",
    };

    /// <summary>
    /// Indexes that depend on columns a pre-change database may not have yet (e.g.
    /// <c>master_record.BatchRecordLine</c>). These are created ONLY after the additive
    /// column migrations have run, so an old database upgrades without the create-index
    /// failing on the missing column.
    /// </summary>
    public static readonly string[] PostMigrationStatements =
    {
        "CREATE INDEX IF NOT EXISTS ix_master_batchline ON master_record(BatchFile, BatchRecordLine)",
        "CREATE INDEX IF NOT EXISTS ix_master_customer_id ON master_record(CustomerId)",
        "CREATE INDEX IF NOT EXISTS ix_master_record_batch_customer ON master_record_batch(CustomerId, BatchedAt)",
        "CREATE INDEX IF NOT EXISTS ix_master_record_batch_fileline ON master_record_batch(BatchFile, Record20LineNumber)",
        "CREATE INDEX IF NOT EXISTS ix_search_response_file_hash ON search_response_file(SourceHash)",
        "CREATE INDEX IF NOT EXISTS ix_kyc_record20_customer ON kyc_record_20(CustomerId)",
        "CREATE INDEX IF NOT EXISTS ix_kyc_record30_customer ON kyc_record_30(CustomerId)",
        "CREATE INDEX IF NOT EXISTS ix_kyc_record40_customer ON kyc_record_40(CustomerId)",
        "CREATE INDEX IF NOT EXISTS ix_kyc_record50_customer ON kyc_record_50(CustomerId)",
        "CREATE INDEX IF NOT EXISTS ix_kyc_record60_customer ON kyc_record_60(CustomerId)",
        "CREATE INDEX IF NOT EXISTS ix_kyc_record70_customer ON kyc_record_70(CustomerId)",
        "CREATE INDEX IF NOT EXISTS ix_le_record20_customer_id ON legal_entity_record_20(CustomerId)",
        "CREATE INDEX IF NOT EXISTS ix_le_record30_customer ON legal_entity_record_30(CustomerId)",
        "CREATE INDEX IF NOT EXISTS ix_le_record40_customer ON legal_entity_record_40(CustomerId)",
        "CREATE INDEX IF NOT EXISTS ix_le_record50_customer ON legal_entity_record_50(CustomerId)",
        "CREATE INDEX IF NOT EXISTS ix_le_record60_customer ON legal_entity_record_60(CustomerId)",
        "CREATE INDEX IF NOT EXISTS ix_le_record70_customer ON legal_entity_record_70(CustomerId)",

        // ---- Indexes for hot query shapes that previously scanned. Non-unique (the schema
        //      deliberately avoids UNIQUE/CHECK/FK constraints), so existing data is never
        //      rejected — these only speed reads. Child-table MasterRecordId indexes serve the
        //      per-record load/delete path in the record repositories. ----
        "CREATE INDEX IF NOT EXISTS ix_kyc_record20_master ON kyc_record_20(MasterRecordId)",
        "CREATE INDEX IF NOT EXISTS ix_kyc_record30_master ON kyc_record_30(MasterRecordId)",
        "CREATE INDEX IF NOT EXISTS ix_kyc_record40_master ON kyc_record_40(MasterRecordId)",
        "CREATE INDEX IF NOT EXISTS ix_kyc_record50_master ON kyc_record_50(MasterRecordId)",
        "CREATE INDEX IF NOT EXISTS ix_kyc_record60_master ON kyc_record_60(MasterRecordId)",
        "CREATE INDEX IF NOT EXISTS ix_kyc_record70_master ON kyc_record_70(MasterRecordId)",
        "CREATE INDEX IF NOT EXISTS ix_master_status_id ON master_record(Status, Id)",
        "CREATE INDEX IF NOT EXISTS ix_master_retry_picker ON master_record(Status, RetryCount, LastActivity, NextRetryAt)",
        "CREATE INDEX IF NOT EXISTS ix_batch_key ON batch(BatchKey)",
        "CREATE INDEX IF NOT EXISTS ix_fvu_run_key ON fvu_run(BatchKey)",
        """
        INSERT INTO master_record_batch (MasterRecordId, CustomerId, BatchFile, Record20LineNumber, BatchedAt)
        SELECT m.Id, m.CustomerId, m.BatchFile, m.BatchRecordLine, COALESCE(m.BatchedAt,m.UpdatedAt,m.CreatedAt)
          FROM master_record m
         WHERE m.BatchFile IS NOT NULL
           AND NOT EXISTS (
               SELECT 1 FROM master_record_batch b
                WHERE b.MasterRecordId=m.Id AND b.BatchFile=m.BatchFile
           )
        """,
    };

    /// <summary>Tables whose legacy <c>SourceCustomerId</c> column is renamed during upgrade.</summary>
    public static readonly string[] CustomerIdTables =
    {
        "master_record",
        "kyc_record_20", "kyc_record_30", "kyc_record_40", "kyc_record_50", "kyc_record_60", "kyc_record_70",
        "legal_entity_record_20", "legal_entity_record_30", "legal_entity_record_40", "legal_entity_record_50",
        "legal_entity_record_60", "legal_entity_record_70",
        "master_record_response", "master_record_attempt", "master_record_reattempt", "search_request",
    };

    /// <summary>Customer-scoped tables that can derive the identifier from master_record.</summary>
    public static readonly string[] MasterLinkedCustomerIdTables =
    {
        "kyc_record_20", "kyc_record_30", "kyc_record_40", "kyc_record_50", "kyc_record_60", "kyc_record_70",
        "legal_entity_record_20", "legal_entity_record_30", "legal_entity_record_40", "legal_entity_record_50",
        "legal_entity_record_60", "legal_entity_record_70",
        "master_record_response", "master_record_attempt", "master_record_reattempt",
    };

    /// <summary>
    /// Idempotent seed rows for the <c>activity_type</c> master. Each pipeline activity is
    /// inserted only if its <c>Code</c> is not already present. The retry policy uses the
    /// requested defaults: exponential backoff starting at 24 hours, doubling per failure,
    /// with at most 3 attempts. Only <b>some</b> activities are marked retryable — the CBS
    /// customer-id fetch (the example given) and the CRM enrich/save step that the existing
    /// <c>retry</c> command re-runs. The rest (batch generation, reconciliation) are
    /// human-exception-driven and are flagged for manual intervention when they fail.
    /// </summary>
    public static readonly string[] SeedStatements =
    {
        """
        INSERT INTO activity_type (Code, Name, IsRetryable, MaxAttempts, BackoffBaseHours, BackoffMultiplier, IsActive, Remarks, CreatedAt)
        SELECT 'CbsFetch','Fetch daily customer ids from the Core Banking System (CBS)', 1, 3, 24, 2.0, 1,
               'Retryable: the CBS source call can fail transiently; exponential backoff 24h, max 3 tries.', strftime('%Y-%m-%dT%H:%M:%SZ','now')
        WHERE NOT EXISTS (SELECT 1 FROM activity_type WHERE Code='CbsFetch')
        """,
        """
        INSERT INTO activity_type (Code, Name, IsRetryable, MaxAttempts, BackoffBaseHours, BackoffMultiplier, IsActive, Remarks, CreatedAt)
        SELECT 'Crm','Enrich the record from the CRM', 1, 3, 24, 2.0, 1,
               'Retryable: CRM enrich + save can fail; exponential backoff 24h, max 3 tries.', strftime('%Y-%m-%dT%H:%M:%SZ','now')
        WHERE NOT EXISTS (SELECT 1 FROM activity_type WHERE Code='Crm')
        """,
        """
        INSERT INTO activity_type (Code, Name, IsRetryable, MaxAttempts, BackoffBaseHours, BackoffMultiplier, IsActive, Remarks, CreatedAt)
        SELECT 'Store','Persist the individual details to the record tables', 1, 3, 24, 2.0, 1,
               'Retryable: the persistence step can fail; exponential backoff 24h, max 3 tries.', strftime('%Y-%m-%dT%H:%M:%SZ','now')
        WHERE NOT EXISTS (SELECT 1 FROM activity_type WHERE Code='Store')
        """,
        """
        INSERT INTO activity_type (Code, Name, IsRetryable, MaxAttempts, BackoffBaseHours, BackoffMultiplier, IsActive, Remarks, CreatedAt)
        SELECT 'BuildZip','Generate the .UPL file + zip', 0, 3, 24, 2.0, 1,
               'Not retryable: deterministic generation; a failure needs manual intervention.', strftime('%Y-%m-%dT%H:%M:%SZ','now')
        WHERE NOT EXISTS (SELECT 1 FROM activity_type WHERE Code='BuildZip')
        """,
        """
        INSERT INTO activity_type (Code, Name, IsRetryable, MaxAttempts, BackoffBaseHours, BackoffMultiplier, IsActive, Remarks, CreatedAt)
        SELECT 'FvuUpload','Submit the batch to the FVU', 0, 3, 24, 2.0, 1,
               'Not retryable automatically: a validation failure is surfaced to the operator.', strftime('%Y-%m-%dT%H:%M:%SZ','now')
        WHERE NOT EXISTS (SELECT 1 FROM activity_type WHERE Code='FvuUpload')
        """,
        """
        INSERT INTO activity_type (Code, Name, IsRetryable, MaxAttempts, BackoffBaseHours, BackoffMultiplier, IsActive, Remarks, CreatedAt)
        SELECT 'Response','Read the CERSAI response file', 0, 3, 24, 2.0, 1,
               'Not retryable automatically: an unmatched/rejected reply needs manual intervention.', strftime('%Y-%m-%dT%H:%M:%SZ','now')
        WHERE NOT EXISTS (SELECT 1 FROM activity_type WHERE Code='Response')
        """,
        """
        INSERT INTO activity_type (Code, Name, IsRetryable, MaxAttempts, BackoffBaseHours, BackoffMultiplier, IsActive, Remarks, CreatedAt)
        SELECT 'Reconciliation','Manual intervention / reconciliation review', 0, 3, 24, 2.0, 1,
               'Not retryable: human-in-the-loop step.', strftime('%Y-%m-%dT%H:%M:%SZ','now')
        WHERE NOT EXISTS (SELECT 1 FROM activity_type WHERE Code='Reconciliation')
        """,
    };

    /// <summary>
    /// Idempotent seed rows for the <c>status_master</c> lookup. Each <c>StatusValue</c>
    /// (the integer persisted in <c>master_record.Status</c>) maps to a short 2–3 char
    /// <c>Code</c>, the enum <c>Name</c>, and a human <c>Description</c>. <c>IsTerminal</c>
    /// mirrors <c>MasterRecordStatusExtensions.IsTerminal</c>. Inserted only when the
    /// <c>StatusValue</c> is not already present (append-only — never renumber).
    /// </summary>
    public static readonly string[] StatusMasterSeedStatements =
    {
        """
        INSERT INTO status_master (StatusValue, Code, Name, Description, IsTerminal, IsActive, CreatedAt)
        SELECT 0,'PND','Pending','Newly fetched daily customer; awaiting CRM enrichment.',0,1,strftime('%Y-%m-%dT%H:%M:%SZ','now')
        WHERE NOT EXISTS (SELECT 1 FROM status_master WHERE StatusValue=0)
        """,
        """
        INSERT INTO status_master (StatusValue, Code, Name, Description, IsTerminal, IsActive, CreatedAt)
        SELECT 1,'CRM','CrmFetched','CRM data fetched successfully for this customer; awaiting save to record tables.',0,1,strftime('%Y-%m-%dT%H:%M:%SZ','now')
        WHERE NOT EXISTS (SELECT 1 FROM status_master WHERE StatusValue=1)
        """,
        """
        INSERT INTO status_master (StatusValue, Code, Name, Description, IsTerminal, IsActive, CreatedAt)
        SELECT 2,'SAV','Saved','Individual details persisted to the record tables; awaiting batch generation.',0,1,strftime('%Y-%m-%dT%H:%M:%SZ','now')
        WHERE NOT EXISTS (SELECT 1 FROM status_master WHERE StatusValue=2)
        """,
        """
        INSERT INTO status_master (StatusValue, Code, Name, Description, IsTerminal, IsActive, CreatedAt)
        SELECT 3,'BAT','Batched','Record enqueued into the generated .UPL batch file; awaiting upload.',0,1,strftime('%Y-%m-%dT%H:%M:%SZ','now')
        WHERE NOT EXISTS (SELECT 1 FROM status_master WHERE StatusValue=3)
        """,
        """
        INSERT INTO status_master (StatusValue, Code, Name, Description, IsTerminal, IsActive, CreatedAt)
        SELECT 4,'FVP','FvuPassed','Batch submitted to the FVU and passed validation; ready for CERSAI upload.',1,1,strftime('%Y-%m-%dT%H:%M:%SZ','now')
        WHERE NOT EXISTS (SELECT 1 FROM status_master WHERE StatusValue=4)
        """,
        """
        INSERT INTO status_master (StatusValue, Code, Name, Description, IsTerminal, IsActive, CreatedAt)
        SELECT 5,'FVF','FvuFailed','Batch submitted to the FVU and failed validation; needs operator attention.',0,1,strftime('%Y-%m-%dT%H:%M:%SZ','now')
        WHERE NOT EXISTS (SELECT 1 FROM status_master WHERE StatusValue=5)
        """,
        """
        INSERT INTO status_master (StatusValue, Code, Name, Description, IsTerminal, IsActive, CreatedAt)
        SELECT 6,'FLD','Failed','Permanent failure (e.g. could not be saved after retries); needs manual intervention.',1,1,strftime('%Y-%m-%dT%H:%M:%SZ','now')
        WHERE NOT EXISTS (SELECT 1 FROM status_master WHERE StatusValue=6)
        """,
        """
        INSERT INTO status_master (StatusValue, Code, Name, Description, IsTerminal, IsActive, CreatedAt)
        SELECT 7,'UPL','Uploaded','Batch uploaded/submitted to CERSAI; awaiting a response file.',0,1,strftime('%Y-%m-%dT%H:%M:%SZ','now')
        WHERE NOT EXISTS (SELECT 1 FROM status_master WHERE StatusValue=7)
        """,
        """
        INSERT INTO status_master (StatusValue, Code, Name, Description, IsTerminal, IsActive, CreatedAt)
        SELECT 8,'RSP','ResponseRead','At least one CERSAI response file has been read for this record.',0,1,strftime('%Y-%m-%dT%H:%M:%SZ','now')
        WHERE NOT EXISTS (SELECT 1 FROM status_master WHERE StatusValue=8)
        """,
        """
        INSERT INTO status_master (StatusValue, Code, Name, Description, IsTerminal, IsActive, CreatedAt)
        SELECT 9,'RCN','Reconciled','Record reconciled (matched/resolved against the CERSAI reply).',1,1,strftime('%Y-%m-%dT%H:%M:%SZ','now')
        WHERE NOT EXISTS (SELECT 1 FROM status_master WHERE StatusValue=9)
        """,
        """
        INSERT INTO status_master (StatusValue, Code, Name, Description, IsTerminal, IsActive, CreatedAt)
        SELECT 10,'REJ','Rejected','Record permanently rejected by CERSAI.',1,1,strftime('%Y-%m-%dT%H:%M:%SZ','now')
        WHERE NOT EXISTS (SELECT 1 FROM status_master WHERE StatusValue=10)
        """,
    };

    /// <summary>
    /// Additive, idempotent column migrations. Older databases were created before the
    /// conditional-mandatory record-20 fields were modelled, so each missing column is
    /// added on startup (ALWAYS with a length, never NOT NULL / CHECK — matching the
    /// "length validation yes, other validation no" schema philosophy).
    /// </summary>
    public static readonly IReadOnlyList<(string Table, string Column, string Sql)> AdditiveMigrations = new[]
    {
        // Organization-owned customer identifier. Initialization renames the legacy
        // SourceCustomerId column, or adds CustomerId where a child table never had it.
        ("master_record", "CustomerId", "ALTER TABLE master_record ADD COLUMN CustomerId VARCHAR(50)"),
        ("kyc_record_20", "CustomerId", "ALTER TABLE kyc_record_20 ADD COLUMN CustomerId VARCHAR(50)"),
        ("kyc_record_30", "CustomerId", "ALTER TABLE kyc_record_30 ADD COLUMN CustomerId VARCHAR(50)"),
        ("kyc_record_40", "CustomerId", "ALTER TABLE kyc_record_40 ADD COLUMN CustomerId VARCHAR(50)"),
        ("kyc_record_50", "CustomerId", "ALTER TABLE kyc_record_50 ADD COLUMN CustomerId VARCHAR(50)"),
        ("kyc_record_60", "CustomerId", "ALTER TABLE kyc_record_60 ADD COLUMN CustomerId VARCHAR(50)"),
        ("kyc_record_70", "CustomerId", "ALTER TABLE kyc_record_70 ADD COLUMN CustomerId VARCHAR(50)"),
        ("legal_entity_record_20", "CustomerId", "ALTER TABLE legal_entity_record_20 ADD COLUMN CustomerId VARCHAR(50)"),
        ("legal_entity_record_30", "CustomerId", "ALTER TABLE legal_entity_record_30 ADD COLUMN CustomerId VARCHAR(50)"),
        ("legal_entity_record_40", "CustomerId", "ALTER TABLE legal_entity_record_40 ADD COLUMN CustomerId VARCHAR(50)"),
        ("legal_entity_record_50", "CustomerId", "ALTER TABLE legal_entity_record_50 ADD COLUMN CustomerId VARCHAR(50)"),
        ("legal_entity_record_60", "CustomerId", "ALTER TABLE legal_entity_record_60 ADD COLUMN CustomerId VARCHAR(50)"),
        ("legal_entity_record_70", "CustomerId", "ALTER TABLE legal_entity_record_70 ADD COLUMN CustomerId VARCHAR(50)"),
        ("legal_entity_record_60", "NumberOfRelatedPersons", "ALTER TABLE legal_entity_record_60 ADD COLUMN NumberOfRelatedPersons INTEGER"),
        ("legal_entity_record_60", "NumberOfBeneficialOwners", "ALTER TABLE legal_entity_record_60 ADD COLUMN NumberOfBeneficialOwners INTEGER"),
        ("master_record_response", "CustomerId", "ALTER TABLE master_record_response ADD COLUMN CustomerId VARCHAR(50)"),
        ("master_record_attempt", "CustomerId", "ALTER TABLE master_record_attempt ADD COLUMN CustomerId VARCHAR(50)"),
        ("master_record_reattempt", "CustomerId", "ALTER TABLE master_record_reattempt ADD COLUMN CustomerId VARCHAR(50)"),
        ("search_request", "CustomerId", "ALTER TABLE search_request ADD COLUMN CustomerId VARCHAR(50)"),

        ("kyc_record_20", "Minor", "ALTER TABLE kyc_record_20 ADD COLUMN Minor VARCHAR(1)"),
        ("kyc_record_20", "DoBMatchWithOvd", "ALTER TABLE kyc_record_20 ADD COLUMN DoBMatchWithOvd VARCHAR(1)"),
        ("kyc_record_20", "NameMatchWithOvd", "ALTER TABLE kyc_record_20 ADD COLUMN NameMatchWithOvd VARCHAR(1)"),
        ("kyc_record_20", "PhotoMatchWithOvd", "ALTER TABLE kyc_record_20 ADD COLUMN PhotoMatchWithOvd VARCHAR(1)"),
        ("kyc_record_20", "GenderProvidedInOvd", "ALTER TABLE kyc_record_20 ADD COLUMN GenderProvidedInOvd VARCHAR(1)"),
        ("kyc_record_20", "GenderMatchWithOvd", "ALTER TABLE kyc_record_20 ADD COLUMN GenderMatchWithOvd VARCHAR(1)"),
        ("kyc_record_20", "Form97Provided", "ALTER TABLE kyc_record_20 ADD COLUMN Form97Provided VARCHAR(1)"),
        ("kyc_record_20", "Form61Provided", "ALTER TABLE kyc_record_20 ADD COLUMN Form61Provided VARCHAR(1)"),
        ("kyc_record_20", "PanDocument", "ALTER TABLE kyc_record_20 ADD COLUMN PanDocument VARCHAR(125)"),
        ("kyc_record_20", "OtherTypeOfImpairment", "ALTER TABLE kyc_record_20 ADD COLUMN OtherTypeOfImpairment VARCHAR(150)"),
        ("kyc_record_20", "DisabilityReferenceNumber", "ALTER TABLE kyc_record_20 ADD COLUMN DisabilityReferenceNumber VARCHAR(18)"),
        ("kyc_record_20", "PermanentDisability", "ALTER TABLE kyc_record_20 ADD COLUMN PermanentDisability VARCHAR(1)"),
        ("kyc_record_20", "DisabilityDate", "ALTER TABLE kyc_record_20 ADD COLUMN DisabilityDate VARCHAR(10)"),
        ("kyc_record_20", "PercentageOfImpairment", "ALTER TABLE kyc_record_20 ADD COLUMN PercentageOfImpairment VARCHAR(3)"),
        ("kyc_record_20", "DifferentlyAbledSupportedByDocument", "ALTER TABLE kyc_record_20 ADD COLUMN DifferentlyAbledSupportedByDocument VARCHAR(1)"),

        // ---- master_record stage/response tracking columns (added when the lifecycle was
        //      extended past FVU. Columns are nullable; existing rows keep their old values.) ----
        ("master_record", "BatchRecordLine", "ALTER TABLE master_record ADD COLUMN BatchRecordLine INTEGER"),
        ("master_record", "ClientType", "ALTER TABLE master_record ADD COLUMN ClientType VARCHAR(1)"),
        ("master_record", "IsCrmFetched", "ALTER TABLE master_record ADD COLUMN IsCrmFetched INTEGER"),
        ("master_record", "IsSaved", "ALTER TABLE master_record ADD COLUMN IsSaved INTEGER"),
        ("master_record", "IsBatched", "ALTER TABLE master_record ADD COLUMN IsBatched INTEGER"),
        ("master_record", "IsUploaded", "ALTER TABLE master_record ADD COLUMN IsUploaded INTEGER"),
        ("master_record", "IsResponseRead", "ALTER TABLE master_record ADD COLUMN IsResponseRead INTEGER"),
        ("master_record", "IsReconciled", "ALTER TABLE master_record ADD COLUMN IsReconciled INTEGER"),
        ("master_record", "IsRejected", "ALTER TABLE master_record ADD COLUMN IsRejected INTEGER"),
        ("master_record", "CrmFetchedAt", "ALTER TABLE master_record ADD COLUMN CrmFetchedAt TEXT"),
        ("master_record", "SavedAt", "ALTER TABLE master_record ADD COLUMN SavedAt TEXT"),
        ("master_record", "BatchedAt", "ALTER TABLE master_record ADD COLUMN BatchedAt TEXT"),
        ("master_record", "UploadedAt", "ALTER TABLE master_record ADD COLUMN UploadedAt TEXT"),
        ("master_record", "FirstResponseAt", "ALTER TABLE master_record ADD COLUMN FirstResponseAt TEXT"),
        ("master_record", "ReconciledAt", "ALTER TABLE master_record ADD COLUMN ReconciledAt TEXT"),
        ("master_record", "LastResponseFileNumber", "ALTER TABLE master_record ADD COLUMN LastResponseFileNumber INTEGER"),
        ("master_record", "LastResponseFileName", "ALTER TABLE master_record ADD COLUMN LastResponseFileName VARCHAR(260)"),
        ("master_record", "LastResponseAckNumber", "ALTER TABLE master_record ADD COLUMN LastResponseAckNumber VARCHAR(10)"),
        ("master_record", "LastResponseStatus", "ALTER TABLE master_record ADD COLUMN LastResponseStatus VARCHAR(2)"),
        ("master_record", "LastResponseCkycReference", "ALTER TABLE master_record ADD COLUMN LastResponseCkycReference VARCHAR(15)"),
        ("master_record", "LastResponseCkycNumber", "ALTER TABLE master_record ADD COLUMN LastResponseCkycNumber VARCHAR(15)"),
        ("master_record", "LastResponseRejectionRemark", "ALTER TABLE master_record ADD COLUMN LastResponseRejectionRemark VARCHAR(500)"),
        ("master_record", "LastResponseReadAt", "ALTER TABLE master_record ADD COLUMN LastResponseReadAt TEXT"),
        ("master_record", "LastResponseRemarks", "ALTER TABLE master_record ADD COLUMN LastResponseRemarks VARCHAR(1000)"),
        ("master_record", "ReconStatus", "ALTER TABLE master_record ADD COLUMN ReconStatus VARCHAR(50)"),
        ("master_record", "ReconRemarks", "ALTER TABLE master_record ADD COLUMN ReconRemarks VARCHAR(1000)"),

        // ---- retry / reattempt / reconciliation columns (added when the retry engine,
        //      reattempt processor and reconciliation report were introduced). Nullable —
        //      existing rows keep their prior values. ----
        ("master_record", "LastActivity", "ALTER TABLE master_record ADD COLUMN LastActivity VARCHAR(50)"),
        ("master_record", "NextRetryAt", "ALTER TABLE master_record ADD COLUMN NextRetryAt TEXT"),
        ("master_record", "NeedsReconcile", "ALTER TABLE master_record ADD COLUMN NeedsReconcile INTEGER"),
        ("master_record", "ReattemptCount", "ALTER TABLE master_record ADD COLUMN ReattemptCount INTEGER"),
        ("master_record", "ReattemptedAt", "ALTER TABLE master_record ADD COLUMN ReattemptedAt TEXT"),
        ("master_record_attempt", "ActivityTypeId", "ALTER TABLE master_record_attempt ADD COLUMN ActivityTypeId INTEGER"),
        ("master_record_attempt", "NextRetryAt", "ALTER TABLE master_record_attempt ADD COLUMN NextRetryAt TEXT"),
        ("search_batch", "FvuZipPath", "ALTER TABLE search_batch ADD COLUMN FvuZipPath VARCHAR(1000)"),
        ("search_batch", "FvuHash", "ALTER TABLE search_batch ADD COLUMN FvuHash VARCHAR(128)"),
        ("search_response", "RecordLevelHash", "ALTER TABLE search_response ADD COLUMN RecordLevelHash VARCHAR(128)"),
        ("search_response_file", "SourceArchiveName", "ALTER TABLE search_response_file ADD COLUMN SourceArchiveName VARCHAR(260)"),
        ("search_response_file", "SourceHash", "ALTER TABLE search_response_file ADD COLUMN SourceHash VARCHAR(128)"),
        ("search_request", "ResponseStatus", "ALTER TABLE search_request ADD COLUMN ResponseStatus VARCHAR(50)"),
        ("search_request", "LastSearchKey", "ALTER TABLE search_request ADD COLUMN LastSearchKey VARCHAR(20)"),
        ("search_request", "LastCkycReference", "ALTER TABLE search_request ADD COLUMN LastCkycReference VARCHAR(15)"),
        ("search_request", "LastResponseRemark", "ALTER TABLE search_request ADD COLUMN LastResponseRemark VARCHAR(250)"),
        ("search_request", "ResponseReadAt", "ALTER TABLE search_request ADD COLUMN ResponseReadAt TEXT"),
    };
}
