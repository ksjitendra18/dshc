using System;
using System.Collections.Generic;
using CKYC.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CKYC.Data;

public partial class CkycDbContext : DbContext
{
    public CkycDbContext(DbContextOptions<CkycDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<ActivityType> ActivityTypes { get; set; }

    public virtual DbSet<Batch> Batches { get; set; }

    public virtual DbSet<DownloadResponseArtifact> DownloadResponseArtifacts { get; set; }

    public virtual DbSet<DownloadResponseFile> DownloadResponseFiles { get; set; }

    public virtual DbSet<DownloadResponseLine> DownloadResponseLines { get; set; }

    public virtual DbSet<FileContent> FileContents { get; set; }

    public virtual DbSet<FvuRun> FvuRuns { get; set; }

    public virtual DbSet<IndividualDocument> IndividualDocuments { get; set; }

    public virtual DbSet<IndividualRecord20> IndividualRecord20s { get; set; }

    public virtual DbSet<IndividualRecord30> IndividualRecord30s { get; set; }

    public virtual DbSet<IndividualRecord40> IndividualRecord40s { get; set; }

    public virtual DbSet<IndividualRecord50> IndividualRecord50s { get; set; }

    public virtual DbSet<IndividualRecord60> IndividualRecord60s { get; set; }

    public virtual DbSet<IndividualRecord70> IndividualRecord70s { get; set; }

    public virtual DbSet<LegalEntityDocument> LegalEntityDocuments { get; set; }

    public virtual DbSet<LegalEntityRecord20> LegalEntityRecord20s { get; set; }

    public virtual DbSet<LegalEntityRecord30> LegalEntityRecord30s { get; set; }

    public virtual DbSet<LegalEntityRecord40> LegalEntityRecord40s { get; set; }

    public virtual DbSet<LegalEntityRecord50> LegalEntityRecord50s { get; set; }

    public virtual DbSet<LegalEntityRecord60> LegalEntityRecord60s { get; set; }

    public virtual DbSet<LegalEntityRecord70> LegalEntityRecord70s { get; set; }

    public virtual DbSet<MasterRecord> MasterRecords { get; set; }

    public virtual DbSet<MasterRecordAttempt> MasterRecordAttempts { get; set; }

    public virtual DbSet<MasterRecordBatch> MasterRecordBatches { get; set; }

    public virtual DbSet<MasterRecordReattempt> MasterRecordReattempts { get; set; }

    public virtual DbSet<MasterRecordResponse> MasterRecordResponses { get; set; }

    public virtual DbSet<SearchBatch> SearchBatches { get; set; }

    public virtual DbSet<SearchRequest> SearchRequests { get; set; }

    public virtual DbSet<SearchResponse> SearchResponses { get; set; }

    public virtual DbSet<SearchResponseFile> SearchResponseFiles { get; set; }

    public virtual DbSet<StatusMaster> StatusMasters { get; set; }

    public virtual DbSet<UpdateBatch> UpdateBatches { get; set; }

    public virtual DbSet<UpdateRequest> UpdateRequests { get; set; }

    public virtual DbSet<UpdateResponse> UpdateResponses { get; set; }

    public virtual DbSet<UpdateResponseFile> UpdateResponseFiles { get; set; }

    public virtual DbSet<UploadResponseFile> UploadResponseFiles { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ActivityType>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__activity__3214EC07523E6DCC");

            entity.ToTable("activity_type");

            entity.HasIndex(e => e.Code, "ix_activity_code");

            entity.Property(e => e.Code).HasMaxLength(50);
            entity.Property(e => e.Name).HasMaxLength(100);
            entity.Property(e => e.Remarks).HasMaxLength(500);
        });

        modelBuilder.Entity<Batch>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__batch__3214EC07126215C3");

            entity.ToTable("batch");

            entity.HasIndex(e => e.BatchKey, "ix_batch_key");

