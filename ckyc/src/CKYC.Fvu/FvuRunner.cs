using System.IO.Compression;
using System.Text;
using CKYC.Core.Abstractions;
using CKYC.Core.Configuration;
using CKYC.Core.Models;
using CKYC.Core.Spec;

namespace CKYC.Fvu;

/// <summary>
/// Selects the concrete FVU implementation. Uses the real FVU_RUN_UTILITY.exe when
/// <see cref="FvuSettings.UseRealFvu"/> is set; otherwise a deterministic local
/// simulation produces the same output contract (used where the EXE is unavailable).
/// </summary>
public sealed class FvuRunner : IFvuRunner
{
    private readonly FvuSettings _fvu;
    private readonly IFileHasher _hasher;

    public FvuRunner(FvuSettings fvu, IFileHasher hasher)
    {
        _fvu = fvu;
        _hasher = hasher;
    }

    public Task<FvuRunResult> RunAsync(GeneratedBatch batch, CancellationToken ct = default)
        => _fvu.UseRealFvu
            ? new CommandLineFvuRunner(_fvu).RunAsync(batch, ct)
            : new SimulatedFvuRunner(_fvu, _hasher).RunAsync(batch, ct);
}

/// <summary>Deterministic local stand-in for the FVU when the EXE is not available.</summary>
public sealed class SimulatedFvuRunner
{
    private readonly FvuSettings _fvu;
    private readonly IFileHasher _hasher;

    public SimulatedFvuRunner(FvuSettings fvu, IFileHasher hasher)
    {
        _fvu = fvu;
        _hasher = hasher;
    }

    public async Task<FvuRunResult> RunAsync(GeneratedBatch batch, CancellationToken ct = default)
    {
        await Task.Yield(); // keep API async-friendly

        var root = _fvu.WorkspaceRoot;
        var batchDir = Path.Combine(root, "runs", batch.BatchKey);
        var outputDir = Path.Combine(batchDir, "output");
        Directory.CreateDirectory(outputDir);

        var bytes = await File.ReadAllBytesAsync(batch.UploadFilePath, ct);
        var fileHash = _hasher.ComputeSha256(bytes);
        var validated = InjectSimulatedHashes(batch.UploadFilePath, fileHash);

        var outName = $"{batch.UploadFileName}.validated";
        var outPath = Path.Combine(outputDir, outName);
        await File.WriteAllTextAsync(outPath, validated, ct);

        var zipPath = Path.Combine(outputDir, $"{batch.UploadFileName}.zip");
        if (File.Exists(zipPath)) File.Delete(zipPath);
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            zip.CreateEntryFromFile(batch.UploadFilePath, batch.UploadFileName);
            zip.CreateEntryFromFile(outPath, outName);
        }

        var summary = new FvuSummary(1, 1, 0, null);
        return new FvuRunResult(batch.BatchKey, true, 0, true, summary, null, null, zipPath, fileHash, null, default);
    }

    private string InjectSimulatedHashes(string uploadPath, string fileHash)
    {
        var sb = new StringBuilder();
        foreach (var line in File.ReadAllLines(uploadPath))
        {
            if (string.IsNullOrWhiteSpace(line)) { sb.AppendLine(); continue; }
            var parts = line.Split('|');
            if (parts[0] == CkycRecords.Header)
            {
                // Record-10: add FVU version + record hash + file hash.
                var recordObj = string.Join('|', parts.Take(11));
                var recordHash = _hasher.ComputeSha256(Encoding.UTF8.GetBytes(recordObj));
                sb.AppendLine($"{recordObj}|V1.0|{recordHash}|{fileHash}");
            }
            else
            {
                var recordHash = _hasher.ComputeSha256(Encoding.UTF8.GetBytes(line));
                sb.AppendLine($"{line}|{recordHash}");
            }
        }
        return sb.ToString();
    }
}
