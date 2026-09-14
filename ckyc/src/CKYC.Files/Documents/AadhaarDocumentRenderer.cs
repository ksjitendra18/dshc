using CKYC.Core.Configuration;
using CKYC.Core.Domain;

namespace CKYC.Files.Documents;

/// <summary>
/// Renders the Aadhaar E-KYC report (the "EKYC Report" template) with one customer's details.
/// The template is the blank report image; all values are overlaid at fixed template coordinates.
/// </summary>
internal static class AadhaarDocumentRenderer
{
    // Native pixel coordinate space of the supplied Aadhaar template.
    private const double CoordinateWidth = 1095;
    private const double CoordinateHeight = 1549;

    // Photo frame in the template, inset slightly so the printed rounded border stays visible.
    private const float PhotoLeft = 100f;
    private const float PhotoTop = 214f;
    private const float PhotoWidth = 209f;
    private const float PhotoHeight = 207f;

    public static byte[] Render(Individual record, byte[] template, GeneratedDocumentSettings options, byte[]? photo = null)
    {
        var name = FullName(record.Name);
        var proof = record.Proofs.FirstOrDefault(p => string.Equals(p.OvdType, "E", StringComparison.OrdinalIgnoreCase));
        var maskedAadhaar = string.IsNullOrWhiteSpace(proof?.IdNumber)
            ? "XXXX XXXX"
            : $"XXXX XXXX {proof!.IdNumber!.Trim()}";
        var address = record.PermanentAddress ?? record.CurrentAddress ?? new AddressDetails();

        var heading = options.HeadingFontSize > 0 ? options.HeadingFontSize : 12f;
        var body = options.FontSize > 0 ? options.FontSize : 11f;
        var headingOffset = DocumentTemplateRenderer.BaselineOffset(heading);
        var bodyOffset = DocumentTemplateRenderer.BaselineOffset(body);

        var fields = new List<TemplateField>
        {
            new(name, 391, 470 - headingOffset, heading),
            new(maskedAadhaar, 390, 510 - bodyOffset, body),
            new(name, 391, 663 - headingOffset, heading),
            new(Gender(record.Gender), 391, 703 - bodyOffset, body),
            new(record.DateOfBirth ?? string.Empty, 392, 743 - bodyOffset, body),
            new(address.Line1, 393, 902 - bodyOffset, body),
            new(address.Line2, 392, 942 - bodyOffset, body),
            new(DashWhenEmpty(address.Line3), 392, 987 - bodyOffset, body),
            new("-", 393, 1027 - bodyOffset, body),
            new(address.City, 389, 1062 - bodyOffset, body),
            new(DashWhenEmpty(address.District), 391, 1107 - bodyOffset, body),
            new(IndianStates.Resolve(address.State), 391, 1143 - bodyOffset, body),
            new(address.PinCode, 393, 1183 - bodyOffset, body),
        };

        TemplateImage? image = null;
        if (options.OverlayPhoto && photo is { Length: > 0 })
            image = new TemplateImage(photo, PhotoLeft, PhotoTop, PhotoWidth, PhotoHeight);

        return DocumentTemplateRenderer.Render(template, CoordinateWidth, CoordinateHeight,
            options.FontFamily, options.TextColor, fields, image);
    }

    internal static string FullName(PersonName? name)
    {
        if (name is null) return string.Empty;
        return string.Join(' ', new[] { name.FirstName, name.MiddleName, name.LastName }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part.Trim()));
    }

    private static string Gender(string? gender) => (gender ?? string.Empty).Trim().ToUpperInvariant() switch
    {
        "M" or "MALE" => "M",
        "F" or "FEMALE" => "F",
        "T" or "TRANSGENDER" => "T",
        var other => other,
    };

    private static string DashWhenEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
}