            entity.Property(e => e.BatchKey).HasMaxLength(60);
            entity.Property(e => e.UploadFileName).HasMaxLength(260);
            entity.Property(e => e.UploadFilePath).HasMaxLength(1000);
            entity.Property(e => e.ZipPath).HasMaxLength(1000);
        });

        modelBuilder.Entity<DownloadResponseArtifact>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__download__3214EC077820DDF0");

            entity.ToTable("download_response_artifact");

            entity.HasIndex(e => e.DownloadResponseFileId, "ix_download_response_artifact_file");

            entity.Property(e => e.EntryPath).HasMaxLength(1000);
            entity.Property(e => e.FileName).HasMaxLength(260);
            entity.Property(e => e.Sha256).HasMaxLength(128);
        });

        modelBuilder.Entity<DownloadResponseFile>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__download__3214EC07056EA15F");

            entity.ToTable("download_response_file");

            entity.HasIndex(e => new { e.SourceHash, e.ResponseFileName }, "ix_download_response_file_hash");

            entity.Property(e => e.ClientType).HasMaxLength(1);
            entity.Property(e => e.FiCode).HasMaxLength(6);
            entity.Property(e => e.RegionCode).HasMaxLength(11);
            entity.Property(e => e.ResponseDate).HasMaxLength(30);
            entity.Property(e => e.ResponseFileName).HasMaxLength(260);
            entity.Property(e => e.SourceArchiveName).HasMaxLength(260);
            entity.Property(e => e.SourceHash).HasMaxLength(128);
            entity.Property(e => e.Version).HasMaxLength(20);
        });

        modelBuilder.Entity<DownloadResponseLine>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__download__3214EC07B53AC933");

            entity.ToTable("download_response_line");

            entity.HasIndex(e => e.DownloadResponseFileId, "ix_download_response_line_file");

            entity.Property(e => e.CkycNumber).HasMaxLength(15);
            entity.Property(e => e.RecordType).HasMaxLength(2);
            entity.Property(e => e.SourceEntryPath).HasMaxLength(1000);
        });

        modelBuilder.Entity<FileContent>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__file_con__3214EC073C5DEC90");

            entity.ToTable("file_content");

            entity.HasIndex(e => e.Sha256, "UQ__file_con__503DE5212086D41B").IsUnique();

            entity.Property(e => e.Sha256)
                .HasMaxLength(64)
                .IsUnicode(false)
                .IsFixedLength();
        });

        modelBuilder.Entity<FvuRun>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__fvu_run__3214EC07D81BF64C");

            entity.ToTable("fvu_run");

            entity.HasIndex(e => e.BatchKey, "ix_fvu_run_key");

            entity.Property(e => e.BatchKey).HasMaxLength(60);
            entity.Property(e => e.ErrorMessage).HasMaxLength(2000);
            entity.Property(e => e.HashValue).HasMaxLength(128);
            entity.Property(e => e.OutputZipPath).HasMaxLength(1000);
        });

        modelBuilder.Entity<IndividualDocument>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__individu__3214EC0771CA8F2D");

            entity.ToTable("individual_document");

            entity.HasIndex(e => e.FileContentId, "ix_individual_document_content");

            entity.HasIndex(e => new { e.MasterRecordId, e.CanonicalFileName }, "uq_individual_document_name").IsUnique();

            entity.Property(e => e.CanonicalFileName).HasMaxLength(255);
            entity.Property(e => e.DocumentKind).HasMaxLength(50);
            entity.Property(e => e.MediaType).HasMaxLength(100);
            entity.Property(e => e.OriginalFileName).HasMaxLength(255);
            entity.Property(e => e.SourceReference).HasMaxLength(500);
            entity.Property(e => e.SourceType).HasMaxLength(30);

            entity.HasOne(d => d.FileContent).WithMany(p => p.IndividualDocuments)
                .HasForeignKey(d => d.FileContentId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_individual_document_content");

            entity.HasOne(d => d.MasterRecord).WithMany(p => p.IndividualDocuments)
                .HasForeignKey(d => d.MasterRecordId)
                .HasConstraintName("fk_individual_document_master");
        });

        modelBuilder.Entity<IndividualRecord20>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__individu__3214EC07440F9E92");

            entity.ToTable("individual_record_20");

            entity.HasIndex(e => e.CustomerId, "ix_individual_record20_customer");

            entity.HasIndex(e => e.MasterRecordId, "ix_individual_record20_master");

            entity.Property(e => e.CustomerId).HasMaxLength(50);
            entity.Property(e => e.DateOfBirth).HasMaxLength(10);
            entity.Property(e => e.DifferentlyAbledStatus).HasMaxLength(1);
            entity.Property(e => e.DifferentlyAbledSupportedByDocument).HasMaxLength(1);
            entity.Property(e => e.DifferentlyAbledType).HasMaxLength(50);
            entity.Property(e => e.DisabilityDate).HasMaxLength(10);
            entity.Property(e => e.DisabilityReferenceNumber).HasMaxLength(18);
            entity.Property(e => e.DoBmatchWithOvd)
                .HasMaxLength(1)
                .HasColumnName("DoBMatchWithOvd");
            entity.Property(e => e.FatherFirst).HasMaxLength(33);
            entity.Property(e => e.FatherLast).HasMaxLength(33);
            entity.Property(e => e.FatherMiddle).HasMaxLength(33);
            entity.Property(e => e.FatherTitle).HasMaxLength(4);
            entity.Property(e => e.Form61Provided).HasMaxLength(1);
            entity.Property(e => e.Form97Provided).HasMaxLength(1);
            entity.Property(e => e.Gender).HasMaxLength(1);
            entity.Property(e => e.GenderMatchWithOvd).HasMaxLength(1);
            entity.Property(e => e.GenderProvidedInOvd).HasMaxLength(1);
            entity.Property(e => e.KycType).HasMaxLength(1);
            entity.Property(e => e.MaidenFirst).HasMaxLength(33);
            entity.Property(e => e.MaidenLast).HasMaxLength(33);
            entity.Property(e => e.MaidenMiddle).HasMaxLength(33);
            entity.Property(e => e.MaidenTitle).HasMaxLength(4);
            entity.Property(e => e.Minor).HasMaxLength(1);
            entity.Property(e => e.MotherFirst).HasMaxLength(33);
            entity.Property(e => e.MotherLast).HasMaxLength(33);
            entity.Property(e => e.MotherMiddle).HasMaxLength(33);
            entity.Property(e => e.MotherTitle).HasMaxLength(4);
            entity.Property(e => e.NameFirst).HasMaxLength(33);
            entity.Property(e => e.NameLast).HasMaxLength(33);
            entity.Property(e => e.NameMatchWithOvd).HasMaxLength(1);
            entity.Property(e => e.NameMiddle).HasMaxLength(33);
            entity.Property(e => e.NameTitle).HasMaxLength(4);
            entity.Property(e => e.Nationality).HasMaxLength(2);
            entity.Property(e => e.NationalitySupportedByDocument).HasMaxLength(1);
            entity.Property(e => e.OtherTypeOfImpairment).HasMaxLength(150);
            entity.Property(e => e.Pan).HasMaxLength(125);
            entity.Property(e => e.PanDocument).HasMaxLength(125);
            entity.Property(e => e.PanVerified).HasMaxLength(1);
            entity.Property(e => e.PercentageOfImpairment).HasMaxLength(3);
            entity.Property(e => e.PermanentDisability).HasMaxLength(1);
            entity.Property(e => e.PhotoMatchWithOvd).HasMaxLength(1);
            entity.Property(e => e.PhotoOfIndividual).HasMaxLength(125);
            entity.Property(e => e.ResidentialStatus).HasMaxLength(50);
            entity.Property(e => e.ResidentialSupportedByDocument).HasMaxLength(1);
            entity.Property(e => e.SearchKey).HasMaxLength(20);
            entity.Property(e => e.SpouseFirst).HasMaxLength(33);
            entity.Property(e => e.SpouseLast).HasMaxLength(33);
            entity.Property(e => e.SpouseMiddle).HasMaxLength(33);
            entity.Property(e => e.SpouseTitle).HasMaxLength(4);
        });

        modelBuilder.Entity<IndividualRecord30>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__individu__3214EC07A15CD380");

            entity.ToTable("individual_record_30");

            entity.HasIndex(e => e.CustomerId, "ix_individual_record30_customer");

            entity.HasIndex(e => e.MasterRecordId, "ix_individual_record30_master");

            entity.Property(e => e.CertifiedCopyWithOriginal).HasMaxLength(1);
            entity.Property(e => e.CopyOfOvd).HasMaxLength(125);
            entity.Property(e => e.CustomerId).HasMaxLength(50);
            entity.Property(e => e.DataFromOfflineVerification).HasMaxLength(1);
            entity.Property(e => e.DrivingLicenseExpiryDate).HasMaxLength(10);
            entity.Property(e => e.EkycDataFromUidai).HasMaxLength(1);
            entity.Property(e => e.EquivalentEdoc)
                .HasMaxLength(1)
                .HasColumnName("EquivalentEDoc");
            entity.Property(e => e.IdNumber).HasMaxLength(100);
            entity.Property(e => e.LengthOfAadhaar).HasMaxLength(1);
            entity.Property(e => e.ModeOfAadhaarVerification).HasMaxLength(50);
            entity.Property(e => e.ModeOfAuthentication).HasMaxLength(1);
            entity.Property(e => e.OvdType).HasMaxLength(1);
            entity.Property(e => e.PassportExpiryDate).HasMaxLength(10);
            entity.Property(e => e.PresenceInEciRepository).HasMaxLength(1);
            entity.Property(e => e.PresenceInMeaRepository).HasMaxLength(1);
            entity.Property(e => e.PresenceInNprRecords).HasMaxLength(1);
            entity.Property(e => e.PresenceInNregaRepository).HasMaxLength(1);
            entity.Property(e => e.PresenceInRtoRepository).HasMaxLength(1);
            entity.Property(e => e.VerifiedFromDigiLocker).HasMaxLength(1);
        });

        modelBuilder.Entity<IndividualRecord40>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__individu__3214EC0702D5EE5F");

            entity.ToTable("individual_record_40");

            entity.HasIndex(e => e.CustomerId, "ix_individual_record40_customer");

            entity.HasIndex(e => e.MasterRecordId, "ix_individual_record40_master");

            entity.Property(e => e.CurrAadhaarVerification).HasMaxLength(1);
            entity.Property(e => e.CurrAddressExactlyMatch).HasMaxLength(13);
            entity.Property(e => e.CurrCertifiedCopy).HasMaxLength(1);
            entity.Property(e => e.CurrCity).HasMaxLength(60);
            entity.Property(e => e.CurrCopyOfOvd).HasMaxLength(125);
            entity.Property(e => e.CurrCountry).HasMaxLength(2);
            entity.Property(e => e.CurrDeemedPoa).HasMaxLength(2);
            entity.Property(e => e.CurrDeemedPoaVerified).HasMaxLength(1);
            entity.Property(e => e.CurrDigiLockerVerified).HasMaxLength(1);
            entity.Property(e => e.CurrDigipin).HasMaxLength(10);
            entity.Property(e => e.CurrDistrict).HasMaxLength(6);
            entity.Property(e => e.CurrEquivalentEdoc)
                .HasMaxLength(1)
                .HasColumnName("CurrEquivalentEDoc");
            entity.Property(e => e.CurrForeignGovDocument).HasMaxLength(125);
            entity.Property(e => e.CurrIdNumber).HasMaxLength(100);
            entity.Property(e => e.CurrLengthOfAadhaar).HasMaxLength(1);
            entity.Property(e => e.CurrLine1).HasMaxLength(60);
            entity.Property(e => e.CurrLine2).HasMaxLength(60);
            entity.Property(e => e.CurrLine3).HasMaxLength(60);
            entity.Property(e => e.CurrMatchOvd).HasMaxLength(13);
            entity.Property(e => e.CurrOvdExpiryDate).HasMaxLength(10);
            entity.Property(e => e.CurrPhysicalReOfficial).HasMaxLength(1);
            entity.Property(e => e.CurrPhysicalThirdParty).HasMaxLength(1);
            entity.Property(e => e.CurrPinCode).HasMaxLength(6);
            entity.Property(e => e.CurrPinOthers).HasMaxLength(6);
            entity.Property(e => e.CurrPositiveVerification).HasMaxLength(1);
            entity.Property(e => e.CurrPresenceInRepository).HasMaxLength(1);
            entity.Property(e => e.CurrProofOfAddress).HasMaxLength(1);
            entity.Property(e => e.CurrProofOfAddressType).HasMaxLength(1);
            entity.Property(e => e.CurrRemoteGeoTagging).HasMaxLength(1);
            entity.Property(e => e.CurrSameAsPermanent).HasMaxLength(1);
            entity.Property(e => e.CurrState).HasMaxLength(2);
            entity.Property(e => e.CurrSupportedDocument).HasMaxLength(1);
            entity.Property(e => e.CustomerId).HasMaxLength(50);
            entity.Property(e => e.PermCity).HasMaxLength(60);
            entity.Property(e => e.PermCountry).HasMaxLength(2);
            entity.Property(e => e.PermDigipin).HasMaxLength(10);
            entity.Property(e => e.PermDistrict).HasMaxLength(6);
            entity.Property(e => e.PermLine1).HasMaxLength(60);
            entity.Property(e => e.PermLine2).HasMaxLength(60);
            entity.Property(e => e.PermLine3).HasMaxLength(60);
            entity.Property(e => e.PermMatchOvd).HasMaxLength(13);
            entity.Property(e => e.PermPinCode).HasMaxLength(6);
            entity.Property(e => e.PermPinOthers).HasMaxLength(6);
            entity.Property(e => e.PermState).HasMaxLength(2);
            entity.Property(e => e.PermSupportedDocument).HasMaxLength(1);
        });

        modelBuilder.Entity<IndividualRecord50>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__individu__3214EC07D400BA15");

            entity.ToTable("individual_record_50");

            entity.HasIndex(e => e.CustomerId, "ix_individual_record50_customer");

            entity.HasIndex(e => e.MasterRecordId, "ix_individual_record50_master");

            entity.Property(e => e.CountryCode).HasMaxLength(4);
            entity.Property(e => e.CustomerId).HasMaxLength(50);
            entity.Property(e => e.EmailAddress).HasMaxLength(254);
            entity.Property(e => e.EmailValidatedViaOtp).HasMaxLength(1);
            entity.Property(e => e.MobileNumber).HasMaxLength(15);
            entity.Property(e => e.MobileValidatedViaOtp).HasMaxLength(1);
            entity.Property(e => e.MobileValidatedViaThirdParty).HasMaxLength(1);
        });

        modelBuilder.Entity<IndividualRecord60>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__individu__3214EC073CC984B2");

            entity.ToTable("individual_record_60");

            entity.HasIndex(e => e.CustomerId, "ix_individual_record60_customer");

            entity.HasIndex(e => e.MasterRecordId, "ix_individual_record60_master");

            entity.Property(e => e.CkycNumberOfRelatedPerson).HasMaxLength(14);
            entity.Property(e => e.CustomerId).HasMaxLength(50);
            entity.Property(e => e.RelatedPersonType).HasMaxLength(1);
        });

        modelBuilder.Entity<IndividualRecord70>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__individu__3214EC073DAC71A3");

            entity.ToTable("individual_record_70");

            entity.HasIndex(e => e.CustomerId, "ix_individual_record70_customer");

            entity.HasIndex(e => e.MasterRecordId, "ix_individual_record70_master");

            entity.Property(e => e.AttestationDate).HasMaxLength(10);
            entity.Property(e => e.ClientConsent).HasMaxLength(125);
            entity.Property(e => e.CustomerId).HasMaxLength(50);
            entity.Property(e => e.DeclarationDate).HasMaxLength(10);
            entity.Property(e => e.DeclarationDocument).HasMaxLength(125);
            entity.Property(e => e.DeclarationFlag).HasMaxLength(1);
            entity.Property(e => e.EmployeeBranch).HasMaxLength(50);
            entity.Property(e => e.EmployeeCkycId).HasMaxLength(50);
            entity.Property(e => e.EmployeeCode).HasMaxLength(50);
            entity.Property(e => e.EmployeeDesignation).HasMaxLength(50);
            entity.Property(e => e.EmployeeName).HasMaxLength(50);
            entity.Property(e => e.FaceToFaceWithNonOfficial).HasMaxLength(1);
            entity.Property(e => e.FaceToFaceWithReOfficial).HasMaxLength(1);
            entity.Property(e => e.InstitutionCode).HasMaxLength(50);
            entity.Property(e => e.InstitutionName).HasMaxLength(50);
            entity.Property(e => e.NonFaceToFace).HasMaxLength(1);
            entity.Property(e => e.Place).HasMaxLength(40);
            entity.Property(e => e.Remarks).HasMaxLength(200);
            entity.Property(e => e.VideoKycWithReOfficial).HasMaxLength(1);
            entity.Property(e => e.VideoKycWithoutOfficial).HasMaxLength(1);
        });

        modelBuilder.Entity<LegalEntityDocument>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__legal_en__3214EC0720261F66");

            entity.ToTable("legal_entity_document");

            entity.HasIndex(e => e.FileContentId, "ix_legal_entity_document_content");

            entity.HasIndex(e => new { e.MasterRecordId, e.CanonicalFileName }, "uq_legal_entity_document_name").IsUnique();

            entity.Property(e => e.CanonicalFileName).HasMaxLength(255);
            entity.Property(e => e.DocumentKind).HasMaxLength(50);
            entity.Property(e => e.MediaType).HasMaxLength(100);
            entity.Property(e => e.OriginalFileName).HasMaxLength(255);
            entity.Property(e => e.SourceReference).HasMaxLength(500);
            entity.Property(e => e.SourceType).HasMaxLength(30);

            entity.HasOne(d => d.FileContent).WithMany(p => p.LegalEntityDocuments)
                .HasForeignKey(d => d.FileContentId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_legal_entity_document_content");

            entity.HasOne(d => d.MasterRecord).WithMany(p => p.LegalEntityDocuments)
                .HasForeignKey(d => d.MasterRecordId)
                .HasConstraintName("fk_legal_entity_document_master");
        });

        modelBuilder.Entity<LegalEntityRecord20>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__legal_en__3214EC07EC3805E1");

            entity.ToTable("legal_entity_record_20");

            entity.HasIndex(e => e.CustomerId, "ix_le_record20_customer");

            entity.HasIndex(e => e.MasterRecordId, "ix_le_record20_master");

            entity.Property(e => e.CountryOfIncorporation).HasMaxLength(2);
            entity.Property(e => e.CustomerId).HasMaxLength(50);
            entity.Property(e => e.DateOfCommencement).HasMaxLength(10);
            entity.Property(e => e.DateOfIncorporation).HasMaxLength(10);
            entity.Property(e => e.EntityConstitution).HasMaxLength(2);
            entity.Property(e => e.EntityName).HasMaxLength(99);
            entity.Property(e => e.Form97).HasMaxLength(1);
            entity.Property(e => e.ListedCompany).HasMaxLength(1);
            entity.Property(e => e.Pan).HasMaxLength(10);
            entity.Property(e => e.PanDocument).HasMaxLength(125);
            entity.Property(e => e.PanVerified).HasMaxLength(1);
            entity.Property(e => e.PlaceOfIncorporation).HasMaxLength(50);
            entity.Property(e => e.RegisteredFirm).HasMaxLength(1);
            entity.Property(e => e.RegisteredTrust).HasMaxLength(1);
            entity.Property(e => e.SearchKey).HasMaxLength(20);
            entity.Property(e => e.TinGstNumber).HasMaxLength(15);
            entity.Property(e => e.TinGstnDocument).HasMaxLength(125);
            entity.Property(e => e.TinIssuingCountry).HasMaxLength(2);
        });

        modelBuilder.Entity<LegalEntityRecord30>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__legal_en__3214EC07EF32C101");

            entity.ToTable("legal_entity_record_30");

            entity.HasIndex(e => e.CustomerId, "ix_le_record30_customer");

            entity.HasIndex(e => e.MasterRecordId, "ix_le_record30_master");

            entity.Property(e => e.ActivityProof1).HasMaxLength(125);
            entity.Property(e => e.ActivityProof2).HasMaxLength(125);
            entity.Property(e => e.CertificateOfCommencement).HasMaxLength(125);
            entity.Property(e => e.CertificateOfIncorporation).HasMaxLength(125);
            entity.Property(e => e.Cin).HasMaxLength(21);
            entity.Property(e => e.CustomerId).HasMaxLength(50);
            entity.Property(e => e.InfoEstablishExistence).HasMaxLength(125);
            entity.Property(e => e.Llpin).HasMaxLength(7);
            entity.Property(e => e.LlpinCertificate).HasMaxLength(125);
            entity.Property(e => e.MemorandumAndArticles).HasMaxLength(125);
            entity.Property(e => e.NamesAllPartners).HasMaxLength(125);
            entity.Property(e => e.NamesBeneficiariesTrustees).HasMaxLength(125);
            entity.Property(e => e.NamesSeniorManagement).HasMaxLength(125);
            entity.Property(e => e.OtherTypePowerOfAttorney).HasMaxLength(125);
            entity.Property(e => e.OtherTypeRegistrationCertificate).HasMaxLength(125);
            entity.Property(e => e.OtherTypeRegistrationNumber).HasMaxLength(50);
            entity.Property(e => e.OthersCompany).HasMaxLength(125);
            entity.Property(e => e.OthersOtherType).HasMaxLength(125);
            entity.Property(e => e.OthersPartnership).HasMaxLength(125);
            entity.Property(e => e.OthersTrust).HasMaxLength(125);
            entity.Property(e => e.OthersUnincorporated).HasMaxLength(125);
            entity.Property(e => e.PartnershipDeed).HasMaxLength(125);
            entity.Property(e => e.RegistrationCertificate).HasMaxLength(125);
            entity.Property(e => e.RegistrationNumber).HasMaxLength(50);
            entity.Property(e => e.ResolutionBoardPoA).HasMaxLength(125);
            entity.Property(e => e.ResolutionManagingBody).HasMaxLength(125);
            entity.Property(e => e.SupportingDocumentsPoi).HasMaxLength(125);
            entity.Property(e => e.TrustDeed).HasMaxLength(125);
            entity.Property(e => e.TrustPowerOfAttorney).HasMaxLength(125);
            entity.Property(e => e.TrustRegistrationCertificate).HasMaxLength(125);
            entity.Property(e => e.TrustRegistrationNumber).HasMaxLength(50);
            entity.Property(e => e.UnincorporatedPowerOfAttorney).HasMaxLength(125);
            entity.Property(e => e.UnincorporatedRegCertificate).HasMaxLength(125);
            entity.Property(e => e.UnincorporatedRegNumber).HasMaxLength(50);
        });

        modelBuilder.Entity<LegalEntityRecord40>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__legal_en__3214EC0706916BA6");

            entity.ToTable("legal_entity_record_40");

            entity.HasIndex(e => e.CustomerId, "ix_le_record40_customer");

            entity.HasIndex(e => e.MasterRecordId, "ix_le_record40_master");

            entity.Property(e => e.CustomerId).HasMaxLength(50);
            entity.Property(e => e.PrinCity).HasMaxLength(60);
            entity.Property(e => e.PrinCountry).HasMaxLength(2);
            entity.Property(e => e.PrinDigipin).HasMaxLength(10);
            entity.Property(e => e.PrinDistrict).HasMaxLength(4);
            entity.Property(e => e.PrinDocument).HasMaxLength(125);
            entity.Property(e => e.PrinLine1).HasMaxLength(60);
            entity.Property(e => e.PrinLine2).HasMaxLength(60);
            entity.Property(e => e.PrinLine3).HasMaxLength(60);
            entity.Property(e => e.PrinOtherDocumentName).HasMaxLength(50);
            entity.Property(e => e.PrinPinCode).HasMaxLength(6);
            entity.Property(e => e.PrinPinOthers).HasMaxLength(6);
            entity.Property(e => e.PrinProofOfAddress).HasMaxLength(1);
            entity.Property(e => e.PrinState).HasMaxLength(2);
            entity.Property(e => e.RegCity).HasMaxLength(60);
            entity.Property(e => e.RegCountry).HasMaxLength(2);
            entity.Property(e => e.RegDigipin).HasMaxLength(10);
            entity.Property(e => e.RegDistrict).HasMaxLength(4);
            entity.Property(e => e.RegDocument).HasMaxLength(125);
            entity.Property(e => e.RegLine1).HasMaxLength(60);
            entity.Property(e => e.RegLine2).HasMaxLength(60);
            entity.Property(e => e.RegLine3).HasMaxLength(60);
            entity.Property(e => e.RegOtherDocumentName).HasMaxLength(50);
            entity.Property(e => e.RegPinCode).HasMaxLength(6);
            entity.Property(e => e.RegPinOthers).HasMaxLength(6);
            entity.Property(e => e.RegProofOfAddress).HasMaxLength(1);
            entity.Property(e => e.RegState).HasMaxLength(2);
            entity.Property(e => e.SameAsRegistered).HasMaxLength(1);
        });

        modelBuilder.Entity<LegalEntityRecord50>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__legal_en__3214EC07514960BB");

            entity.ToTable("legal_entity_record_50");

            entity.HasIndex(e => e.CustomerId, "ix_le_record50_customer");

            entity.HasIndex(e => e.MasterRecordId, "ix_le_record50_master");

            entity.Property(e => e.CountryCode1).HasMaxLength(6);
            entity.Property(e => e.CountryCode2).HasMaxLength(6);
            entity.Property(e => e.CustomerId).HasMaxLength(50);
            entity.Property(e => e.EmailId1).HasMaxLength(254);
            entity.Property(e => e.EmailId2).HasMaxLength(254);
            entity.Property(e => e.Fax).HasMaxLength(12);
            entity.Property(e => e.MobileNumber1).HasMaxLength(15);
            entity.Property(e => e.MobileNumber2).HasMaxLength(15);
            entity.Property(e => e.Telephone).HasMaxLength(12);
        });

        modelBuilder.Entity<LegalEntityRecord60>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__legal_en__3214EC079EFB341E");

            entity.ToTable("legal_entity_record_60");

            entity.HasIndex(e => e.CustomerId, "ix_le_record60_customer");

            entity.HasIndex(e => e.MasterRecordId, "ix_le_record60_master");

            entity.Property(e => e.CkycNumber).HasMaxLength(14);
            entity.Property(e => e.ControllingInterest).HasMaxLength(50);
            entity.Property(e => e.CustomerId).HasMaxLength(50);
            entity.Property(e => e.Din).HasMaxLength(8);
            entity.Property(e => e.OtherRelationName).HasMaxLength(33);
            entity.Property(e => e.PercentageOwnership).HasMaxLength(10);
            entity.Property(e => e.Relation).HasMaxLength(60);
        });

        modelBuilder.Entity<LegalEntityRecord70>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__legal_en__3214EC074004AB60");

            entity.ToTable("legal_entity_record_70");

            entity.HasIndex(e => e.CustomerId, "ix_le_record70_customer");

            entity.HasIndex(e => e.MasterRecordId, "ix_le_record70_master");

            entity.Property(e => e.AttestationDate).HasMaxLength(10);
            entity.Property(e => e.CertifiedCopies).HasMaxLength(1);
            entity.Property(e => e.ConsentDocument).HasMaxLength(125);
            entity.Property(e => e.CustomerId).HasMaxLength(50);
            entity.Property(e => e.DeclarationDate).HasMaxLength(10);
            entity.Property(e => e.DeclarationDocument).HasMaxLength(125);
            entity.Property(e => e.DeclarationFlag).HasMaxLength(1);
            entity.Property(e => e.EmployeeBranch).HasMaxLength(50);
            entity.Property(e => e.EmployeeCkycId).HasMaxLength(14);
            entity.Property(e => e.EmployeeCode).HasMaxLength(50);
            entity.Property(e => e.EmployeeDesignation).HasMaxLength(50);
            entity.Property(e => e.EmployeeName).HasMaxLength(99);
            entity.Property(e => e.EquivalentEdoc)
                .HasMaxLength(1)
                .HasColumnName("EquivalentEDoc");
            entity.Property(e => e.InstitutionCode).HasMaxLength(50);
            entity.Property(e => e.InstitutionName).HasMaxLength(99);
            entity.Property(e => e.Place).HasMaxLength(40);
            entity.Property(e => e.Remarks).HasMaxLength(200);
            entity.Property(e => e.VerificationFromDigiLocker).HasMaxLength(1);
        });

        modelBuilder.Entity<MasterRecord>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__master_r__3214EC07BC9370B9");

            entity.ToTable("master_record");

            entity.HasIndex(e => new { e.BatchFile, e.BatchRecordLine }, "ix_master_batchline");

            entity.HasIndex(e => e.CustomerId, "ix_master_customer_id");

            entity.HasIndex(e => new { e.Status, e.RetryCount, e.LastActivity, e.NextRetryAt }, "ix_master_retry_picker");

            entity.HasIndex(e => new { e.Status, e.Id }, "ix_master_status_id");

            entity.Property(e => e.BatchFile).HasMaxLength(260);
            entity.Property(e => e.ClientType).HasMaxLength(1);
            entity.Property(e => e.CustomerId).HasMaxLength(50);
            entity.Property(e => e.LastActivity).HasMaxLength(50);
            entity.Property(e => e.LastError).HasMaxLength(1000);
            entity.Property(e => e.LastResponseAckNumber).HasMaxLength(10);
            entity.Property(e => e.LastResponseCkycNumber).HasMaxLength(15);
            entity.Property(e => e.LastResponseCkycReference).HasMaxLength(15);
            entity.Property(e => e.LastResponseFileName).HasMaxLength(260);
            entity.Property(e => e.LastResponseRejectionRemark).HasMaxLength(500);
            entity.Property(e => e.LastResponseRemarks).HasMaxLength(1000);
            entity.Property(e => e.LastResponseStatus).HasMaxLength(2);
            entity.Property(e => e.ReconRemarks).HasMaxLength(1000);
            entity.Property(e => e.ReconStatus).HasMaxLength(50);
            entity.Property(e => e.Remarks).HasMaxLength(500);
            entity.Property(e => e.StatusCode).HasMaxLength(3);
        });

        modelBuilder.Entity<MasterRecordAttempt>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__master_r__3214EC0799BEEDE1");

            entity.ToTable("master_record_attempt");

            entity.HasIndex(e => e.MasterRecordId, "ix_master_attempt_master");

            entity.Property(e => e.CustomerId).HasMaxLength(50);
            entity.Property(e => e.Error).HasMaxLength(1000);
            entity.Property(e => e.Remarks).HasMaxLength(1000);
            entity.Property(e => e.Stage).HasMaxLength(50);
        });

        modelBuilder.Entity<MasterRecordBatch>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__master_r__3214EC0749135185");

            entity.ToTable("master_record_batch");

            entity.HasIndex(e => new { e.CustomerId, e.BatchedAt }, "ix_master_record_batch_customer");

            entity.HasIndex(e => new { e.BatchFile, e.Record20LineNumber }, "ix_master_record_batch_fileline");

            entity.HasIndex(e => e.MasterRecordId, "ix_master_record_batch_master");

            entity.Property(e => e.BatchFile).HasMaxLength(260);
            entity.Property(e => e.CustomerId).HasMaxLength(50);
        });

        modelBuilder.Entity<MasterRecordReattempt>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__master_r__3214EC0743CD2CCC");

            entity.ToTable("master_record_reattempt");

            entity.HasIndex(e => e.MasterRecordId, "ix_master_reattempt_master");

            entity.Property(e => e.CustomerId).HasMaxLength(50);
            entity.Property(e => e.PreviousReconStatus).HasMaxLength(50);
            entity.Property(e => e.PreviousResponseAckNumber).HasMaxLength(10);
            entity.Property(e => e.PreviousResponseCkycNumber).HasMaxLength(15);
            entity.Property(e => e.PreviousResponseCkycReference).HasMaxLength(15);
            entity.Property(e => e.PreviousResponseRejectionRemark).HasMaxLength(500);
            entity.Property(e => e.PreviousResponseStatus).HasMaxLength(2);
            entity.Property(e => e.Reason).HasMaxLength(1000);
        });

        modelBuilder.Entity<MasterRecordResponse>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__master_r__3214EC07C7E8E148");

            entity.ToTable("master_record_response");

            entity.HasIndex(e => e.MasterRecordId, "ix_master_response_master");

            entity.Property(e => e.AckNumber).HasMaxLength(10);
            entity.Property(e => e.BatchFile).HasMaxLength(260);
            entity.Property(e => e.CkycNumber).HasMaxLength(15);
            entity.Property(e => e.CkycReferenceNumber).HasMaxLength(15);
            entity.Property(e => e.CustomerId).HasMaxLength(50);
            entity.Property(e => e.RawData).HasMaxLength(4000);
            entity.Property(e => e.RecordStatus).HasMaxLength(2);
            entity.Property(e => e.RejectionRemark).HasMaxLength(500);
            entity.Property(e => e.Remarks).HasMaxLength(1000);
            entity.Property(e => e.ResponseFileName).HasMaxLength(260);
        });

        modelBuilder.Entity<SearchBatch>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__search_b__3214EC07F804FC68");

            entity.ToTable("search_batch");

            entity.HasIndex(e => new { e.BusinessDate, e.FileSequence }, "ix_search_batch_date");

            entity.HasIndex(e => e.FileName, "ix_search_batch_file");

            entity.Property(e => e.ClaimToken).HasMaxLength(36);
            entity.Property(e => e.Error).HasMaxLength(2000);
            entity.Property(e => e.FileName).HasMaxLength(260);
            entity.Property(e => e.FilePath).HasMaxLength(1000);
            entity.Property(e => e.FvuHash).HasMaxLength(128);
            entity.Property(e => e.FvuZipPath).HasMaxLength(1000);
        });

        modelBuilder.Entity<SearchRequest>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__search_r__3214EC07C85B6592");

            entity.ToTable("search_request");

            entity.HasIndex(e => e.ClaimToken, "ix_search_request_claim");

            entity.HasIndex(e => new { e.OutputFileName, e.OutputLineNumber }, "ix_search_request_output");

            entity.HasIndex(e => new { e.ProcessingStatus, e.Id }, "ix_search_request_status");

            entity.Property(e => e.ClaimToken).HasMaxLength(36);
            entity.Property(e => e.ClientType).HasMaxLength(1);
            entity.Property(e => e.Constitution).HasMaxLength(1);
            entity.Property(e => e.CustomerId).HasMaxLength(50);
            entity.Property(e => e.DateOfBirth).HasMaxLength(10);
            entity.Property(e => e.DateOfIncorporation).HasMaxLength(10);
            entity.Property(e => e.ExternalRequestId).HasMaxLength(50);
            entity.Property(e => e.FirstName).HasMaxLength(33);
            entity.Property(e => e.Gender).HasMaxLength(1);
            entity.Property(e => e.IdentityTypeAndNumber).HasMaxLength(2000);
            entity.Property(e => e.LastCkycReference).HasMaxLength(15);
            entity.Property(e => e.LastError).HasMaxLength(2000);
            entity.Property(e => e.LastName).HasMaxLength(33);
            entity.Property(e => e.LastResponseRemark).HasMaxLength(250);
            entity.Property(e => e.LastSearchKey).HasMaxLength(20);
            entity.Property(e => e.LegalEntityName).HasMaxLength(99);
            entity.Property(e => e.MiddleName).HasMaxLength(33);
            entity.Property(e => e.MobileNumber).HasMaxLength(10);
            entity.Property(e => e.OutputFileName).HasMaxLength(260);
            entity.Property(e => e.PhotoReferenceNumber).HasMaxLength(40);
            entity.Property(e => e.Relation).HasMaxLength(50);
            entity.Property(e => e.RelationFirstName).HasMaxLength(33);
            entity.Property(e => e.RelationLastName).HasMaxLength(33);
            entity.Property(e => e.RelationMiddleName).HasMaxLength(33);
            entity.Property(e => e.ResponseStatus).HasMaxLength(50);
            entity.Property(e => e.VerifiableCredential).HasMaxLength(50);
        });

        modelBuilder.Entity<SearchResponse>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__search_r__3214EC07A40A0AAC");

            entity.ToTable("search_response");

            entity.HasIndex(e => e.SearchRequestId, "ix_search_response_request");

            entity.Property(e => e.AadhaarDocument).HasMaxLength(1);
            entity.Property(e => e.Cin).HasMaxLength(40);
            entity.Property(e => e.CkycReferenceNumber).HasMaxLength(15);
            entity.Property(e => e.ClientType).HasMaxLength(1);
            entity.Property(e => e.DeactivationReason).HasMaxLength(100);
            entity.Property(e => e.DisabilityDocument).HasMaxLength(1);
            entity.Property(e => e.DrivingLicenseDocument).HasMaxLength(1);
            entity.Property(e => e.EmailAddress).HasMaxLength(99);
            entity.Property(e => e.Filler1).HasMaxLength(1);
            entity.Property(e => e.Filler2).HasMaxLength(1);
            entity.Property(e => e.Filler3).HasMaxLength(1);
            entity.Property(e => e.Filler4).HasMaxLength(1);
            entity.Property(e => e.Filler5).HasMaxLength(1);
            entity.Property(e => e.Filler6).HasMaxLength(1);
            entity.Property(e => e.Filler7).HasMaxLength(1);
            entity.Property(e => e.Filler8).HasMaxLength(1);
            entity.Property(e => e.FirstName).HasMaxLength(33);
            entity.Property(e => e.ForeignJurisdictionDocument).HasMaxLength(1);
            entity.Property(e => e.Form6061Document).HasMaxLength(1);
            entity.Property(e => e.Gender).HasMaxLength(1);
            entity.Property(e => e.IncorporationDocument).HasMaxLength(1);
            entity.Property(e => e.LastName).HasMaxLength(33);
            entity.Property(e => e.LastUpdatedDate).HasMaxLength(10);
            entity.Property(e => e.LegalEntityName).HasMaxLength(150);
            entity.Property(e => e.MemorandumDocument).HasMaxLength(1);
            entity.Property(e => e.MiddleName).HasMaxLength(33);
            entity.Property(e => e.MobileNumber).HasMaxLength(12);
            entity.Property(e => e.NprDocument).HasMaxLength(1);
            entity.Property(e => e.NregaDocument).HasMaxLength(1);
            entity.Property(e => e.OtherDocument).HasMaxLength(1);
            entity.Property(e => e.PanDocument).HasMaxLength(1);
            entity.Property(e => e.PartnershipDeed).HasMaxLength(1);
            entity.Property(e => e.PassportDocument).HasMaxLength(1);
            entity.Property(e => e.PhotoReference).HasMaxLength(20);
            entity.Property(e => e.RecordLevelHash).HasMaxLength(128);
            entity.Property(e => e.RegistrationCertificate).HasMaxLength(1);
            entity.Property(e => e.RegistrationDate).HasMaxLength(12);
            entity.Property(e => e.Remark).HasMaxLength(250);
            entity.Property(e => e.ResponseFileName).HasMaxLength(260);
            entity.Property(e => e.SearchByOvdNumber).HasMaxLength(15);
            entity.Property(e => e.SearchByOvdType).HasMaxLength(1);
            entity.Property(e => e.SearchKey).HasMaxLength(20);
            entity.Property(e => e.SupportingPoiDocument).HasMaxLength(1);
            entity.Property(e => e.TrustDeed).HasMaxLength(1);
            entity.Property(e => e.UtilityBillDocument).HasMaxLength(1);
            entity.Property(e => e.VoterIdDocument).HasMaxLength(1);
        });

        modelBuilder.Entity<SearchResponseFile>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__search_r__3214EC073AAF45C2");

            entity.ToTable("search_response_file");

            entity.HasIndex(e => e.SearchBatchId, "ix_search_response_file_batch");

            entity.HasIndex(e => e.SourceHash, "ix_search_response_file_hash");

            entity.Property(e => e.FiCode).HasMaxLength(6);
            entity.Property(e => e.Filler).HasMaxLength(50);
            entity.Property(e => e.RegionCode).HasMaxLength(11);
            entity.Property(e => e.ResponseFileName).HasMaxLength(260);
            entity.Property(e => e.ResponseTimestamp).HasMaxLength(20);
            entity.Property(e => e.SourceArchiveName).HasMaxLength(260);
            entity.Property(e => e.SourceHash).HasMaxLength(128);
        });

        modelBuilder.Entity<StatusMaster>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__status_m__3214EC0768E4503F");

            entity.ToTable("status_master");

            entity.HasIndex(e => e.Code, "ix_status_code");

            entity.HasIndex(e => e.StatusValue, "ix_status_value");

            entity.Property(e => e.Code).HasMaxLength(3);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.Name).HasMaxLength(50);
        });

        modelBuilder.Entity<UpdateBatch>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__update_b__3214EC071EBB4FDA");

            entity.ToTable("update_batch");

            entity.HasIndex(e => new { e.BusinessDate, e.ClientType, e.FileSequence }, "ix_update_batch_date");

            entity.HasIndex(e => e.FileName, "ix_update_batch_file");

            entity.Property(e => e.ClaimToken).HasMaxLength(36);
            entity.Property(e => e.ClientType).HasMaxLength(1);
            entity.Property(e => e.Error).HasMaxLength(2000);
            entity.Property(e => e.FileName).HasMaxLength(260);
            entity.Property(e => e.FilePath).HasMaxLength(1000);
            entity.Property(e => e.FvuHash).HasMaxLength(128);
            entity.Property(e => e.FvuZipPath).HasMaxLength(1000);
        });

        modelBuilder.Entity<UpdateRequest>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__update_r__3214EC07C8D0BCCA");

            entity.ToTable("update_request");

            entity.HasIndex(e => e.ClaimToken, "ix_update_request_claim");

            entity.HasIndex(e => new { e.OutputFileName, e.OutputLineNumber }, "ix_update_request_output");

            entity.HasIndex(e => new { e.ProcessingStatus, e.Id }, "ix_update_request_status");

            entity.HasIndex(e => new { e.ClientType, e.ProcessingStatus, e.Id }, "ix_update_request_picker")
                .IncludeProperties(e => e.ClaimedAt);

            entity.Property(e => e.CkycNumber).HasMaxLength(14);
            entity.Property(e => e.ClaimToken).HasMaxLength(36);
            entity.Property(e => e.ClientType).HasMaxLength(1);
            entity.Property(e => e.CustomerId).HasMaxLength(50);
            entity.Property(e => e.ExternalRequestId).HasMaxLength(50);
            entity.Property(e => e.LastAckNumber).HasMaxLength(20);
            entity.Property(e => e.LastError).HasMaxLength(2000);
            entity.Property(e => e.LastResponseRemark).HasMaxLength(500);
            entity.Property(e => e.LastResponseStatusCode).HasMaxLength(2);
            entity.Property(e => e.OutputBatchKey).HasMaxLength(60);
            entity.Property(e => e.OutputFileName).HasMaxLength(260);
            entity.Property(e => e.ResponseStatus).HasMaxLength(20);
        });

        modelBuilder.Entity<UpdateResponse>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__update_r__3214EC07BED76200");

            entity.ToTable("update_response");

            entity.Property(e => e.AckNumber).HasMaxLength(20);
            entity.Property(e => e.CkycNumber).HasMaxLength(15);
            entity.Property(e => e.RecordStatus).HasMaxLength(2);
            entity.Property(e => e.RejectionRemark).HasMaxLength(150);
            entity.Property(e => e.ResponseFileName).HasMaxLength(260);
        });

        modelBuilder.Entity<UpdateResponseFile>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__update_r__3214EC07EFB84357");

            entity.ToTable("update_response_file");

            entity.HasIndex(e => e.SourceHash, "ix_update_response_file_hash");

            entity.Property(e => e.ClientType).HasMaxLength(1);
            entity.Property(e => e.FiCode).HasMaxLength(6);
            entity.Property(e => e.Filler1).HasMaxLength(50);
            entity.Property(e => e.Filler2).HasMaxLength(50);
            entity.Property(e => e.RegionCode).HasMaxLength(11);
            entity.Property(e => e.ResponseFileName).HasMaxLength(260);
            entity.Property(e => e.ResponseTimestamp).HasMaxLength(30);
            entity.Property(e => e.SourceArchiveName).HasMaxLength(260);
            entity.Property(e => e.SourceHash).HasMaxLength(128);
        });

        modelBuilder.Entity<UploadResponseFile>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__upload_r__3214EC0778A7C646");

            entity.ToTable("upload_response_file");

            entity.HasIndex(e => new { e.SourceHash, e.ResponseFileName }, "ix_upload_response_file_identity");

            entity.Property(e => e.BatchFile).HasMaxLength(260);
            entity.Property(e => e.ResponseFileName).HasMaxLength(260);
            entity.Property(e => e.ResponseTimestamp).HasMaxLength(30);
            entity.Property(e => e.SourceArchiveName).HasMaxLength(260);
            entity.Property(e => e.SourceHash).HasMaxLength(128);
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
