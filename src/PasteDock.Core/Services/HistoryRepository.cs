using PasteDock.Core.Models;
using PasteDock.Core.Search;
using PasteDock.Core.Storage;

namespace PasteDock.Core.Services;

/// <summary>All mutation is serialized. In-memory state changes only after the durable index commit.</summary>
public sealed class HistoryRepository
{
    private readonly EncryptedFileStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private HistoryState _state = new(2, []);
    private readonly TimeProvider _timeProvider;
    private ClipEntry[] _entries => Volatile.Read(ref _state).Items;
    public const long MaxUndoBytes = 128L * 1024 * 1024;
    public static TimeSpan UndoLifetime => TimeSpan.FromMinutes(10);
    public DeletionUndoInfo? DeletionUndo
    {
        get
        {
            var undo = Volatile.Read(ref _state).Undo;
            return undo is not null && undo.ExpiresAt > _timeProvider.GetUtcNow()
                ? new(undo.Items.Length, undo.Items.Sum(e => e.ByteCount), undo.ExpiresAt) : null;
        }
    }
    public long PendingDeletionBytes => Volatile.Read(ref _state).Undo?.Items.Sum(e => e.ByteCount) ?? 0;
    public event Action? Changed;
    public IReadOnlyList<ClipEntry> Snapshot => Array.AsReadOnly(_entries);

