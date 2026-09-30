using System.Text;

namespace CKYC.Sftp.DocumentSources;

/// <summary>
/// Normalizes document payloads that arrive wrapped as text. Some channel sources (beckyc)
/// publish an image as a single <c>data:&lt;mime&gt;;base64,&lt;payload&gt;</c> line inside a
/// <c>.txt</c> file instead of an image file.
/// <para>
/// The declared mime type is <b>not</b> trusted: some senders label a JPEG as
/// <c>image/png</c>. The real content type is sniffed from the decoded bytes so the file is
/// published under a name whose extension matches its content, which the document store
/// requires (<c>ValidateSignature</c>).
/// </para>
/// </summary>
internal static class EncodedDocumentContent
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Returns the document as it should be stored: a data-URI/base64 payload is decoded and
    /// renamed to the sniffed extension; anything else is returned unchanged.
    /// </summary>
    public static (string Name, byte[] Content) Normalize(string name, byte[] content)
    {
        if (content.Length == 0) return (name, content);
        if (TryReadText(content) is not { } text) return (name, content);
        if (!TryExtractBase64(text, name, out var payload, out var declaredMediaType)) return (name, content);

        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(payload);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException($"The document '{name}' contains a base64 payload that could not be decoded.", ex);
        }

        if (decoded.Length == 0)
            throw new InvalidDataException($"The document '{name}' decoded to empty content.");

        var extension = SniffExtension(decoded)
            ?? ExtensionForMediaType(declaredMediaType)
            ?? throw new InvalidDataException(
                $"The base64 document '{name}' decoded to an unsupported content type (only JPEG, PNG and PDF are supported).");

        return (Path.ChangeExtension(name, extension), decoded);
    }

    /// <summary>Reads the content as text only when every byte is printable ASCII/whitespace.</summary>
    private static string? TryReadText(byte[] content)
    {
        foreach (var b in content)
        {
            if (b is 0 or (> 0x0D and < 0x20) or > 0x7E) return null;
        }
        return Encoding.ASCII.GetString(content).Trim();
    }

    /// <summary>
    /// Pulls the base64 payload out of a data URI (<c>data:image/jpeg;base64,…</c>) or, for a
    /// <c>.txt</c> file, a bare base64 blob. Returns false when the text is neither.
    /// </summary>
    private static bool TryExtractBase64(string text, string name, out string payload, out string? declaredMediaType)
    {
        declaredMediaType = null;
        payload = string.Empty;

        var marker = text.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
        var dataPrefix = text.StartsWith("data:", StringComparison.OrdinalIgnoreCase);
        if (dataPrefix && marker >= 0)
        {
            var header = text[5..marker];
            var semicolon = header.IndexOf(';');
            declaredMediaType = (semicolon >= 0 ? header[..semicolon] : header).Trim();
            payload = StripWhitespace(text[(marker + "base64,".Length)..]);
            return payload.Length > 0;
        }

        // A bare base64 body is only meaningful for a .txt wrapper; require it to look like one.
        if (Path.GetExtension(name).Equals(".txt", StringComparison.OrdinalIgnoreCase))
        {
            var candidate = StripWhitespace(text);
            if (candidate.Length >= 16 && candidate.All(IsBase64Char))
            {
                payload = candidate;
                return true;
            }
        }

        return false;
    }

    private static string StripWhitespace(string value)
        => string.Concat(value.Where(ch => !char.IsWhiteSpace(ch)));

    private static bool IsBase64Char(char ch)
        => char.IsAsciiLetterOrDigit(ch) || ch is '+' or '/' or '=' or '-' or '_';

    private static string? SniffExtension(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return ".jpg";
        if (bytes.Length >= PngSignature.Length && bytes.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature)) return ".png";
        if (bytes.Length >= 5 && bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8)) return ".pdf";
        return null;
    }

    private static string? ExtensionForMediaType(string? mediaType) => mediaType?.Trim().ToLowerInvariant() switch
    {
        "image/jpeg" or "image/jpg" or "image/pjpeg" => ".jpg",
        "image/png" or "image/x-png" => ".png",
        "application/pdf" => ".pdf",
        _ => null,
    };
}
