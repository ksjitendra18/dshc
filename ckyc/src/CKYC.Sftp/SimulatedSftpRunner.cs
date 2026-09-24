using CKYC.Core.Configuration;
using CKYC.Core.Models;

namespace CKYC.Sftp;

/// <summary>
/// Deterministic local stand-in for the SFTP utility, used when
/// <see cref="SftpSettings.UseRealSftp"/> is false (offline CI / no network). It still writes
/// the generated config.yaml so the wiring can be exercised, but performs no transfer:
/// upload reports the staged payloads, download reports nothing new.
/// </summary>
public sealed class SimulatedSftpRunner
{
    private readonly SftpSettings _sftp;
    private readonly SftpPaths _paths;

    public SimulatedSftpRunner(SftpSettings sftp, SftpPaths paths)
    {
        _sftp = sftp;
        _paths = paths;
    }

    public Task<SftpRunResult> RunAsync(SftpOperation operation, CancellationToken ct = default)
    {
        // Surface config-generation problems early, exactly as the real run would.
        SftpConfigGenerator.Write(_sftp, _paths);

        var files = operation == SftpOperation.Upload
            ? _paths.ListUploadPayloads().Select(s => s.Path).ToList()
            : new List<string>();

        var result = new SftpRunResult(operation, true, 0, true, "[simulated sftp]", null, _paths.ConfigPath, null, files, null);
        return Task.FromResult(result);
    }
}