    public HistoryRepository(EncryptedFileStore store, TimeProvider? timeProvider = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var state = await _store.LoadStateAsync(ct).ConfigureAwait(false);
            Volatile.Write(ref _state, state);
            if (state.Undo is not null && state.Undo.ExpiresAt <= _timeProvider.GetUtcNow())
                await CommitStateAsync(state.Items, null, ct).ConfigureAwait(false);
            // Undo blobs remain referenced until an index without them has committed.
            _store.RemoveOrphans(_state.Items.Concat(_state.Undo?.Items ?? []).ToArray());
        }
        finally { _gate.Release(); }
    }

    public async Task<ClipEntry> CaptureAsync(ClipPayload payload, string source, AppSettings settings,
        CancellationToken ct = default)
    {
        payload.Validate(settings.MaxItemMb * 1024L * 1024);
        var fresh = ClipFactory.Create(payload, source, _timeProvider.GetUtcNow());
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        var wroteBlob = false;
        try
        {
            var current = _entries;
            var existing = current.FirstOrDefault(e => e.Fingerprint == fresh.Fingerprint);
            if (existing is not null)
            {
                var updated = existing with
                {
                    LastCopiedAt = fresh.LastCopiedAt,
                    SourceApp = fresh.SourceApp,
                    CopyCount = existing.CopyCount == int.MaxValue ? int.MaxValue : existing.CopyCount + 1
                };
                await CommitAsync(current.Select(e => e.Id == updated.Id ? updated : e).ToArray(), ct).ConfigureAwait(false);
                return updated;
            }
            var plan = RetentionPolicy.Plan(current.Append(fresh), settings, _timeProvider.GetUtcNow());
            if (plan.ProtectedQuotaExceeded || !plan.Kept.Any(e => e.Id == fresh.Id))
                throw new InvalidOperationException("History is full of protected items. Unpin items or increase the limit.");
            await _store.SavePayloadAsync(fresh.BlobId, payload, ct).ConfigureAwait(false);
            wroteBlob = true;
            await CommitAsync(plan.Kept.ToArray(), ct).ConfigureAwait(false);
            foreach (var old in plan.Removed) _store.DeleteBlobBestEffort(old.BlobId);
            return fresh;
        }
        catch
        {
            if (wroteBlob && !_entries.Any(e => e.BlobId == fresh.BlobId)) _store.DeleteBlobBestEffort(fresh.BlobId);
            throw;
        }
        finally { _gate.Release(); }
    }

    public async Task<ClipPayload> LoadAsync(Guid id, CancellationToken ct = default)
    {
        // Hold the mutation gate so an edit/delete cannot remove the blob while it is being read.
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var entry = _entries.FirstOrDefault(e => e.Id == id) ?? throw new KeyNotFoundException("This item no longer exists.");
            return await _store.LoadPayloadAsync(entry.BlobId, ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task UpdateMetadataAsync(IEnumerable<Guid> ids, Func<ClipEntry, ClipEntry> update, CancellationToken ct = default)
    {
        var set = ids.ToHashSet();
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var next = _entries.Select(entry =>
            {
                if (!set.Contains(entry.Id)) return entry;
                var edited = update(entry);
                // Callers may alter user metadata, but cannot repoint content or alter identity.
                return entry with
                {
                    Title = ClipFactory.Truncate(edited.Title.Trim(), 150),
                    IsPinned = edited.IsPinned,
                    IsFavorite = edited.IsFavorite,
                    Tags = ClipFactory.NormalizeTags(edited.Tags),
                    LastPastedAt = edited.LastPastedAt
                };
            }).ToArray();
            await CommitAsync(next, ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task ReplacePayloadAsync(Guid id, ClipPayload payload, AppSettings settings, CancellationToken ct = default)
    {
        payload.Validate(settings.MaxItemMb * 1024L * 1024);
        var replacement = ClipFactory.Create(payload, "PasteDock", _timeProvider.GetUtcNow());
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var previous = _entries.FirstOrDefault(e => e.Id == id) ?? throw new KeyNotFoundException("The item no longer exists.");
            replacement = replacement with
            {
                Id = previous.Id, CreatedAt = previous.CreatedAt, IsPinned = previous.IsPinned,
                IsFavorite = previous.IsFavorite, Tags = previous.Tags, CopyCount = previous.CopyCount,
                LastPastedAt = previous.LastPastedAt, SourceApp = previous.SourceApp
            };
            var next = _entries.Select(e => e.Id == id ? replacement : e).ToArray();
            // An explicit edit never silently evicts unrelated history.
            if (next.Sum(e => e.ByteCount) > settings.StorageLimitMb * 1024L * 1024)
                throw new InvalidOperationException("The edit would exceed the storage limit.");
            await _store.SavePayloadAsync(replacement.BlobId, payload, ct).ConfigureAwait(false);
            try { await CommitAsync(next, ct).ConfigureAwait(false); }
            catch { _store.DeleteBlobBestEffort(replacement.BlobId); throw; }
            _store.DeleteBlobBestEffort(previous.BlobId);
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var set = ids.ToHashSet();
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var removed = _entries.Where(e => set.Contains(e.Id)).ToArray();
            if (removed.Length == 0) return;
            var previousUndo = _state.Undo;
            await CommitStateAsync(_entries.Where(e => !set.Contains(e.Id)).ToArray(), null, ct).ConfigureAwait(false);
            foreach (var entry in removed) _store.DeleteBlobBestEffort(entry.BlobId);
            if (previousUndo is not null)
                foreach (var entry in previousUndo.Items) _store.DeleteBlobBestEffort(entry.BlobId);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Manual deletion with one durable, encrypted undo batch. A later deletion replaces the batch.</summary>
    public async Task<int> DeleteWithUndoAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var set = ids.ToHashSet();
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var removed = _entries.Where(e => set.Contains(e.Id)).ToArray();
            if (removed.Length == 0) return 0;
            if (removed.Sum(e => e.ByteCount) > MaxUndoBytes)
                throw new InvalidOperationException("This deletion exceeds the 128 MiB undo limit. Select fewer items or choose Delete permanently.");
            var previousUndo = _state.Undo;
            var now = _timeProvider.GetUtcNow();
            var undo = new DeletedBatch(Guid.NewGuid(), now, now + UndoLifetime, removed);
            await CommitStateAsync(_entries.Where(e => !set.Contains(e.Id)).ToArray(), undo, ct).ConfigureAwait(false);
            if (previousUndo is not null)
                foreach (var old in previousUndo.Items) _store.DeleteBlobBestEffort(old.BlobId);
            return removed.Length;
        }
        finally { _gate.Release(); }
    }

    public async Task<UndoRestoreResult> UndoLastDeleteAsync(AppSettings settings, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var undo = _state.Undo ?? throw new InvalidOperationException("There is no deletion to undo.");
            if (undo.ExpiresAt <= _timeProvider.GetUtcNow())
            {
                await CommitStateAsync(_entries, null, ct).ConfigureAwait(false);
                foreach (var old in undo.Items) _store.DeleteBlobBestEffort(old.BlobId);
                throw new InvalidOperationException("The ten-minute undo window has expired.");
            }
            var next = _entries.ToList();
            var byFingerprint = next.Select((e, i) => (e, i))
                .GroupBy(pair => pair.e.Fingerprint, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().i, StringComparer.Ordinal);
            var restored = 0; var merged = 0;
            foreach (var old in undo.Items)
            {
                ct.ThrowIfCancellationRequested();
                if (byFingerprint.TryGetValue(old.Fingerprint, out var index))
                {
                    var current = next[index];
                    // A copy made since deletion keeps its identity. Restore annotations without duplicating content.
                    next[index] = current with
                    {
                        Title = old.Title,
                        IsPinned = current.IsPinned || old.IsPinned,
                        IsFavorite = current.IsFavorite || old.IsFavorite,
                        Tags = ClipFactory.NormalizeTags(old.Tags.Concat(current.Tags)),
                        CreatedAt = current.CreatedAt < old.CreatedAt ? current.CreatedAt : old.CreatedAt,
                        LastCopiedAt = current.LastCopiedAt > old.LastCopiedAt ? current.LastCopiedAt : old.LastCopiedAt,
                        LastPastedAt = Later(current.LastPastedAt, old.LastPastedAt),
                        CopyCount = (int)Math.Min(int.MaxValue, (long)current.CopyCount + old.CopyCount)
                    };
                    merged++;
                }
                else
                {
                    byFingerprint[old.Fingerprint] = next.Count;
                    next.Add(old); restored++;
                }
            }
            if (next.Count > settings.HistoryCapacity || next.Sum(e => e.ByteCount) > settings.StorageLimitMb * 1024L * 1024)
                throw new InvalidOperationException("Restoring would exceed the history limit. Increase it or remove other clips; the undo copy is unchanged.");
            await CommitStateAsync(next.ToArray(), null, ct).ConfigureAwait(false);
            var live = next.Select(e => e.BlobId).ToHashSet();
            foreach (var old in undo.Items)
                if (!live.Contains(old.BlobId)) _store.DeleteBlobBestEffort(old.BlobId);
            return new UndoRestoreResult(restored, merged);
        }
        finally { _gate.Release(); }
    }

    public async Task ForgetDeletionAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var undo = _state.Undo;
            if (undo is null) return;
            await CommitStateAsync(_entries, null, ct).ConfigureAwait(false);
            foreach (var old in undo.Items) _store.DeleteBlobBestEffort(old.BlobId);
        }
        finally { _gate.Release(); }
    }

    private static DateTimeOffset? Later(DateTimeOffset? a, DateTimeOffset? b) =>
        a is null ? b : b is null ? a : a.Value > b.Value ? a : b;

    public async Task PruneAsync(AppSettings settings, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var now = _timeProvider.GetUtcNow();
            var plan = RetentionPolicy.Plan(_entries, settings, now);
            var expired = _state.Undo is { } batch && batch.ExpiresAt <= now ? batch : null;
            if (plan.Removed.Count == 0 && expired is null) return;
            await CommitStateAsync(plan.Kept.ToArray(), expired is null ? _state.Undo : null, ct).ConfigureAwait(false);
            foreach (var entry in plan.Removed) _store.DeleteBlobBestEffort(entry.BlobId);
            if (expired is not null)
                foreach (var entry in expired.Items) _store.DeleteBlobBestEffort(entry.BlobId);
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<ClipEntry>> SearchAsync(SearchQuery query, CancellationToken ct = default)
    {
        var snapshot = _entries;
        var matches = new List<ClipEntry>();
        foreach (var entry in snapshot)
        {
            ct.ThrowIfCancellationRequested();
            if (!SearchMatcher.MatchesMetadata(entry, query)) continue;
            if (SearchMatcher.MatchesText(entry, query)) { matches.Add(entry); continue; }
            if (!query.DeepSearch || query.Terms.Length == 0) continue;
            try
            {
                var payload = await LoadAsync(entry.Id, ct).ConfigureAwait(false);
                if (SearchMatcher.MatchesText(entry, query, payload.GetSearchableText())) matches.Add(entry);
            }
            catch (KeyNotFoundException) { } // Item was removed after the search snapshot.
        }
        return matches.OrderByDescending(e => e.IsPinned)
            .ThenByDescending(e => query.Terms.Length == 0 ? 0 : SearchMatcher.Score(e, query))
            .ThenByDescending(e => e.LastCopiedAt).ToArray();
    }

    public async Task<IReadOnlyList<BackupItem>> ExportAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_entries.Sum(e => e.ByteCount) > BackupService.MaxRawBytes)
                throw new InvalidOperationException("Encrypted backups are limited to 128 MiB of source content.");
            var result = new List<BackupItem>();
            foreach (var entry in _entries)
                result.Add(new BackupItem(entry, await _store.LoadPayloadAsync(entry.BlobId, ct).ConfigureAwait(false)));
            return result;
        }
        finally { _gate.Release(); }
    }

    public async Task<int> ImportAsync(IReadOnlyList<BackupItem> items, AppSettings settings, CancellationToken ct = default)
    {
        if (items.Count > Limits.MaxItems) throw new InvalidDataException("The backup contains too many items.");
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        var newBlobs = new List<Guid>();
        var committed = false;
        try
        {
            var next = _entries.ToList();
            var fingerprints = next.Select(e => e.Fingerprint).ToHashSet(StringComparer.Ordinal);
            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();
                if (item is null || item.Entry is null || item.Payload is null || item.Entry.Title is null
                    || item.Entry.SourceApp is null || item.Entry.Tags is null || item.Entry.Tags.Any(t => t is null) || item.Entry.CopyCount < 1)
                    throw new InvalidDataException("The backup contains invalid metadata.");
                item.Payload.Validate(settings.MaxItemMb * 1024L * 1024);
                var fresh = ClipFactory.Create(item.Payload, item.Entry.SourceApp, _timeProvider.GetUtcNow());
                if (!fingerprints.Add(fresh.Fingerprint)) continue;
                fresh = fresh with
                {
                    Title = ClipFactory.Truncate(item.Entry.Title, 150),
                    IsPinned = item.Entry.IsPinned, IsFavorite = item.Entry.IsFavorite,
                    Tags = ClipFactory.NormalizeTags(item.Entry.Tags),
                    CreatedAt = item.Entry.CreatedAt, LastCopiedAt = item.Entry.LastCopiedAt,
                    LastPastedAt = item.Entry.LastPastedAt, CopyCount = item.Entry.CopyCount
                };
                next.Add(fresh);
                if (next.Count > settings.HistoryCapacity || next.Sum(e => e.ByteCount) > settings.StorageLimitMb * 1024L * 1024)
                    throw new InvalidOperationException("The backup will not fit. Increase the history/storage limits before importing.");
                await _store.SavePayloadAsync(fresh.BlobId, item.Payload, ct).ConfigureAwait(false);
                newBlobs.Add(fresh.BlobId);
            }
            if (newBlobs.Count > 0) await CommitAsync(next.ToArray(), ct).ConfigureAwait(false);
            committed = true;
            return newBlobs.Count;
        }
        finally
        {
            if (!committed) foreach (var id in newBlobs) _store.DeleteBlobBestEffort(id);
            _gate.Release();
        }
    }

    private Task CommitAsync(ClipEntry[] next, CancellationToken ct) => CommitStateAsync(next, _state.Undo, ct);

    private async Task CommitStateAsync(ClipEntry[] next, DeletedBatch? undo, CancellationToken ct)
    {
        var state = new HistoryState(2, next, undo);
        await _store.SaveStateAsync(state, ct).ConfigureAwait(false);
        Volatile.Write(ref _state, state);
        // Observers cannot turn a successful disk commit into an apparent transaction failure.
        var observers = Changed;
        if (observers is not null)
            foreach (Action observer in observers.GetInvocationList())
                try { observer(); } catch { /* Observers own their error reporting. */ }
    }
}
