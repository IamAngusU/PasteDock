using PasteDock.Core.Models;

namespace PasteDock.Core.Search;

public enum SearchSort { BestMatch, RecentlyCopied, MostCopied, Newest, Largest }

public static class SearchOrdering
{
    public static ClipEntry[] Apply(IEnumerable<ClipEntry> entries, SearchQuery query, SearchSort sort = SearchSort.BestMatch)
    {
        var pinned = entries.OrderByDescending(entry => entry.IsPinned);
        var ordered = sort switch
        {
            SearchSort.MostCopied => pinned.ThenByDescending(entry => entry.CopyCount),
            SearchSort.Newest => pinned.ThenByDescending(entry => entry.CreatedAt),
            SearchSort.Largest => pinned.ThenByDescending(entry => entry.ByteCount),
            SearchSort.RecentlyCopied => pinned.ThenByDescending(entry => entry.LastCopiedAt),
            _ => pinned.ThenByDescending(entry => SearchMatcher.Score(entry, query))
        };
        return ordered.ThenByDescending(entry => entry.LastCopiedAt).ThenBy(entry => entry.Id).ToArray();
    }
}
