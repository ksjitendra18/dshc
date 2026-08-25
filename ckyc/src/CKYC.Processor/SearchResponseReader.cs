using System.IO.Compression;
using System.Text;
using CKYC.Core.Domain;

namespace CKYC.Processor;

internal static class SearchResponseReader
{
    private static readonly string[] NewLines = ["\r\n", "\n"];

    public static async Task<IReadOnlyList<SearchResponseImport>> ReadAsync(string path, string sourceHash, CancellationToken ct)
    {
        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = ZipFile.OpenRead(path);
            var entries = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name) && e.Name.Contains(".SRC.RES", StringComparison.OrdinalIgnoreCase)).ToList();
            if (entries.Count == 0) throw new InvalidDataException("Response ZIP does not contain a .SRC.RES file.");
            var result = new List<SearchResponseImport>(entries.Count);
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

    private static SearchResponseImport Parse(string content, string archiveName, string sourceHash, string responseFileName)
    {
        var lines = content.Split(NewLines, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) throw new InvalidDataException($"Response file '{responseFileName}' is empty.");
        var headerFields = lines[0].Split('|');
        if (headerFields.Length < 8 || headerFields[0] != "10")
            throw new InvalidDataException($"Response file '{responseFileName}' has no valid record-10 header.");
        var header = new SearchResponseHeader
        {
            ResponseFileName = Path.GetFileName(responseFileName),
            ResponseFileNumber = ResponseNumber(responseFileName),
            FiCode = At(headerFields, 1), RegionCode = At(headerFields, 2), TotalRecords = Number(At(headerFields, 3)),
            TotalProcessed = Number(At(headerFields, 4)), RecordsUnderProcessing = Number(At(headerFields, 5)),
            RecordsFailed = Number(At(headerFields, 6)), ResponseTimestamp = At(headerFields, 7), Filler = At(headerFields, 8),
            RawHeaderData = lines[0]
        };
        var details = new List<SearchResponseDetail>();
        foreach (var line in lines.Skip(1))
        {
            var fields = line.Split('|');
            if (fields.Length == 0 || fields[0] != "20") continue;
            if (fields.Length < 21) throw new InvalidDataException($"Response detail in '{responseFileName}' has fewer than 21 fields.");
            details.Add(new SearchResponseDetail
            {
                LineNumber = Number(At(fields, 1)), ClientType = At(fields, 2), InputRecordLineNumber = Number(At(fields, 3)),
                SearchByOvdType = At(fields, 4), SearchByOvdNumber = At(fields, 5), SearchKey = At(fields, 6),
                CkycReferenceNumber = At(fields, 7), FirstName = At(fields, 8), MiddleName = At(fields, 9),
                LastName = At(fields, 10), Gender = At(fields, 11), MobileNumber = At(fields, 12), EmailAddress = At(fields, 13),
                LastUpdatedDate = At(fields, 14), Cin = At(fields, 15), LegalEntityName = At(fields, 16),
                PhotoReference = At(fields, 17), RegistrationDate = At(fields, 18), DeactivationReason = At(fields, 19),
                Remark = At(fields, 20), DocumentFlags = Values(fields, 21, 18), Fillers = Values(fields, 39, 8),
                RecordLevelHash = At(fields, 47), RawResponseData = line
            });
        }
        return new SearchResponseImport(archiveName, sourceHash, InputFileName(responseFileName), header, details);
    }

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
