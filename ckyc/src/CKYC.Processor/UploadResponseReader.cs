using System.IO.Compression;
using System.Text;
using CKYC.Core.Abstractions;
using CKYC.Core.Models;
using CKYC.Files;

namespace CKYC.Processor;

internal sealed record UploadResponseImport(
    string SourceArchiveName,
    string SourceHash,
    string ResponseFileName,
    string Content,
    CkycResponseFile Parsed);

internal static class UploadResponseReader
{
    public static async Task<IReadOnlyList<UploadResponseImport>> ReadAsync(
        string path, IFileHasher hasher, CancellationToken ct)
    {
        var sourceName = Path.GetFileName(path);
        var sourceHash = hasher.ComputeSha256(path);
        if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            var content = await File.ReadAllTextAsync(path, Encoding.UTF8, ct);
            var name = Path.GetFileName(path);
            return [new UploadResponseImport(sourceName, sourceHash, name, content, CkycResponseParser.Parse(name, content))];
        }

        using var zip = ZipFile.OpenRead(path);
        var entries = zip.Entries
            .Where(e => !string.IsNullOrEmpty(e.Name) && e.Name.Contains(".UPL.RES", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (entries.Count == 0)
            throw new InvalidDataException($"Response ZIP '{sourceName}' does not contain a .UPL.RES file.");

        var result = new List<UploadResponseImport>(entries.Count);
        foreach (var entry in entries)
        {
            await using var stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var content = await reader.ReadToEndAsync(ct);
            result.Add(new UploadResponseImport(sourceName, sourceHash, entry.Name, content,
                CkycResponseParser.Parse(entry.Name, content)));
        }
        return result;
    }

    public static string? InferUploadFileName(string path)
    {
        if (!File.Exists(path)) return null;
        var responseName = Path.GetFileName(path);
        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = ZipFile.OpenRead(path);
            responseName = zip.Entries.FirstOrDefault(e => !string.IsNullOrEmpty(e.Name)
                && e.Name.Contains(".UPL.RES", StringComparison.OrdinalIgnoreCase))?.Name;
        }

        if (string.IsNullOrEmpty(responseName)) return null;
        var marker = responseName.IndexOf(".RES", StringComparison.OrdinalIgnoreCase);
        return marker > 0 ? Path.GetFileName(responseName[..marker]) : null;
    }
}
