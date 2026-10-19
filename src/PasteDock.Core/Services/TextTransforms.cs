using System.Text;
using System.Text.Json;
using PasteDock.Core.Models;

namespace PasteDock.Core.Services;

public enum TextTransform { JsonPretty, JsonCompact, TrimLines, UniqueLines, SortLines, NormalizeLineEndings, Uppercase, Lowercase, UrlEncode, UrlDecode, Base64Encode, Base64Decode }

public static class TextTransforms
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static string Apply(string text, TextTransform transform)
    {
        if (Encoding.UTF8.GetByteCount(text) > Limits.MaxPayloadBytes)
            throw new InvalidOperationException("The text is too large to transform.");
        var result = transform switch
        {
            TextTransform.JsonPretty => FormatJson(text, true),
            TextTransform.JsonCompact => FormatJson(text, false),
            TextTransform.TrimLines => string.Join("\n", Lines(text).Select(l => l.Trim())),
            TextTransform.UniqueLines => string.Join("\n", Lines(text).Distinct(StringComparer.Ordinal)),
            TextTransform.SortLines => string.Join("\n", Lines(text).OrderBy(l => l, StringComparer.OrdinalIgnoreCase)),
            TextTransform.NormalizeLineEndings => text.Replace("\r\n", "\n").Replace('\r', '\n'),
            TextTransform.Uppercase => text.ToUpperInvariant(),
            TextTransform.Lowercase => text.ToLowerInvariant(),
            TextTransform.UrlEncode => Uri.EscapeDataString(text),
            TextTransform.UrlDecode => Uri.UnescapeDataString(text),
            TextTransform.Base64Encode => Convert.ToBase64String(StrictUtf8.GetBytes(text)),
            TextTransform.Base64Decode => StrictUtf8.GetString(Convert.FromBase64String(text)),
            _ => throw new ArgumentOutOfRangeException(nameof(transform))
        };
        if (Encoding.UTF8.GetByteCount(result) > Limits.MaxPayloadBytes)
            throw new InvalidOperationException("The transformed result exceeds the 64 MiB limit. The original was not changed.");
        return result;
    }

    private static string[] Lines(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    private static string FormatJson(string text, bool indent)
    {
        using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 128 });
        return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = indent, MaxDepth = 128 });
    }
}
