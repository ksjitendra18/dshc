using System.Diagnostics;
using CKYC.Core.Configuration;
using CKYC.Core.Models;

namespace CKYC.Sftp;

/// <summary>
/// Real SFTP integration: invokes <c>SFTPRunner.exe</c> as a child process in
/// <c>upload</c> or <c>download</c> mode with the generated <c>config.yaml</c>, captures the
/// exit code and console output, and locates the Executive Summary PDF produced by an upload.
///
/// Exit codes (see the vendor integration document V1.2):
/// <list type="bullet">
///   <item><description>0 — transfer completed successfully.</description></item>
///   <item><description>1 — configuration error (missing/invalid config.yaml).</description></item>
///   <item><description>3 — runtime exception (connection failed / JAR error).</description></item>
/// </list>
/// </summary>
public sealed class CommandLineSftpRunner
{
    private readonly SftpSettings _sftp;
    private readonly SftpPaths _paths;

    public CommandLineSftpRunner(SftpSettings sftp, SftpPaths paths)
    {
        _sftp = sftp;
        _paths = paths;
    }

    public async Task<SftpRunResult> RunAsync(SftpOperation operation, CancellationToken ct = default)
    {
        var staged = operation == SftpOperation.Upload ? _paths.ListUploadPayloads() : Array.Empty<SftpStagedFile>();
        var downloadBefore = operation == SftpOperation.Download ? ListDownloadFiles() : null;

        try
        {
            var exePath = ResolveExePath(_sftp.ExePath);
            if (exePath is null)
                return Failure(operation, $"SFTPRunner.exe was not found at '{_sftp.ExePath}'.", staged);

            Directory.CreateDirectory(_paths.IndividualFolder);
            Directory.CreateDirectory(_paths.LegalEntityFolder);
            Directory.CreateDirectory(_paths.DownloadFolder);
            Directory.CreateDirectory(_paths.ReportFolder);

            var configPath = SftpConfigGenerator.Write(_sftp, _paths);
            var mode = operation == SftpOperation.Upload ? "upload" : "download";
            var (exitCode, stdout, stderr) = await RunProcessAsync(exePath, mode, configPath, ct);

            var (passed, message) = MapExitCode(exitCode);
            var transferred = operation == SftpOperation.Upload
                ? staged.Select(s => s.Path).ToList()
                : ListDownloadFiles().Except(downloadBefore!, StringComparer.OrdinalIgnoreCase).ToList();

            return new SftpRunResult(operation, true, exitCode, passed, stdout, stderr, configPath,
                operation == SftpOperation.Upload ? FindLatestReport() : null, transferred, message);
        }
        catch (Exception ex)
        {
            return Failure(operation, ex.Message, staged);
        }
    }

    private SftpRunResult Failure(SftpOperation operation, string message, IReadOnlyList<SftpStagedFile> staged)
        => new(operation, false, -1, false, null, null, _paths.ConfigPath, null,
            operation == SftpOperation.Upload ? staged.Select(s => s.Path).ToList() : Array.Empty<string>(),
            message);

    private async Task<(int ExitCode, string StdOut, string StdErr)> RunProcessAsync(
        string exePath, string mode, string configPath, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = $"{mode} --config \"{configPath}\"",
            WorkingDirectory = Path.GetDirectoryName(exePath)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        var timeout = TimeSpan.FromSeconds(_sftp.TimeoutSeconds + 120);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            throw new TimeoutException($"SFTP utility did not exit within {timeout.TotalSeconds}s.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        return (process.ExitCode, stdout, stderr);
    }

    private static (bool Passed, string? Error) MapExitCode(int exitCode) => exitCode switch
    {
        0 => (true, null),
        1 => (false, "SFTP configuration error — verify config.yaml, credentials and folder paths."),
        3 => (false, "SFTP runtime error — connection failed or the utility JAR raised an exception."),
        _ => (false, $"SFTP utility exited with code {exitCode}."),
    };

    private IReadOnlyList<string> ListDownloadFiles()
        => Directory.Exists(_paths.DownloadFolder)
            ? Directory.GetFiles(_paths.DownloadFolder).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList()
            : Array.Empty<string>();

    private string? FindLatestReport()
    {
        if (!Directory.Exists(_paths.ReportFolder)) return null;
        return Directory.GetFiles(_paths.ReportFolder, "Upload_Report_*.pdf")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    /// <summary>Resolves the configured exe path against the working directory; null when it does not exist.</summary>
    private static string? ResolveExePath(string configured)
    {
        if (string.IsNullOrWhiteSpace(configured)) return null;
        var full = Path.GetFullPath(configured);
        return File.Exists(full) ? full : null;
    }
}
