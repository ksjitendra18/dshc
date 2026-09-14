using CKYC.Core.Configuration;
using CKYC.Core.Domain;

namespace CKYC.Files.Documents;

/// <summary>
/// Renders the Annexure-1 "Client Consent Declaration" with one customer's details. The template
/// background is the supplied consent PDF rasterised once (see the committed template asset); the
/// values are overlaid at fixed PDF-point coordinates.
/// </summary>
internal static class ConsentDocumentRenderer
{
    // The consent template is a single A4 page; its coordinate space is A4 in points.
    private const double CoordinateWidth = 595.276;
    private const double CoordinateHeight = 841.89;

    // Ink-top positions (in points) of each blank on the template, pre-compensated for the
    // renderer's ascent offset so the value sits on the printed line.
    private const float NameTop = 89.1f;
    private const float RelationTop = 89.1f;
    private const float ReportingEntityTop = 99.7f;
    private const float AadhaarNoTop = 173.4f;
    private const float PanNoTop = 225.4f;
    private const float ClientNameTop = 486.6f;
    private const float ClientDateTop = 509.1f;
    private const float CheckTop = 176.4f;
    private const float PanCheckTop = 228.4f;

    public static byte[] Render(Individual record, byte[] template, GeneratedDocumentSettings options)
    {
        var name = AadhaarDocumentRenderer.FullName(record.Name);
        var relation = Relation(record);
        var reportingEntity = record.Other?.InstitutionName ?? string.Empty;
        var proof = record.Proofs.FirstOrDefault(p => string.Equals(p.OvdType, "E", StringComparison.OrdinalIgnoreCase));
        var aadhaar = proof?.IdNumber?.Trim() ?? string.Empty;
        var pan = record.Pan?.Trim() ?? string.Empty;
        var date = DeclarationDate(record);

        var size = options.FontSize > 0 ? options.FontSize : 10f;
        var small = Math.Max(8f, size - 1f);

        var fields = new List<TemplateField>
        {
            new(name, 125.3f, NameTop, size),
            new(relation, 405.6f, RelationTop, size),
            new(reportingEntity, 293.6f, ReportingEntityTop, size),
            new(aadhaar, 284.2f, AadhaarNoTop, size),
            new(pan, 284.2f, PanNoTop, size),
            new(name, 290f, ClientNameTop, size),
            new(date, 290f, ClientDateTop, size),
        };

        if (!string.IsNullOrWhiteSpace(aadhaar)) fields.Add(new("X", 160.4f, CheckTop, small));
        if (!string.IsNullOrWhiteSpace(pan)) fields.Add(new("X", 160.4f, PanCheckTop, small));

        return DocumentTemplateRenderer.Render(template, CoordinateWidth, CoordinateHeight,
            options.FontFamily, options.TextColor, fields);
    }

    private static string Relation(Individual record)
    {
        var candidate = FirstNonEmpty(
            AadhaarDocumentRenderer.FullName(record.FatherName),
            AadhaarDocumentRenderer.FullName(record.MotherName),
            AadhaarDocumentRenderer.FullName(record.SpouseName));
        return candidate;
    }

    private static string DeclarationDate(Individual record)
    {
        var stored = record.Other?.DeclarationDate;
        if (!string.IsNullOrWhiteSpace(stored)) return stored.Trim();
        return DateTime.Today.ToString("dd-MM-yyyy");
    }

    private static string FirstNonEmpty(params string[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
}
