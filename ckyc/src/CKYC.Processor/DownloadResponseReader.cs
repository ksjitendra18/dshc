using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using CKYC.Core.Abstractions;
using CKYC.Core.Domain;

namespace CKYC.Processor;

internal static class DownloadResponseReader
{
    public static async Task<IReadOnlyList<DownloadResponseImport>> ReadAsync(
        string path, IFileHasher hasher, CancellationToken ct)
    {
        var archiveName = Path.GetFileName(path);
        var sourceHash = hasher.ComputeSha256(path);
        if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            var content = await File.ReadAllTextAsync(path, Encoding.UTF8, ct);
            return [Parse(Path.GetFileName(path), content, archiveName, sourceHash, [], Path.GetFileName(path))];
        }

        using var zip = ZipFile.OpenRead(path);
        var responseEntries = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)
            && e.FullName.Contains(".DWN.RES", StringComparison.OrdinalIgnoreCase)).ToList();
        if (responseEntries.Count == 0)
            throw new InvalidDataException($"Download ZIP '{archiveName}' contains no .DWN.RES text file.");

        var artifacts = new List<DownloadArtifact>();
        foreach (var entry in zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name) && !responseEntries.Contains(e)))
        {
            await using var stream = entry.Open();
            var artifactHash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct));
            artifacts.Add(new DownloadArtifact(entry.FullName, entry.Name, entry.Length, artifactHash));
        }

        var result = new List<DownloadResponseImport>();
        foreach (var entry in responseEntries)
        {
            await using var stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var import = Parse(entry.Name, await reader.ReadToEndAsync(ct), archiveName, sourceHash, artifacts, entry.FullName);
            foreach (var related in zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)
                && e.Name.EndsWith(".DWL_RES.csv", StringComparison.OrdinalIgnoreCase)))
            {
                await using var relatedStream = related.Open();
                using var relatedReader = new StreamReader(relatedStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                AddLines(import, await relatedReader.ReadToEndAsync(ct), related.FullName);
            }
            result.Add(import);
        }
        return result;
    }

    private static DownloadResponseImport Parse(string name, string content, string archive, string hash,
        IReadOnlyList<DownloadArtifact> artifacts, string sourceEntryPath)
    {
        var rows = content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (rows.Length == 0) throw new InvalidDataException($"Download response '{name}' is empty.");
        var header = rows[0].Split('|');
        if (header.Length < 7 || At(header, 0) != "10")
            throw new InvalidDataException($"Download response '{name}' has no valid record-10 header.");

        var import = new DownloadResponseImport
        {
            ResponseFileName = name,
            ResponseFileNumber = ResponseNumber(name),
            FiCode = At(header, 1), RegionCode = At(header, 2), ClientType = At(header, 3),
            TotalRecords = Number(At(header, 4)), Version = At(header, 5), ResponseDate = At(header, 6),
            RawHeaderData = rows[0], SourceArchiveName = archive, SourceHash = hash,
            Artifacts = artifacts.ToList(),
        };
        AddLines(import, string.Join('\n', rows.Skip(1)), sourceEntryPath);
        return import;
    }

    private static void AddLines(DownloadResponseImport import, string content, string sourceEntryPath)
    {
        foreach (var row in content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = row.Split('|');
            var type = At(fields, 0);
            if (string.IsNullOrWhiteSpace(type)) continue;
            import.Lines.Add(new DownloadResponseLine(sourceEntryPath, type, Number(At(fields, 1)), Number(At(fields, 2)),
                type == "20" ? At(fields, 4) : null, row));
        }
    }

    private static int ResponseNumber(string name)
    {
        var marker = name.LastIndexOf(".RES", StringComparison.OrdinalIgnoreCase);
        if (marker < 0) return 0;
        var digits = new string(name[(marker + 4)..].TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var value) ? value : 0;
    }

    private static string? At(string[] values, int index) => index < values.Length ? values[index] : null;
    private static int? Number(string? value) => int.TryParse(value, out var result) ? result : null;
}
