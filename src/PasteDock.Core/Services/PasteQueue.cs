namespace PasteDock.Core.Services;

public sealed record QueuedClip(Guid Ticket, Guid ClipId);

/// <summary>A bounded, ordered queue. Tickets distinguish intentional repeated clips.</summary>
public sealed class PasteQueue
{
    private readonly List<QueuedClip> _items = [];
    public const int Capacity = 10_000;
    public int Count => _items.Count;
    public IReadOnlyList<QueuedClip> Snapshot => _items.ToArray();

    public void Add(IEnumerable<Guid> clipIds)
    {
        var ids = clipIds.Take(Capacity + 1).ToArray();
        if (ids.Any(id => id == Guid.Empty)) throw new ArgumentException("A queued clip must have a valid identity.");
        if (_items.Count + ids.Length > Capacity)
            throw new InvalidOperationException($"The queue holds up to {Capacity:N0} clips. Paste or remove queued items before adding more.");
        _items.AddRange(ids.Select(id => new QueuedClip(Guid.NewGuid(), id)));
    }
    public bool TryPeek(out QueuedClip item)
    {
        item = _items.FirstOrDefault()!;
        return item is not null;
    }
    public bool Complete(Guid ticket)
    {
        if (_items.Count == 0 || _items[0].Ticket != ticket) return false;
        _items.RemoveAt(0); return true;
    }
    public bool Remove(Guid ticket) => _items.RemoveAll(item => item.Ticket == ticket) > 0;
    public bool Move(Guid ticket, int offset)
    {
        if (offset is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(offset));
        var index = _items.FindIndex(item => item.Ticket == ticket);
        if (index < 0 || index + offset < 0 || index + offset >= _items.Count) return false;
        var item = _items[index]; _items.RemoveAt(index); _items.Insert(index + offset, item); return true;
    }
    public void Clear() => _items.Clear();
}
