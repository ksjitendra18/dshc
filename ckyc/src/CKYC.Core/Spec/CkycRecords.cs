namespace CKYC.Core.Spec;

/// <summary>
/// CKYC file-format constants: record-type codes and the batch file naming convention.
/// </summary>
public static class CkycRecords
{
    public const int MaxIndividualBatchRecords = 500;
    public const int MaxLegalEntityBatchRecords = 10;
    public const long MaxIndividualBytesPerCustomer = 500L * 1024L;
    public const long MaxLegalEntityBytesPerCustomer = 25L * 1024L * 1024L;
    public const long MaxLegalSmallDocumentBytes = 500L * 1024L;

    public const string Header = "10";
    public const string Demographic = "20";
    public const string Proof = "30";
    public const string Address = "40";
    public const string Contact = "50";
    public const string RelatedParty = "60";
    public const string Other = "70";

    // Response file records (used by the FVU output)
    public const string ResponseHeader = "90";
    public const string ResponseDetail = "100";
    public const string ReconHeader = "110";
    public const string ReconDetail = "120";
}

/// <summary>
/// Builds and parses the CKYC batch file name:
///   &lt;ClientType&gt;_&lt;UserID&gt;_&lt;FICODE&gt;_&lt;DDMMYYYY&gt;_&lt;nnnnn&gt;.&lt;ext&gt;
/// </summary>
public static class CkycFileName
{
    public static string Build(string clientType, string userId, string fiCode, DateOnly businessDate, int sequence, string extension)
        => $"{clientType}_{userId}_{fiCode}_{businessDate:ddMMyyyy}_{sequence:00000}.{extension.TrimStart('.')}";

    public static bool TryCreateClientTypeFromName(string fileName, out char clientType)
    {
        clientType = '\0';
        if (string.IsNullOrWhiteSpace(fileName)) return false;
        var first = fileName[0];
        if (first is 'I' or 'L') { clientType = first; return true; }
        return false;
    }
}
