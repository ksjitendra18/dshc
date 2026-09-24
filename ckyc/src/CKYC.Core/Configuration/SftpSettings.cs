using System.Globalization;

namespace CKYC.Core.Configuration;

/// <summary>
/// SFTP transport settings. The pipeline drives the CERSAI-provided
/// <c>SFTPRunner.exe</c> to push FVU-validated <c>.UPL</c> batches and pull processed
/// response files.
/// <para>
/// <see cref="Enabled"/> is the master switch. When it is false the FVU keeps writing to its
/// per-batch run folder (legacy behaviour) and the <c>sftp</c> commands refuse to run. When
/// it is true the FVU writes each validated individual/legal <c>.UPL</c> ZIP straight into
/// the matching, deterministic upload folder below, so the SFTP utility always scans a known
/// location.
/// </para>
/// </summary>
public sealed class SftpSettings
{
    /// <summary>Master switch. When false the SFTP commands are disabled and FVU output routing is unchanged.</summary>
    public bool Enabled { get; set; }

    /// <summary>Absolute or working-directory-relative path to <c>SFTPRunner.exe</c>.</summary>
    public string ExePath { get; set; } = @"D:\centralprocessing\vendor\SFTP_Utility_windows\SFTPRunner.exe";

    /// <summary>Working area for the generated <c>config.yaml</c>, the optional run archive.</summary>
    public string WorkspaceRoot { get; set; } = "runtime/sftp";

    /// <summary>
    /// Deterministic root the FVU writes validated <c>.UPL</c> ZIPs into. The FVU output
    /// folder for an individual / legal batch is <c>&lt;OutboundRoot&gt;\INDIVIDUAL</c> or
    /// <c>&lt;OutboundRoot&gt;\LEGAL_ENTITY</c> — the same folders the SFTP utility uploads from.
    /// </summary>
    public string OutboundRoot { get; set; } = "runtime/sftp/outbound";

    /// <summary>SFTP upload scan folder for individual batches. Defaults to <c>&lt;OutboundRoot&gt;\INDIVIDUAL</c>.</summary>
    public string? IndividualFolder { get; set; }

    /// <summary>SFTP upload scan folder for legal-entity batches. Defaults to <c>&lt;OutboundRoot&gt;\LEGAL_ENTITY</c>.</summary>
    public string? LegalEntityFolder { get; set; }

    /// <summary>Folder the utility downloads processed response ZIPs into.</summary>
    public string DownloadPath { get; set; } = "runtime/sftp/downloads";

    /// <summary>Folder the utility writes its Executive Summary PDF report into.</summary>
    public string ReportFolder { get; set; } = "runtime/sftp/reports";

    /// <summary>Optional explicit <c>config.yaml</c> path. Defaults to <c>&lt;WorkspaceRoot&gt;\config.yaml</c>.</summary>
    public string? ConfigPath { get; set; }

    /// <summary>Optional archive root for successfully uploaded files. Defaults to <c>&lt;WorkspaceRoot&gt;\archive</c>.</summary>
    public string? ArchiveRoot { get; set; }

    // ---- connection (mirrors the vendor config.yaml) ----
    public string Host { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    /// <summary>Financial Institution code sent as <c>upload.ficode</c>.</summary>
    public string FiCode { get; set; } = string.Empty;

    public SftpProxySettings Proxy { get; set; } = new();

    /// <summary>Subprocess timeout in seconds. The utility boots an embedded JVM, so allow slack.</summary>
    public int TimeoutSeconds { get; set; } = 600;

    /// <summary>true invokes the real <c>SFTPRunner.exe</c>; false uses a deterministic local simulation (no network).</summary>
    public bool UseRealSftp { get; set; } = true;

    /// <summary>Move uploaded <c>.UPL.zip</c> files out of the scan folders after a successful upload so they are not re-sent.</summary>
    public bool ArchiveUploadedFiles { get; set; } = true;
}

/// <summary>Optional internal HTTP proxy for the SFTP utility (<c>proxy</c> section of config.yaml).</summary>
public sealed class SftpProxySettings
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 18888;
    public string? Username { get; set; }
    public string? Password { get; set; }
}

