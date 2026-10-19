using PasteDock.Core.Models;

namespace PasteDock.Core.Search;

public static class SearchMatcher
{
    public static bool MatchesMetadata(ClipEntry entry, SearchQuery query) =>
        (!query.Kind.HasValue || entry.Kind == query.Kind)
        && (!query.PinnedOnly || entry.IsPinned)
        && (!query.FavoritesOnly || entry.IsFavorite)
        && (!query.ProtectedOnly || entry.IsProtected)
        && (!query.OcrOnly || entry.HasOcr)
        && (!query.MinCopies.HasValue || entry.CopyCount >= query.MinCopies.Value)
        && (query.App is null || entry.SourceApp.Contains(query.App, StringComparison.OrdinalIgnoreCase))
        && (!query.After.HasValue || entry.LastCopiedAt >= query.After)
        && (!query.Before.HasValue || entry.LastCopiedAt < query.Before)
        && query.Tags.All(tag => entry.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase));

    public static bool MatchesText(ClipEntry entry, SearchQuery query, string? fullText = null)
    {
        return query.Terms.All(term => Contains(entry.Title, term) || Contains(entry.SourceApp, term)
            || entry.Tags.Any(t => Contains(t, term)) || Contains(fullText ?? entry.SearchText, term));
    }

    public static int Score(ClipEntry entry, SearchQuery query) => query.Terms.Sum(term =>
        entry.Title.Equals(term, StringComparison.OrdinalIgnoreCase) ? 100 :
        entry.Title.StartsWith(term, StringComparison.OrdinalIgnoreCase) ? 50 :
        Contains(entry.Title, term) ? 25 : 1);

    private static bool Contains(string value, string term) => value.Contains(term, StringComparison.OrdinalIgnoreCase);
}
