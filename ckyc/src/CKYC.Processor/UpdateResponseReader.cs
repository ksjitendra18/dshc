using System.IO.Compression;
using System.Text;
using CKYC.Core.Domain;

namespace CKYC.Processor;

/// <summary>
/// Reads bulk-update reply files (.UPD.RESm or a ZIP containing one). The layout is the
/// "Update_response" sheet shared by both vendor update workbooks:
///   • record 80 header — client type, FI code, region code, totals, timestamp, fillers;
///   • record 90 detail — line number, line number of the submitted record-20, ack number,
///     record status (02 No Match / 03 Rejected), CKYC number when matched, rejection remark
///     when rejected.
/// </summary>
internal static class UpdateResponseReader
{
    private static readonly string[] NewLines = ["\r\n", "\n"];

    public static async Task<IReadOnlyList<UpdateResponseImport>> ReadAsync(string path, string sourceHash, CancellationToken ct)
    {
        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = ZipFile.OpenRead(path);
            var entries = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name) && e.Name.Contains(".UPD.RES", StringComparison.OrdinalIgnoreCase)).ToList();
            if (entries.Count == 0) throw new InvalidDataException("Response ZIP does not contain a .UPD.RES file.");
            var result = new List<UpdateResponseImport>(entries.Count);
            foreach (var entry in entries)
            {
                await using var stream = entry.Open();
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                result.Add(Parse(await reader.ReadToEndAsync(ct), Path.GetFileName(path), sourceHash, entry.Name));
            }
            return result;
        }

        var content = await File.ReadAllTextAsync(path, Encoding.UTF8, ct);
        return new[] { Parse(content, Path.GetFileName(path), sourceHash, Path.GetFileName(path)) };
    }

    private static UpdateResponseImport Parse(string content, string archiveName, string sourceHash, string responseFileName)
    {
        var lines = content.Split(NewLines, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) throw new InvalidDataException($"Response file '{responseFileName}' is empty.");
        var headerFields = lines[0].Split('|');
        if (headerFields[0] != "80")
            throw new InvalidDataException($"Response file '{responseFileName}' has no valid record-80 header.");
        var header = new UpdateResponseHeader
        {
            ResponseFileName = Path.GetFileName(responseFileName),
            ResponseFileNumber = ResponseNumber(responseFileName),
            ClientType = At(headerFields, 1), FiCode = At(headerFields, 2), RegionCode = At(headerFields, 3),
            TotalRecords = Number(At(headerFields, 4)), TotalProcessed = Number(At(headerFields, 5)),
            RecordsUnderProcessing = Number(At(headerFields, 6)), RecordsFailed = Number(At(headerFields, 7)),
            ResponseTimestamp = At(headerFields, 8), Filler1 = At(headerFields, 9), Filler2 = At(headerFields, 10),
            RawHeaderData = lines[0],
        };
        if (!string.IsNullOrWhiteSpace(header.ResponseTimestamp))
        {
            // Normalise the DD-MM-YYYY(/timestamp) stamp to sortable order where parseable.
            if (DateTime.TryParseExact(header.ResponseTimestamp[..10], "dd-MM-yyyy",
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var stamp))
                header.ResponseTimestamp = stamp.ToString("dd-MM-yyyy") + header.ResponseTimestamp[10..];
        }
        var details = new List<UpdateResponseDetail>();
        foreach (var line in lines.Skip(1))
        {
            var fields = line.Split('|');
            if (fields.Length == 0 || fields[0] != "90") continue;
            if (fields.Length < 7)
                throw new InvalidDataException($"Response detail in '{responseFileName}' has fewer than 7 fields.");
            details.Add(new UpdateResponseDetail
            {
                LineNumber = Number(At(fields, 1)),
                InputRecord20LineNumber = Number(At(fields, 2)),
                AckNumber = At(fields, 3),
                RecordStatus = At(fields, 4),
                CkycNumber = At(fields, 5),
                RejectionRemark = At(fields, 6),
                RawResponseData = line,
            });
        }
        return new UpdateResponseImport(archiveName, sourceHash, InputFileName(responseFileName), header, details);
    }

    /// <summary>Strip the ".RESm" suffix from the reply name to obtain the submitted .UPD name.</summary>
    private static string InputFileName(string responseFileName)
    {
        var value = Path.GetFileName(responseFileName);
        var marker = value.IndexOf(".RES", StringComparison.OrdinalIgnoreCase);
        return marker > 0 ? value[..marker] : value;
    }

    private static int ResponseNumber(string responseFileName)
    {
        var marker = responseFileName.LastIndexOf(".RES", StringComparison.OrdinalIgnoreCase);
        if (marker < 0) return 0;
        var digits = new string(responseFileName[(marker + 4)..].TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var result) ? result : 0;
    }

    private static string?[] Values(string[] fields, int start, int count)
        => Enumerable.Range(start, count).Select(index => At(fields, index)).ToArray();

    private static string? At(string[] values, int index) => index < values.Length ? values[index] : null;

    private static int? Number(string? value) => int.TryParse(value, out var result) ? result : null;
}
