using CKYC.Core.Abstractions;
using CKYC.Core.Configuration;
using CKYC.Core.Models;

namespace CKYC.Sftp;

/// <summary>
/// Selects the concrete SFTP implementation. Uses the real <c>SFTPRunner.exe</c> when
/// <see cref="SftpSettings.UseRealSftp"/> is set; otherwise a deterministic local simulation
/// that performs no transfer (used where the EXE / network is unavailable).
/// </summary>
public sealed class SftpRunner : ISftpRunner
{
    private readonly SftpSettings _sftp;

    public SftpRunner(SftpSettings sftp, SftpPaths paths)
    {
        _sftp = sftp;
        Paths = paths;
    }

    public SftpPaths Paths { get; }

    public Task<SftpRunResult> UploadAsync(CancellationToken ct = default) => RunAsync(SftpOperation.Upload, ct);

    public Task<SftpRunResult> DownloadAsync(CancellationToken ct = default) => RunAsync(SftpOperation.Download, ct);

    private Task<SftpRunResult> RunAsync(SftpOperation operation, CancellationToken ct)
        => _sftp.UseRealSftp
            ? new CommandLineSftpRunner(_sftp, Paths).RunAsync(operation, ct)
            : new SimulatedSftpRunner(_sftp, Paths).RunAsync(operation, ct);
}
