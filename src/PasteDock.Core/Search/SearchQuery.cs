using System.Globalization;
using System.Text;
using PasteDock.Core.Models;

namespace PasteDock.Core.Search;

public sealed record SearchQuery
{
    public string[] Terms { get; init; } = [];
    public string[] ExcludedTerms { get; init; } = [];
    public string[] Tags { get; init; } = [];
    public string[] ExcludedTags { get; init; } = [];
    public string[] ExcludedApps { get; init; } = [];
    public ClipKind[] ExcludedKinds { get; init; } = [];
    public ClipKind? Kind { get; init; }
    public string? App { get; init; }
    public bool PinnedOnly { get; init; }
    public bool FavoritesOnly { get; init; }
    public bool ProtectedOnly { get; init; }
    public bool OcrOnly { get; init; }
    public bool SnippetsOnly { get; init; }
    public bool HidePinned { get; init; }
    public bool HideFavorites { get; init; }
    public bool HideProtected { get; init; }
    public bool HideOcr { get; init; }
    public bool HideSnippets { get; init; }
    public int? MinCopies { get; init; }
    public DateTimeOffset? After { get; init; }
    public DateTimeOffset? Before { get; init; }
    public bool DeepSearch { get; init; }

    public static SearchQuery Parse(string? input)
    {
        var terms = new List<string>(); var excluded = new List<string>();
        var tags = new List<string>(); var excludedTags = new List<string>();
        var excludedApps = new List<string>(); var excludedKinds = new List<ClipKind>();
        var query = new SearchQuery();
        foreach (var token in Tokenize((input ?? "")[..Math.Min(input?.Length ?? 0, 2048)]))
        {
            var part = token.Negated ? token.Value[1..] : token.Value;
            var target = token.Negated ? excluded : terms;
            if (token.Literal) { if (part.Length > 0) target.Add(part); continue; }
            if (part.StartsWith('#') && part.Length > 1)
            { (token.Negated ? excludedTags : tags).Add(part[1..]); continue; }
            if (part.StartsWith("app:", StringComparison.OrdinalIgnoreCase) && part.Length > 4)
            {
                if (token.Negated) excludedApps.Add(part[4..]);
                else query = query with { App = part[4..] };
                continue;
            }
            if (part.Equals("is:pinned", StringComparison.OrdinalIgnoreCase))
            { query = token.Negated ? query with { HidePinned = true } : query with { PinnedOnly = true }; continue; }
            if (part.Equals("is:favorite", StringComparison.OrdinalIgnoreCase))
            { query = token.Negated ? query with { HideFavorites = true } : query with { FavoritesOnly = true }; continue; }
            if (part.Equals("is:protected", StringComparison.OrdinalIgnoreCase))
            { query = token.Negated ? query with { HideProtected = true } : query with { ProtectedOnly = true }; continue; }
            if (part.Equals("has:ocr", StringComparison.OrdinalIgnoreCase))
            { query = token.Negated ? query with { HideOcr = true } : query with { OcrOnly = true }; continue; }
            if (part.Equals("is:snippet", StringComparison.OrdinalIgnoreCase))
            { query = token.Negated ? query with { HideSnippets = true } : query with { SnippetsOnly = true }; continue; }
            if (!token.Negated && part.StartsWith("copies:", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(part[7..], NumberStyles.None, CultureInfo.InvariantCulture, out var copies)
                && copies is >= 1 and <= 1_000_000)
            { query = query with { MinCopies = copies }; continue; }
            if (part.StartsWith("type:", StringComparison.OrdinalIgnoreCase) && part.Length > 5 && char.IsLetter(part[5])
                && Enum.TryParse<ClipKind>(part[5..], true, out var kind) && Enum.IsDefined(kind))
            {
                if (token.Negated) excludedKinds.Add(kind); else query = query with { Kind = kind };
                continue;
            }
            if (!token.Negated && part.StartsWith("after:", StringComparison.OrdinalIgnoreCase)
                && DateTimeOffset.TryParseExact(part[6..], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var after))
            { query = query with { After = after }; continue; }
            if (!token.Negated && part.StartsWith("before:", StringComparison.OrdinalIgnoreCase)
                && DateTimeOffset.TryParseExact(part[7..], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var before))
            { query = query with { Before = before }; continue; }
            target.Add(part);
        }
        return query with
        {
            Terms = terms.ToArray(), ExcludedTerms = excluded.ToArray(), Tags = tags.ToArray(),
            ExcludedTags = excludedTags.ToArray(), ExcludedApps = excludedApps.ToArray(), ExcludedKinds = excludedKinds.ToArray()
        };
    }

    public static string AppFilter(string app) => "app:\"" + app.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private sealed record Token(string Value, bool Negated, bool Literal);
    private static IEnumerable<Token> Tokenize(string input)
    {
        var token = new StringBuilder();
        var quoted = false; var literal = false; var startsQuoted = false;
        for (var index = 0; index < input.Length; index++)
        {
            var c = input[index];
            if (c == '"')
            {
                if (token.Length == 0) { literal = true; startsQuoted = true; }
                else if (token.Length == 1 && token[0] == '-') literal = true;
                quoted = !quoted; continue;
            }
            if (c == '\\' && quoted && index + 1 < input.Length && input[index + 1] is '"' or '\\')
            { token.Append(input[++index]); continue; }
            if (char.IsWhiteSpace(c) && !quoted)
            {
                if (token.Length > 0) yield return new(token.ToString(), !startsQuoted && token[0] == '-' && token.Length > 1, literal);
                token.Clear(); literal = startsQuoted = false;
            }
            else token.Append(c);
        }
        if (token.Length > 0) yield return new(token.ToString(), !startsQuoted && token[0] == '-' && token.Length > 1, literal);
    }
}
