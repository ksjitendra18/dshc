using CKYC.Core.Abstractions;
using CKYC.Core.Configuration;
using CKYC.Core.Domain;
using CKYC.Core.Models;

namespace CKYC.Files.Documents;

/// <summary>The generated documents (and any non-fatal problems) for one batch.</summary>
public sealed record DocumentGenerationResult(
    IReadOnlyList<GeneratedDocument> Documents,
    IReadOnlyList<string> Warnings)
{
    public static readonly DocumentGenerationResult Empty = new(Array.Empty<GeneratedDocument>(), Array.Empty<string>());
}

/// <summary>
/// Produces the per-channel supporting documents for a batch. For each record it renders the
/// documents configured for that record's intake channel (e.g. the BCE/<c>beckyc</c> channel gets
/// a filled Aadhaar EKYC report and client consent) plus any channel-neutral documents (the static
/// undertaking), publishes their file names onto the record's document slots, and returns the bytes
/// for injection into the batch. Nothing is written to the document database — generation is
/// deterministic from the record and the template, so a batch always reflects current data.
/// </summary>
public sealed class DocumentGenerationService
{
    private readonly DocumentGenerationSettings _settings;
    private readonly IDocumentStore _documents;

    public DocumentGenerationService(DocumentGenerationSettings settings, IDocumentStore documents)
    {
        _settings = settings;
        _documents = documents;
    }

    public async Task<DocumentGenerationResult> GenerateAsync(
        IReadOnlyDictionary<string, MasterRecord> mastersByCustomerId,
        IReadOnlyList<Individual> records,
        CancellationToken ct = default)
    {
        if (!_settings.Enabled || _settings.Documents.Count == 0 || records.Count == 0)
            return DocumentGenerationResult.Empty;

        var templateRoot = ResolveTemplateRoot(_settings.TemplateRoot);
        var documents = new List<GeneratedDocument>();
        var warnings = new List<string>();
        var templates = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var missingTemplates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in records)
        {
            if (!mastersByCustomerId.TryGetValue(record.CustomerId, out var master)) continue;
            var channel = MasterRecordSourceValue.For(master.Source);

            foreach (var document in _settings.Documents)
            {
                if (!document.Enabled) continue;
                if (!AppliesToChannel(document, channel)) continue;
                if (missingTemplates.Contains(document.Template)) continue;

                if (!templates.TryGetValue(document.Template, out var template))
                {
                    var path = Path.Combine(templateRoot, document.Template);
                    if (!File.Exists(path))
                    {
                        missingTemplates.Add(document.Template);
                        warnings.Add($"Document template not found: '{path}'.");
                        continue;
                    }
                    template = await File.ReadAllBytesAsync(path, ct);
                    templates[document.Template] = template;
                }

                byte[] content;
                try
                {
                    var photo = await ResolvePhotoAsync(document, record, master.Id, ct);
                    content = Render(document, template, record, photo);
                }
                catch (Exception ex)
                {
                    warnings.Add($"Failed to render '{document.FileName}' for '{record.CustomerId}': {ex.Message}");
                    continue;
                }

                RecordDocumentSlots.Apply(record, document.Slots, document.FileName, warnings);
                documents.Add(new GeneratedDocument(master.Id, document.FileName, document.Kind, content));
            }
        }

        return new DocumentGenerationResult(documents, warnings);
    }

    private static bool AppliesToChannel(GeneratedDocumentSettings document, string channel)
        => document.Channels.Count == 0
           || document.Channels.Any(c => string.Equals(c?.Trim(), channel, StringComparison.OrdinalIgnoreCase));

    private static byte[] Render(GeneratedDocumentSettings document, byte[] template, Individual record, byte[]? photo)
        => document.Kind.Trim().ToLowerInvariant() switch
        {
            "static" => template,
            "aadhaar" => AadhaarDocumentRenderer.Render(record, template, document, photo),
            "consent" => ConsentDocumentRenderer.Render(record, template, document),
            _ => throw new InvalidOperationException($"Unknown document kind '{document.Kind}'."),
        };

    /// <summary>Loads the record's photo from the document store for the Aadhaar EKYC report, if present.</summary>
    private async Task<byte[]?> ResolvePhotoAsync(GeneratedDocumentSettings document, Individual record, long masterRecordId, CancellationToken ct)
    {
        if (!string.Equals(document.Kind?.Trim(), "Aadhaar", StringComparison.OrdinalIgnoreCase)) return null;
        if (!document.OverlayPhoto || string.IsNullOrWhiteSpace(record.PhotoOfIndividual)) return null;

        var stored = await _documents.GetAsync(masterRecordId, record.PhotoOfIndividual, ct);
        return stored is not null && stored.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            ? stored.Content
            : null;
    }

    private static string ResolveTemplateRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) root = "doc_format";
        return Path.IsPathRooted(root) ? root : Path.GetFullPath(root);
    }
}
