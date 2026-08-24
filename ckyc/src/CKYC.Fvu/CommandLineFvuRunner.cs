using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using CKYC.Core.Configuration;
using CKYC.Core.Models;

namespace CKYC.Fvu;

/// <summary>
/// Real FVU integration: invokes FVU_RUN_UTILITY.exe as a subprocess with a generated
/// config.yaml, captures the JSON summary and exit code, locates the processed output
/// ZIP, and extracts the file-level hash from the validated record-10 header.
/// </summary>
public sealed class CommandLineFvuRunner
{
    private readonly FvuSettings _fvu;

    public CommandLineFvuRunner(FvuSettings fvu) => _fvu = fvu;

    public async Task<FvuRunResult> RunAsync(Core.Models.GeneratedBatch batch, CancellationToken ct = default)
    {
        var workspace = PrepareWorkspace(batch);

        try
        {
            var (exitCode, stdout, stderr) = await RunProcessAsync(workspace.ConfigPath, ct);

            var summary = TryParseSummary(stdout);
            var passed = exitCode == 0 && summary is { Success: > 0 };

            string? outputZip = FindOutputZip(workspace.OutputFolder, batch.UploadFileName);
            string? hash = null;

            if (outputZip is not null)
                hash = ExtractFileHash(outputZip);

            var errors = exitCode != 0 ? TryParseErrors(stdout, workspace.OutputFolder) : default;

            return new FvuRunResult(
                batch.BatchKey, true, exitCode, passed, summary, stdout, stderr,
                outputZip, hash,
                exitCode switch
                {
                    0 => null,
                    2 => "FVU configuration error",
                    3 => "One or more input files failed validation",
                    _ => "FVU fatal error",
                },
                errors);
        }
        catch (Exception ex)
        {
            return new FvuRunResult(batch.BatchKey, false, -1, false, null, null, ex.Message,
                null, null, ex.Message, default);
        }
    }

    private (string InputFolder, string OutputFolder, string LogFolder, string DocFolder, string ConfigPath) PrepareWorkspace(GeneratedBatch batch)
    {
        var root = _fvu.WorkspaceRoot;
        var batchDir = Path.Combine(root, "runs", batch.BatchKey);
        var inputDir = Path.Combine(batchDir, "input");
        var outputDir = Path.Combine(batchDir, "output");
        var logDir = Path.Combine(batchDir, "logs");
        var docDir = Path.Combine(batchDir, "support_docs");
        Directory.CreateDirectory(inputDir);
        Directory.CreateDirectory(outputDir);
        Directory.CreateDirectory(logDir);
        Directory.CreateDirectory(docDir);

        var src = batch.UploadFilePath;
        var dst = Path.Combine(inputDir, batch.UploadFileName);
        File.Copy(src, dst, overwrite: true);

        // Copy the batch's support_docs into this run's support_docs so the FVU's
        // SupportDocPath check succeeds.
        var batchSupport = Path.Combine(Path.GetDirectoryName(src) ?? ".", "support_docs");
        if (Directory.Exists(batchSupport))
        {
            foreach (var file in Directory.GetFiles(batchSupport))
                File.Copy(file, Path.Combine(docDir, Path.GetFileName(file)), overwrite: true);
        }

        var configPath = Path.Combine(batchDir, "config.yaml");
        var yaml = FvuConfigGenerator.Build(inputDir, docDir, outputDir, logDir, _fvu);
        File.WriteAllText(configPath, yaml, new UTF8Encoding(false));

        return (inputDir, outputDir, logDir, docDir, configPath);
    }

