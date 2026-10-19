namespace PasteDock.Core.Models;

/// <summary>Small, immutable metadata. Full content is loaded from its encrypted blob on demand.</summary>
public sealed record ClipEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid BlobId { get; init; } = Guid.NewGuid();
    public ClipKind Kind { get; init; }
    public string Title { get; init; } = "Untitled";
    public bool HasCustomTitle { get; init; }
    public bool IsSnippet { get; init; }
    public string Preview { get; init; } = "";
    public string SearchText { get; init; } = "";
    public string SourceApp { get; init; } = "Unknown";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastCopiedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastPastedAt { get; init; }
    public int CopyCount { get; init; } = 1;
    public bool IsPinned { get; init; }
    public bool IsFavorite { get; init; }
    public string[] Tags { get; init; } = [];
    public long ByteCount { get; init; }
    public string Fingerprint { get; init; } = "";
    public int ImageWidth { get; init; }
    public int ImageHeight { get; init; }
    public bool HasOcr { get; init; }
    public bool IsProtected => IsPinned || IsFavorite || IsSnippet || string.Equals(SourceApp, "PasteDock", StringComparison.OrdinalIgnoreCase);
}
