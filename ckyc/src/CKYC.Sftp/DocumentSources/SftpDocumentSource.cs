using System.Text;
using System.Text.RegularExpressions;
using CKYC.Core.Abstractions;
using CKYC.Core.Configuration;
using Renci.SshNet;

namespace CKYC.Sftp.DocumentSources;

/// <summary>
/// Fetches a customer's image/supporting document from an SFTP server, addressed by the
/// step-1 document key (<c>dockey</c>): the remote folder is
/// <c>&lt;basePath&gt;/&lt;dockey&gt;</c> and the configured file(s) are downloaded from it.
/// <para>
/// When <see cref="ChannelDocumentFetchSettings.UseRealSftp"/> is false the same folder layout
/// is served from a local inbox (<c>&lt;downloadRoot&gt;/inbox/&lt;channel&gt;/&lt;dockey&gt;</c>)
/// so the flow can be exercised without a server — mirroring the existing SFTP transport
/// simulation.
/// </para>
/// </summary>
public sealed class SftpDocumentSource : IDocumentSource
{
    private static readonly string[] SupportedExtensions = [".pdf", ".jpg", ".jpeg", ".png"];

    private readonly ChannelDocumentFetchSettings _settings;
    private readonly string _downloadRoot;

    public SftpDocumentSource(string channel, ChannelDocumentFetchSettings settings, string downloadRoot)
    {
        Channel = channel;
        _settings = settings;
        _downloadRoot = downloadRoot;
    }

    public string Channel { get; }

    public bool IsConfigured => _settings.Enabled
        && !_settings.IsNone
        && (!_settings.UseRealSftp || !string.IsNullOrWhiteSpace(_settings.Host));

    public async Task<IReadOnlyList<FetchedDocument>> FetchAsync(DocumentFetchRequest request, CancellationToken ct = default)
    {
        var key = string.IsNullOrWhiteSpace(request.DocumentKey) ? request.CustomerId : request.DocumentKey;
        return _settings.UseRealSftp
            ? await FetchRealAsync(key, ct).ConfigureAwait(false)
            : await FetchSimulatedAsync(key, ct).ConfigureAwait(false);
    }

    private Task<IReadOnlyList<FetchedDocument>> FetchRealAsync(string key, CancellationToken ct)
        => Task.Run<IReadOnlyList<FetchedDocument>>(() =>
        {
            using var client = CreateClient();
            client.Connect();
            try
            {
                var folder = _settings.ResolveRemoteFolder(key);
                if (!client.Exists(folder))
                    throw new DirectoryNotFoundException($"The SFTP folder for document key '{key}' does not exist: '{folder}'.");

                var names = client.ListDirectory(folder)
                    .Where(entry => !entry.IsDirectory && !entry.IsSymbolicLink)
                    .Select(entry => entry.Name)
                    .ToList();

                var selected = SelectFiles(names, key, folder);
                var documents = new List<FetchedDocument>();
                foreach (var name in selected)
                {
                    var remotePath = $"{folder.TrimEnd('/')}/{name}";
                    using var buffer = new MemoryStream();
                    client.DownloadFile(remotePath, buffer);
                    documents.Add(new FetchedDocument(name, buffer.ToArray(), remotePath));
                }
                return documents;
            }
            finally
            {
                if (client.IsConnected) client.Disconnect();
            }
        }, ct);

    private Task<IReadOnlyList<FetchedDocument>> FetchSimulatedAsync(string key, CancellationToken ct)
        => Task.Run<IReadOnlyList<FetchedDocument>>(() =>
        {
            var folder = _settings.ResolveLocalFolder(_downloadRoot, Channel, key);
            if (!Directory.Exists(folder))
                throw new DirectoryNotFoundException($"The local document inbox for key '{key}' does not exist: '{folder}'.");

            var names = Directory.EnumerateFiles(folder)
                .Select(Path.GetFileName)
                .Where(name => name is not null)
                .Select(name => name!)
                .ToList();

            var selected = SelectFiles(names, key, folder);
            var documents = new List<FetchedDocument>();
            foreach (var name in selected)
            {
                var path = Path.Combine(folder, name);
                documents.Add(new FetchedDocument(name, File.ReadAllBytes(path), path));
            }
            return documents;
        }, ct);

    /// <summary>
    /// Chooses the files to fetch from the folder listing. An explicit <c>remote</c> that is
    /// missing throws (the record is blocked); otherwise the configured patterns (or, when no
    /// documents are configured, every supported file) select the payload.
    /// </summary>
    private List<string> SelectFiles(List<string> available, string key, string folder)
    {
        if (available.Count == 0)
            throw new FileNotFoundException($"No file was found for document key '{key}' in '{folder}'.");

        var selected = new List<string>();
        if (_settings.Documents.Count == 0)
        {
            foreach (var name in available.Where(IsSupported)) Add(name);
        }
        else
        {
            foreach (var document in _settings.Documents)
            {
                if (document.Enabled is false) continue;
                var remote = document.Remote?.Trim();
                if (!string.IsNullOrWhiteSpace(remote))
                {
                    var match = available.FirstOrDefault(name => string.Equals(name, remote, StringComparison.OrdinalIgnoreCase));
                    if (match is null)
                        throw new FileNotFoundException($"Expected file '{remote}' for document key '{key}' was not found in '{folder}'.");
                    Add(match);
                    continue;
                }

                var regex = GlobToRegex(string.IsNullOrWhiteSpace(document.Pattern) ? "*" : document.Pattern!);
                foreach (var name in available.Where(name => IsSupported(name) && regex.IsMatch(name))) Add(name);
            }
        }

        if (selected.Count == 0)
            throw new FileNotFoundException($"No matching supported document was found for key '{key}' in '{folder}'.");

        return selected;

        void Add(string name)
        {
            if (!selected.Contains(name, StringComparer.OrdinalIgnoreCase)) selected.Add(name);
        }
    }

    private static bool IsSupported(string name)
        => SupportedExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);

    private SftpClient CreateClient()
    {
        var username = _settings.Username ?? string.Empty;
        var methods = new List<AuthenticationMethod>();
        if (!string.IsNullOrWhiteSpace(_settings.PrivateKeyPath))
        {
            var keyPath = Path.GetFullPath(_settings.PrivateKeyPath!);
            var keyFile = string.IsNullOrEmpty(_settings.PrivateKeyPassphrase)
                ? new PrivateKeyFile(keyPath)
                : new PrivateKeyFile(keyPath, _settings.PrivateKeyPassphrase);
            methods.Add(new PrivateKeyAuthenticationMethod(username, keyFile));
        }
        else
        {
            methods.Add(new PasswordAuthenticationMethod(username, _settings.Password ?? string.Empty));
        }

        var connection = new ConnectionInfo(_settings.Host, _settings.Port <= 0 ? 22 : _settings.Port, username, methods.ToArray());
        return new SftpClient(connection)
        {
            OperationTimeout = TimeSpan.FromSeconds(Math.Max(1, _settings.TimeoutSeconds)),
        };
    }

    private static Regex GlobToRegex(string pattern)
    {
        var builder = new StringBuilder("^");
        foreach (var ch in pattern)
        {
            builder.Append(ch switch
            {
                '*' => ".*",
                '?' => ".",
                _ => Regex.Escape(ch.ToString()),
            });
        }
        builder.Append('$');
        return new Regex(builder.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
