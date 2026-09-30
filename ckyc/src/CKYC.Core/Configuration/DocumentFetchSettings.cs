namespace CKYC.Core.Configuration;

/// <summary>
/// Per-channel image/document fetching. After the CRM data is fetched (<c>store</c>) an
/// individual record must have its supporting image/document pulled from the channel's
/// source before it can move on to the customer search and batching. Each intake channel
/// (<c>app</c>, <c>beckyc</c>, …) can have its own source, because the image lives in a
/// different place per channel — for <c>beckyc</c> it is a folder on an SFTP server:
/// <c>&lt;basePath&gt;/&lt;dockey&gt;/&lt;file&gt;.txt</c>, where the <c>.txt</c> holds the image as
/// a base64 data URI that the fetch decodes.
/// <para>
/// The lookup key (the <c>dockey</c>) arrives with the daily customer-id source fetch
/// (step 1) and is stored on <c>master_record.DocumentKey</c>. When the source does not
/// supply one, the customer id is used as the key.
/// </para>
/// </summary>
public sealed class DocumentFetchSettings
{
    /// <summary>Master switch. When false, <c>store</c> skips the image step (legacy behaviour).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Working area for fetched files and (when <c>useRealSftp=false</c>) the local inbox that
    /// simulates the remote server. A relative path resolves against the process working directory.
    /// </summary>
    public string DownloadRoot { get; set; } = "runtime/document-fetch";

    /// <summary>Channel-name → source configuration (e.g. <c>beckyc</c>).</summary>
    public Dictionary<string, ChannelDocumentFetchSettings> Channels { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The configuration for an intake channel, matched case-insensitively; null when the
    /// channel has no source configured.
    /// </summary>
    public ChannelDocumentFetchSettings? ForChannel(string? channel)
    {
        if (string.IsNullOrWhiteSpace(channel)) return null;
        foreach (var (key, value) in Channels)
            if (string.Equals(key?.Trim(), channel.Trim(), StringComparison.OrdinalIgnoreCase))
                return value;
        return null;
    }

    /// <summary>True when the channel has an active image/document source.</summary>
    public bool IsConfiguredFor(string? channel)
    {
        if (!Enabled) return false;
        var settings = ForChannel(channel);
        return settings is { Enabled: true } && !settings.IsNone;
    }

    /// <summary>Resolves <see cref="DownloadRoot"/> against the process working directory.</summary>
    public string ResolveDownloadRoot()
        => Path.GetFullPath(string.IsNullOrWhiteSpace(DownloadRoot) ? "runtime/document-fetch" : DownloadRoot);
}

/// <summary>Image/document source for one intake channel.</summary>
public sealed class ChannelDocumentFetchSettings
{
    /// <summary>Per-channel switch; lets a channel be temporarily disabled without removing its config.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Source kind. <c>Sftp</c> is the implemented transport; <c>None</c> disables it.</summary>
    public string Kind { get; set; } = "Sftp";

    /// <summary>true fetches over SFTP; false reads from the local inbox (deterministic offline demo).</summary>
    public bool UseRealSftp { get; set; } = true;

    // ---- connection (SFTP) ----
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 22;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    /// <summary>Optional OpenSSH/PEM private key; when set it is used instead of the password.</summary>
    public string? PrivateKeyPath { get; set; }
    public string? PrivateKeyPassphrase { get; set; }

    /// <summary>Remote base folder, e.g. <c>x/y/z</c> (relative to the SFTP login folder).</summary>
    public string BasePath { get; set; } = string.Empty;

    /// <summary>Folder under <see cref="BasePath"/> for one record; <c>{dockey}</c> is replaced with the record's key.</summary>
    public string FolderPattern { get; set; } = "{dockey}";

    /// <summary>
    /// Simulated-transport inbox root. The fetch reads <c>&lt;LocalInboxPath&gt;/&lt;dockey&gt;/…</c>.
    /// Defaults to <c>&lt;downloadRoot&gt;/inbox/&lt;channel&gt;</c>.
    /// </summary>
    public string? LocalInboxPath { get; set; }

    /// <summary>Subprocess/connection timeout in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// The files to pull for a record. Each entry names the remote file inside the dockey folder
    /// and optionally the record slot/document name it should be published as. When empty, every
    /// supported image/PDF found in the folder is fetched under its own name.
    /// </summary>
    public List<ChannelDocumentSettings> Documents { get; set; } = new();

    public bool IsNone => string.Equals(Kind?.Trim(), "None", StringComparison.OrdinalIgnoreCase);

    /// <summary>Resolves the remote folder for a document key: <c>&lt;basePath&gt;/&lt;folderPattern&gt;</c>.</summary>
    public string ResolveRemoteFolder(string documentKey)
    {
        var folder = (FolderPattern ?? string.Empty).Replace("{dockey}", documentKey ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        var basePath = (BasePath ?? string.Empty).Trim().Trim('/');
        var parts = new[] { basePath, folder.Trim('/') }.Where(p => !string.IsNullOrEmpty(p));
        return string.Join('/', parts);
    }

    /// <summary>Resolves the simulated inbox folder for a record.</summary>
    public string ResolveLocalFolder(string downloadRoot, string channel, string documentKey)
    {
        var root = string.IsNullOrWhiteSpace(LocalInboxPath)
            ? Path.Combine(downloadRoot, "inbox", channel)
            : Path.GetFullPath(LocalInboxPath);
        return Path.Combine(root, documentKey ?? string.Empty);
    }
}

/// <summary>One file to pull from a channel's document source.</summary>
public sealed class ChannelDocumentSettings
{
    /// <summary>Set false to skip this entry without removing it.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Exact remote file name inside the dockey folder. Empty means "use <see cref="Pattern"/>".</summary>
    public string Remote { get; set; } = string.Empty;

    /// <summary>Glob (with <c>*</c> / <c>?</c>) matched against the remote file names inside the folder.</summary>
    public string Pattern { get; set; } = "*";

    /// <summary>
    /// File name to publish the fetched bytes under. Defaults to the slot's current record file
    /// name, then the remote basename.
    /// </summary>
    public string? Target { get; set; }

    /// <summary>Record document slot to point at the fetched file (e.g. <c>photoOfIndividual</c>).</summary>
    public string? Slot { get; set; }
}
