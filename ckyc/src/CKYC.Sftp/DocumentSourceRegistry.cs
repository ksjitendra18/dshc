using CKYC.Core.Abstractions;
using CKYC.Core.Configuration;
using CKYC.Sftp.DocumentSources;

namespace CKYC.Sftp;

/// <summary>
/// Builds the per-channel <see cref="IDocumentSource"/> instances from
/// <see cref="DocumentFetchSettings"/>. Each intake channel (app / beckyc) gets its own
/// source, because the image lives in a different place per channel; only channels with a
/// usable configuration are registered.
/// </summary>
public sealed class DocumentSourceRegistry : IDocumentSourceRegistry
{
    private readonly Dictionary<string, IDocumentSource> _sources = new(StringComparer.OrdinalIgnoreCase);

    public DocumentSourceRegistry(DocumentFetchSettings settings)
    {
        if (!settings.Enabled) return;
        var downloadRoot = settings.ResolveDownloadRoot();

        foreach (var (channel, channelSettings) in settings.Channels)
        {
            if (string.IsNullOrWhiteSpace(channel) || channelSettings is null) continue;
            if (!channelSettings.Enabled || channelSettings.IsNone) continue;
            if (!string.Equals(channelSettings.Kind?.Trim(), "Sftp", StringComparison.OrdinalIgnoreCase)) continue;

            var source = new SftpDocumentSource(channel.Trim(), channelSettings, downloadRoot);
            if (source.IsConfigured) _sources[channel.Trim()] = source;
        }
    }

    public bool IsConfigured(string? channel) => Resolve(channel) is not null;

    public IDocumentSource? Resolve(string? channel)
        => !string.IsNullOrWhiteSpace(channel) && _sources.TryGetValue(channel.Trim(), out var source) ? source : null;
}
