namespace PasteDock.Core.Models;

// The live index and the last deletion are one encrypted, atomically committed document.
internal sealed record HistoryState(int Version, ClipEntry[] Items, DeletedBatch? Undo = null);
internal sealed record DeletedBatch(Guid Id, DateTimeOffset DeletedAt, DateTimeOffset ExpiresAt, ClipEntry[] Items);

public sealed record DeletionUndoInfo(int ItemCount, long ByteCount, DateTimeOffset ExpiresAt);
public sealed record UndoRestoreResult(int Restored, int Merged)
{
    public int Total => Restored + Merged;
}