    private async Task<(int ExitCode, string StdOut, string StdErr)> RunProcessAsync(string configPath, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _fvu.ExePath,
            Arguments = $"-c \"{configPath}\"",
            WorkingDirectory = Path.GetDirectoryName(_fvu.ExePath)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        // Point the bundle extraction at a writable temp folder.
        var tmp = Path.Combine(_fvu.WorkspaceRoot, "tmp");
        Directory.CreateDirectory(tmp);
        psi.Environment["TMP"] = tmp;
        psi.Environment["TEMP"] = tmp;

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        var timeout = TimeSpan.FromSeconds(_fvu.RequestTimeoutSeconds + 120);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            throw new TimeoutException($"FVU process did not exit within {timeout.TotalSeconds}s.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        return (process.ExitCode, stdout, stderr);
    }

    private static FvuSummary? TryParseSummary(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout)) return null;
        var first = stdout.IndexOf('{');
        var last = stdout.LastIndexOf('}');
        if (first < 0 || last <= first) return null;
        var json = stdout.Substring(first, last - first + 1);
        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("totalFiles", out var tf)) return null;
            var total = tf.GetInt32();
            var success = root.GetProperty("success").GetInt32();
            var failed = root.GetProperty("failed").GetInt32();
            var pdf = root.TryGetProperty("summaryPdf", out var sp) ? sp.GetString() : null;
            return new FvuSummary(total, success, failed, pdf);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? FindOutputZip(string outputFolder, string inputFileName)
    {
        if (!Directory.Exists(outputFolder)) return null;
        var baseName = Path.GetFileNameWithoutExtension(inputFileName);
        return Directory.GetFiles(outputFolder, "*.zip")
            .FirstOrDefault(f => Path.GetFileName(f).StartsWith(baseName, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ExtractFileHash(string zipPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var entry = zip.Entries.FirstOrDefault(e => e.Name.EndsWith(".UPL", StringComparison.OrdinalIgnoreCase)
                                                        || e.Name.EndsWith(".UPD", StringComparison.OrdinalIgnoreCase));
            if (entry is null) return null;

            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            while (reader.ReadLine() is { } line)
            {
                if (!line.StartsWith("10|", StringComparison.Ordinal)) continue;
                var parts = line.Split('|');
                // Record-10 fields: [11] record-level hash, [12] file-level hash.
                if (parts.Length > 12 && !string.IsNullOrWhiteSpace(parts[12])) return parts[12];
                if (parts.Length > 11 && !string.IsNullOrWhiteSpace(parts[11])) return parts[11];
            }
        }
        catch
        {
            // best-effort
        }
        return null;
    }

    private static List<ValidationError>? TryParseErrors(string stdout, string outputFolder)
    {
        // Prefer the .ERR file written next to the input file.
        var errFile = Directory.Exists(outputFolder)
            ? Directory.GetFiles(outputFolder, "*.ERR").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            : null;
        if (errFile is not null)
        {
            var errs = new List<ValidationError>();
            foreach (var line in File.ReadAllLines(errFile))
            {
                var p = line.Split('|');
                if (p.Length < 6) continue;
                errs.Add(new ValidationError(
                    int.TryParse(p[0], out var sr) ? sr : null,
                    null, p[1],
                    p[2], p[3], p[4], p[5]));
            }
            if (errs.Count > 0) return errs;
        }

        var embedded = ExtractJsonErrors(stdout);
        return embedded?.Count > 0 ? embedded : null;
    }

    private static List<ValidationError>? ExtractJsonErrors(string stdout)
    {
        try
        {
            var first = stdout.IndexOf('{');
            var last = stdout.LastIndexOf('}');
            if (first < 0 || last <= first) return null;
            var doc = JsonDocument.Parse(stdout.Substring(first, last - first + 1));
            if (!doc.RootElement.TryGetProperty("errors", out var errors)) return null;
            if (!errors.TryGetProperty("errors", out var list)) return null;
            var result = new List<ValidationError>();
            foreach (var e in list.EnumerateArray())
            {
                result.Add(new ValidationError(
                    e.TryGetProperty("srNo", out var sr) && sr.TryGetInt32(out var sri) ? sri : null,
                    e.TryGetProperty("recordType", out var rt) ? rt.GetString() : null,
                    e.TryGetProperty("lineNumber", out var ln) ? ln.ToString() : null,
                    e.TryGetProperty("fieldName", out var fn) ? fn.GetString() : null,
                    e.TryGetProperty("fieldValue", out var fv) ? fv.GetString() : null,
                    e.TryGetProperty("errorCode", out var ec) ? ec.GetString() : null,
                    e.TryGetProperty("errorDescription", out var ed) ? ed.GetString() : null));
            }
            return result;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
