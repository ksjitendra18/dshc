namespace CKYC.Core.Domain;

/// <summary>Metadata and content for one customer-owned CKYC supporting document.</summary>
public sealed record CustomerDocument(
    long Id,
    long MasterRecordId,
    long FileContentId,
    string OriginalFileName,
    string CanonicalFileName,
    string MediaType,
    string? DocumentKind,
    string SourceType,
    string? SourceReference,
    string Sha256,
    long ByteLength,
    byte[] Content,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>Source-neutral metadata supplied while importing a document stream.</summary>
public sealed record DocumentImport(
    long MasterRecordId,
    string FileName,
    string? DocumentKind,
    string SourceType,
    string? SourceReference);