/// <summary>A validated <c>.UPL.zip</c> staged in an SFTP upload folder, ready to push.</summary>
public sealed record SftpStagedFile(string Entity, string Folder, string Path, string FileName);

/// <summary>
/// Resolved, absolute SFTP folders. Shared by the FVU output routing (so the validated ZIP
/// lands in a deterministic location) and the SFTP runner (so it scans/uploads exactly those
/// folders). Relative settings are resolved against the current working directory.
/// </summary>
public sealed class SftpPaths
{
    public const string IndividualEntity = "INDIVIDUAL";
    public const string LegalEntityEntity = "LEGAL_ENTITY";

    public required string IndividualFolder { get; init; }
    public required string LegalEntityFolder { get; init; }
    public required string DownloadFolder { get; init; }
    public required string ReportFolder { get; init; }
    public required string ConfigPath { get; init; }
    public required string ArchiveRoot { get; init; }

    public static SftpPaths Resolve(SftpSettings settings)
    {
        var outbound = Path.GetFullPath(settings.OutboundRoot);
        var workspace = Path.GetFullPath(settings.WorkspaceRoot);

        return new SftpPaths
        {
            IndividualFolder = ResolveFolder(settings.IndividualFolder, Path.Combine(outbound, IndividualEntity)),
            LegalEntityFolder = ResolveFolder(settings.LegalEntityFolder, Path.Combine(outbound, LegalEntityEntity)),
            DownloadFolder = Path.GetFullPath(settings.DownloadPath),
            ReportFolder = Path.GetFullPath(settings.ReportFolder),
            ConfigPath = ResolveFolder(settings.ConfigPath, Path.Combine(workspace, "config.yaml")),
            ArchiveRoot = ResolveFolder(settings.ArchiveRoot, Path.Combine(workspace, "archive")),
        };
    }

    private static string ResolveFolder(string? value, string fallback)
        => Path.GetFullPath(string.IsNullOrWhiteSpace(value) ? fallback : value);

    /// <summary>
    /// The deterministic FVU upload folder for a batch file name
    /// (<c>I_*.UPL</c> → individual, <c>L_*.UPL</c> → legal entity); null for anything else
    /// (search <c>.SRC</c>, update <c>.UPD</c>, …) so their output routing is unchanged.
    /// </summary>
    public string? UploadFolderFor(string? uploadFileName)
    {
        if (string.IsNullOrWhiteSpace(uploadFileName)) return null;
        var name = Path.GetFileName(uploadFileName);
        if (!name.EndsWith(".UPL", StringComparison.OrdinalIgnoreCase)) return null;
        return char.ToUpperInvariant(name[0]) switch
        {
            'I' => IndividualFolder,
            'L' => LegalEntityFolder,
            _ => null,
        };
    }

    /// <summary>Lists the staged <c>.UPL.zip</c> payloads waiting to be pushed for both entity types.</summary>
    public IReadOnlyList<SftpStagedFile> ListUploadPayloads()
    {
        var staged = new List<SftpStagedFile>();
        Add(IndividualEntity, IndividualFolder);
        Add(LegalEntityEntity, LegalEntityFolder);
        return staged.OrderBy(s => s.Path, StringComparer.OrdinalIgnoreCase).ToList();

        void Add(string entity, string folder)
        {
            if (!Directory.Exists(folder)) return;
            foreach (var file in Directory.GetFiles(folder, "*.UPL.zip"))
                staged.Add(new SftpStagedFile(entity, folder, file, Path.GetFileName(file)));
        }
    }

    /// <summary>A unique timestamped archive folder for the given entity type.</summary>
    public string ArchiveFolderFor(string entity)
        => Path.Combine(ArchiveRoot, entity,
            DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture));
}
