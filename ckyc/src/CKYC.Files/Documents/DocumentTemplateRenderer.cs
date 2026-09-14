using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CKYC.Files.Documents;

/// <summary>One value to stamp onto a template: position/size are in the template's coordinate space.</summary>
internal readonly record struct TemplateField(string Text, float Left, float Top, float Size);

/// <summary>An image (e.g. the customer photo) stamped onto a template in template coordinates.</summary>
internal readonly record struct TemplateImage(byte[] Content, float Left, float Top, float Width, float Height);

/// <summary>
/// Stamps text values onto a template image and emits a single-page A4 PDF. The template image
/// is drawn full-bleed as the primary layer; every field is a separate overlay layer positioned
/// with <c>TranslateX/TranslateY</c>. QuestPDF renders the values as real vector text (not a
/// bitmap), so the output stays crisp at any OCR resolution.
/// </summary>
internal static class DocumentTemplateRenderer
{
    // A4 in PostScript points, the page size every generated document uses.
    private const float PageWidth = 595.276f;
    private const float PageHeight = 841.89f;

    public static byte[] Render(
        byte[] background,
        double coordinateWidth,
        double coordinateHeight,
        string fontFamily,
        string? textColor,
        IReadOnlyList<TemplateField> fields,
        TemplateImage? image = null)
    {
        QuestPdfBootstrap.Ensure();
        var scaleX = coordinateWidth > 0 ? PageWidth / (float)coordinateWidth : 1f;
        var scaleY = coordinateHeight > 0 ? PageHeight / (float)coordinateHeight : 1f;
        var color = string.IsNullOrWhiteSpace(textColor) ? "#101010" : textColor;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(0);
                page.Content().Layers(layers =>
                {
                    layers.PrimaryLayer().Image(background).FitArea();
                    if (image is { } picture && picture.Content.Length > 0)
                    {
                        var left = picture.Left * scaleX;
                        var top = picture.Top * scaleY;
                        var width = picture.Width * scaleX;
                        var height = picture.Height * scaleY;
                        // Mask the template's placeholder photo, then draw the customer photo centred
                        // and contained so it never distorts or overflows the printed frame.
                        layers.Layer().TranslateX(left).TranslateY(top).Width(width).Height(height).Background("#FFFFFF");
                        layers.Layer().TranslateX(left).TranslateY(top).Width(width).Height(height)
                            .AlignCenter().AlignMiddle().Image(picture.Content).FitArea();
                    }
                    foreach (var field in fields)
                    {
                        if (string.IsNullOrWhiteSpace(field.Text)) continue;
                        var text = field.Text;
                        var left = field.Left;
                        var top = field.Top;
                        var size = field.Size;
                        layers.Layer()
                            .TranslateX(left * scaleX)
                            .TranslateY(top * scaleY)
                            .Text(text)
                            .FontFamily(fontFamily)
                            .FontSize(size)
                            .FontColor(color)
                            // Keep the text layer faithful to the glyph shapes (no "ti"/"fi" ligature
                            // substitutions) so text extraction and OCR read the same characters.
                            .DisableFontFeature(FontFeatures.StandardLigatures);
                    }
                });
            });
        }).GeneratePdf();
    }

    /// <summary>
    /// Vertical nudge (in template units) that compensates for the font ascent gap between a
    /// text block's top and the visible glyph tops, calibrated against the sample templates.
    /// </summary>
    public static float BaselineOffset(float size) => size >= 12f ? 4f : 3f;
}

/// <summary>Applies the QuestPDF community licence exactly once per process.</summary>
internal static class QuestPdfBootstrap
{
    private static readonly object Gate = new();
    private static bool _initialized;

    public static void Ensure()
    {
        if (_initialized) return;
        lock (Gate)
        {
            if (_initialized) return;
            QuestPDF.Settings.License = LicenseType.Community;
            _initialized = true;
        }
    }
}
