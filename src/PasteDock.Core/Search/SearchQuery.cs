using System.Text;
using PasteDock.Core.Models;

namespace PasteDock.Core.Search;

public sealed record SearchQuery
{
    public string[] Terms { get; init; } = [];
    public string[] Tags { get; init; } = [];
    public ClipKind? Kind { get; init; }
    public string? App { get; init; }
    public bool PinnedOnly { get; init; }
    public bool FavoritesOnly { get; init; }
    public bool ProtectedOnly { get; init; }
    public bool OcrOnly { get; init; }
    public int? MinCopies { get; init; }
    public DateTimeOffset? After { get; init; }
    public DateTimeOffset? Before { get; init; }
    public bool DeepSearch { get; init; }

    public static SearchQuery Parse(string? input)
    {
        var terms = new List<string>();
        var tags = new List<string>();
        var query = new SearchQuery();
        foreach (var part in Tokenize((input ?? "")[..Math.Min(input?.Length ?? 0, 2048)]))
        {
            if (part.StartsWith('#') && part.Length > 1) { tags.Add(part[1..]); continue; }
            if (part.StartsWith("app:", StringComparison.OrdinalIgnoreCase)) { query = query with { App = part[4..] }; continue; }
            if (part.Equals("is:pinned", StringComparison.OrdinalIgnoreCase)) { query = query with { PinnedOnly = true }; continue; }
            if (part.Equals("is:favorite", StringComparison.OrdinalIgnoreCase)) { query = query with { FavoritesOnly = true }; continue; }
            if (part.Equals("is:protected", StringComparison.OrdinalIgnoreCase)) { query = query with { ProtectedOnly = true }; continue; }
            if (part.Equals("has:ocr", StringComparison.OrdinalIgnoreCase)) { query = query with { OcrOnly = true }; continue; }
            if (part.StartsWith("copies:", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(part[7..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var copies)
                && copies is >= 1 and <= 1_000_000)
            { query = query with { MinCopies = copies }; continue; }
            if (part.StartsWith("type:", StringComparison.OrdinalIgnoreCase)
                && Enum.TryParse<ClipKind>(part[5..], true, out var kind) && Enum.IsDefined(kind))
            { query = query with { Kind = kind }; continue; }
            if (part.StartsWith("after:", StringComparison.OrdinalIgnoreCase)
                && DateTimeOffset.TryParseExact(part[6..], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal, out var date))
            { query = query with { After = date }; continue; }
            if (part.StartsWith("before:", StringComparison.OrdinalIgnoreCase)
                && DateTimeOffset.TryParseExact(part[7..], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal, out var before))
            { query = query with { Before = before }; continue; }
            terms.Add(part);
        }
        return query with { Terms = terms.ToArray(), Tags = tags.ToArray() };
    }

    private static IEnumerable<string> Tokenize(string input)
    {
        var token = new StringBuilder();
        var quoted = false;
        foreach (var c in input)
        {
            if (c == '"') { quoted = !quoted; continue; }
            if (char.IsWhiteSpace(c) && !quoted)
            {
                if (token.Length > 0) { yield return token.ToString(); token.Clear(); }
            }
            else token.Append(c);
        }
        if (token.Length > 0) yield return token.ToString();
    }
}
