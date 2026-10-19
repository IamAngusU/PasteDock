using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PasteDock.Core.Models;

namespace PasteDock.Core.Services;

public static partial class ClipFactory
{
    public static ClipEntry Create(ClipPayload payload, string sourceApp, DateTimeOffset now)
    {
        payload.Validate();
        var searchable = payload.GetSearchableText();
        var kind = Classify(payload);
        var title = kind switch
        {
            ClipKind.Image => $"Image · {payload.ImageWidth:N0} × {payload.ImageHeight:N0}",
            ClipKind.Files => payload.Files.Length == 1 ? FileName(payload.Files[0]) : $"{payload.Files.Length} files",
            _ => SingleLine(payload.Text ?? "Rich text", 100)
        };
        return new ClipEntry
        {
            Kind = kind,
            Title = string.IsNullOrWhiteSpace(title) ? "Whitespace" : title,
            Preview = Truncate(searchable, Limits.PreviewCharacters),
            SearchText = Truncate(searchable, Limits.SearchCharacters),
            SourceApp = Truncate(sourceApp, 100),
            IsSnippet = sourceApp.Equals("PasteDock", StringComparison.OrdinalIgnoreCase),
            CreatedAt = now,
            LastCopiedAt = now,
            ByteCount = payload.GetByteCount(),
            Fingerprint = Fingerprint(payload),
            ImageWidth = payload.ImageWidth,
            ImageHeight = payload.ImageHeight,
            HasOcr = !string.IsNullOrWhiteSpace(payload.OcrText)
        };
    }

    public static ClipKind Classify(ClipPayload payload)
    {
        if (payload.Files.Length > 0) return ClipKind.Files;
        if (payload.Png is not null) return ClipKind.Image;
        var text = payload.Text?.Trim() ?? "";
        if (ColorPattern().IsMatch(text)) return ClipKind.Color;
        if (!text.Contains('\n') && Uri.TryCreate(text, UriKind.Absolute, out var uri)
            && uri.Scheme is "https" or "http") return ClipKind.Link;
        if (text.Length <= 1_000_000 && text.Length >= 2 && text[0] is '{' or '[')
        {
            try { using var json = JsonDocument.Parse(text); return ClipKind.Json; }
            catch (JsonException) { }
        }
        if (CodePattern().IsMatch(Truncate(text, 8000))) return ClipKind.Code;
        return ClipKind.Text;
    }

    public static string Fingerprint(ClipPayload payload)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        // Length-prefixed, format-labelled fields prevent concatenation collisions.
        void Append(string label, byte[]? bytes)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(label));
            Span<byte> length = stackalloc byte[8];
            BinaryPrimitives.WriteInt64LittleEndian(length, bytes?.LongLength ?? -1);
            hash.AppendData(length);
            if (bytes is not null) hash.AppendData(bytes);
        }
        void Text(string label, string? value)
        {
            byte[]? bytes = value is null ? null : Encoding.UTF8.GetBytes(value);
            try { Append(label, bytes); }
            finally { if (bytes is not null) CryptographicOperations.ZeroMemory(bytes); }
        }
        Text("text", payload.Text);
        Text("html", payload.Html);
        Text("rtf", payload.Rtf);
        Append("png", payload.Png);
        for (var i = 0; i < payload.Files.Length; i++) Text($"file:{i}", payload.Files[i]);
        // OCR is derived metadata: adding OCR does not create a different clipboard identity.
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public static string[] NormalizeTags(IEnumerable<string> tags) => tags
        .Select(t => t.Trim().TrimStart('#').ToLowerInvariant())
        .Where(t => t.Length is > 0 and <= 32 && t.All(c => char.IsLetterOrDigit(c) || c is '-' or '_'))
        .Distinct(StringComparer.OrdinalIgnoreCase).Take(16).ToArray();

    public static string SingleLine(string text, int length) =>
        Truncate(text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim(), length);

    public static string Truncate(string text, int length)
    {
        if (text.Length <= length) return text;
        if (length > 0 && char.IsHighSurrogate(text[length - 1])) length--;
        return text[..length];
    }

    private static string FileName(string path) => path.TrimEnd('\\', '/').Split('\\', '/').LastOrDefault() ?? "File";

    [GeneratedRegex("^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")]
    private static partial Regex ColorPattern();

    [GeneratedRegex(@"(?m)(^\s*(using |import |export |function |class |public |private |const |let |def |SELECT |CREATE TABLE |#!/|<\?php)|=>|</[a-zA-Z][^>]*>)", RegexOptions.IgnoreCase, 100)]
    private static partial Regex CodePattern();
}
