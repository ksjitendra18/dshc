namespace CKYC.Core.Models;

/// <summary>Result of the daily source fetch (step 1) into the master table.</summary>
public sealed record FetchResult(int Inserted, int Skipped, int Total);
public sealed record RetryResult(int Attempted, int Succeeded, int PermanentFailed, int Skipped);

/// <summary>Outcome of saving one individual record (step 3), including the simulated-error scenario.</summary>
public sealed record SaveRecordResult(long MasterRecordId, bool Success, string? Error, string? Summary);
public sealed record SaveBatchResult(int Succeeded, int Failed, int Total);

/// <summary>Identifies a generated batch .UPL file and its zip archive (step 4).</summary>
public sealed record GeneratedBatch(
    string BatchKey,
    string UploadFileName,
    string UploadFilePath,
    string? ZipPath,
    int RecordCount,
    DateTime CreatedAt,
    IReadOnlyList<SkippedRecord>? Skipped = null,
    IReadOnlyDictionary<string, int>? Record20Lines = null)
{
    public string DirectoryPath => Path.GetDirectoryName(UploadFilePath) ?? string.Empty;
    public int SkippedCount => Skipped?.Count ?? 0;
}

/// <summary>
/// A record that was excluded from the batch because it failed the conditional-mandatory
/// (CM) validation rules, together with the reasons it was rejected.
/// </summary>
public sealed record SkippedRecord(
    string CustomerId,
    string CustomerName,
    IReadOnlyList<ValidationError> Errors);

/// <summary>Parsed FVU standard-output JSON summary.</summary>
public sealed record FvuSummary(int TotalFiles, int Success, int Failed, string? SummaryPdf);

/// <summary>
/// Full outcome of a single FVU run (step 5), including where the processed output ZIP
/// and its hash can be found.
/// </summary>
public sealed record FvuRunResult(
    string BatchKey,
    bool Executed,
    int ExitCode,
    bool Passed,
    FvuSummary? Summary,
    string? StdOut,
    string? StdErr,
    string? OutputZipPath,
    string? Hash,
    string? ErrorMessage,
    IReadOnlyList<ValidationError>? ValidationErrors);

/// <summary>A single validation error returned by the FVU backend.</summary>
public sealed record ValidationError(
    int? SrNo,
    string? RecordType,
    string? LineNumber,
    string? FieldName,
    string? FieldValue,
    string? ErrorCode,
    string? ErrorDescription);
