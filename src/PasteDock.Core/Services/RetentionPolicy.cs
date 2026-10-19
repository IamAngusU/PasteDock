using PasteDock.Core.Models;

namespace PasteDock.Core.Services;

public sealed record RetentionPlan(IReadOnlyList<ClipEntry> Kept, IReadOnlyList<ClipEntry> Removed, bool ProtectedQuotaExceeded);

public static class RetentionPolicy
{
    public static RetentionPlan Plan(IEnumerable<ClipEntry> entries, AppSettings settings, DateTimeOffset now)
    {
        var all = entries.ToArray();
        var kept = new HashSet<Guid>(all.Select(e => e.Id));
        var removed = new List<ClipEntry>();
        long bytes = all.Sum(e => e.ByteCount);
        long maxBytes = settings.StorageLimitMb * 1024L * 1024;
        var candidates = all.Where(e => !e.IsProtected).OrderBy(e => e.LastCopiedAt).ToArray();
        foreach (var item in candidates)
        {
            var expired = settings.RetentionDays > 0 && item.LastCopiedAt < now.AddDays(-settings.RetentionDays);
            if (!expired && kept.Count <= settings.HistoryCapacity && bytes <= maxBytes) continue;
            kept.Remove(item.Id);
            removed.Add(item);
            bytes -= item.ByteCount;
        }
        return new RetentionPlan(all.Where(e => kept.Contains(e.Id)).ToArray(), removed,
            kept.Count > settings.HistoryCapacity || bytes > maxBytes);
    }
}
