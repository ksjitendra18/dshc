using System.Text.RegularExpressions;
using CKYC.Core.Models;
using CKYC.Core.Spec;

namespace CKYC.Files;

/// <summary>
/// Parses a CERSAI response (reply) file — the <c>*.UPL.RESm</c> output the FVU returns for a
/// submitted batch. The format (records 90 header + 100 detail) is documented in the
/// "Upload_response" sheet of File_Format_Upload_Individual_1.0.xlsx; the <c>m</c> in the file
/// name is the response-file number (0, 1, 2, ...), so a batch can produce several responses.
/// </summary>
public static class CkycResponseParser
{
    private static readonly Regex ResFileNumber = new(@"\.RES(?<n>\d+)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static CkycResponseFile Parse(string fileName, string content)
    {
        var fileNumber = ParseResponseFileNumber(fileName);
        ResponseHeader? header = null;
        var details = new List<ResponseDetail>();

        foreach (var line in content.Split('\n', '\r'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            var parts = trimmed.Split('|');
            if (parts.Length == 0) continue;

            var recordType = parts[0].Trim();
            if (recordType == CkycRecords.ResponseHeader)
                header = ParseHeader(fileName, parts);
            else if (recordType == CkycRecords.ResponseDetail)
                details.Add(ParseDetail(parts));
        }

        return new CkycResponseFile(fileName, fileNumber, header, details);
    }

    /// <summary>Extracts the response-file number (<c>m</c>) from a <c>*.UPL.RESm</c> name.</summary>
    public static int ParseResponseFileNumber(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return 0;
        var m = ResFileNumber.Match(fileName);
        return m.Success && int.TryParse(m.Groups["n"].Value, out var n) ? n : 0;
    }

    private static ResponseHeader ParseHeader(string fileName, string[] p)
    {
        if (p.Length < 9)
            throw new InvalidDataException($"Response file '{fileName}' has an incomplete record-90 header.");
        // Record 90 fields (0-based): [0] type, [1] client, [2] FI code, [3] region,
        // [4] total, [5] processed, [6] under processing, [7] failed, [8] timestamp.
        return new ResponseHeader(
            ParseInt(p, 4),
            ParseInt(p, 5),
            ParseInt(p, 6),
            ParseInt(p, 7),
            Get(p, 8));
    }

    private static ResponseDetail ParseDetail(string[] p)
    {
        if (p.Length < 8)
            throw new InvalidDataException("Response contains an incomplete record-100 detail.");
        // Record 100 fields (0-based): [0] type, [1] line no, [2] input record-20 line no,
        // [3] ack no, [4] record status, [5] CKYC reference no, [6] CKYC no, [7] rejection remark.
        return new ResponseDetail(
            ParseInt(p, 1),
            ParseNullableInt(p, 2),
            Get(p, 3),
            Get(p, 4),
            Get(p, 5),
            Get(p, 6),
            Get(p, 7));
    }

    private static int ParseInt(string[] parts, int index)
        => ParseNullableInt(parts, index) ?? 0;

    private static int? ParseNullableInt(string[] parts, int index)
    {
        var v = Get(parts, index);
        return int.TryParse(v, out var n) ? n : null;
    }

    private static string? Get(string[] parts, int index)
        => index < parts.Length ? parts[index].Trim() : null;
}
