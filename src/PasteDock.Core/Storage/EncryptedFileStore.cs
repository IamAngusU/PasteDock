using System.Security.Cryptography;
using System.Text.Json;
using PasteDock.Core.Models;

namespace PasteDock.Core.Storage;

public sealed class EncryptedFileStore
{
    private readonly string _root;
    private readonly IContentProtector _protector;
    private static readonly JsonSerializerOptions Json = new() { MaxDepth = 128 };
    public string RootPath => _root;

    public EncryptedFileStore(string root, IContentProtector protector)
    {
        _root = Path.GetFullPath(root);
        _protector = protector;
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, "content"));
    }

    public Task SaveIndexAsync(IReadOnlyList<ClipEntry> entries, CancellationToken ct = default) =>
        SaveStateAsync(new HistoryState(2, entries.ToArray()), ct);

    internal Task SaveStateAsync(HistoryState state, CancellationToken ct = default)
    {
        ValidateState(state);
        return WriteAsync(Path.Combine(_root, "index.pdv"), state, ct);
    }

    public async Task<IReadOnlyList<ClipEntry>> LoadIndexAsync(CancellationToken ct = default) =>
        (await LoadStateAsync(ct).ConfigureAwait(false)).Items;

    internal async Task<HistoryState> LoadStateAsync(CancellationToken ct = default)
    {
        var path = Path.Combine(_root, "index.pdv");
        if (!File.Exists(path))
        {
            if (Directory.EnumerateFiles(Path.Combine(_root, "content"), "*.pdv").Any())
                throw new InvalidDataException("The index is missing but encrypted content exists. No content has been deleted.");
            return new HistoryState(2, []);
        }
        var state = await ReadAsync<HistoryState>(path, 400L * 1024 * 1024, ct).ConfigureAwait(false);
        ValidateState(state);
        if (ReferencedEntries(state).Any(e => !File.Exists(BlobPath(e.BlobId))))
            throw new InvalidDataException("A committed content file is missing. Automatic cleanup was not performed.");
        return state with { Version = 2 };
    }

    private static IEnumerable<ClipEntry> ReferencedEntries(HistoryState state) =>
        state.Items.Concat(state.Undo?.Items ?? []);

    private static void ValidateState(HistoryState state)
    {
        if (state.Version is not (1 or 2) || state.Items is null || state.Items.Length > Limits.MaxItems
            || (state.Version == 1 && state.Undo is not null))
            throw new InvalidDataException("Unsupported or invalid history index. It has not been replaced.");
        if (state.Undo is { } undo && (undo.Id == Guid.Empty || undo.Items is null
            || undo.Items.Length is < 1 or > Limits.MaxItems || undo.ExpiresAt <= undo.DeletedAt
            || undo.ExpiresAt - undo.DeletedAt > TimeSpan.FromMinutes(10)))
            throw new InvalidDataException("The deletion undo record is invalid. No content has been deleted.");
        var all = ReferencedEntries(state).ToArray();
        if (all.Any(e => e is null || e.Id == Guid.Empty || e.BlobId == Guid.Empty || e.SnippetId == Guid.Empty
                || e.ByteCount < 0 || e.ByteCount > Limits.MaxPayloadBytes || e.CopyCount < 1 || e.Tags is null
                || e.Title is null || e.Preview is null || e.SearchText is null || e.SourceApp is null
                || e.Fingerprint is null || e.Fingerprint.Length != 64 || !e.Fingerprint.All(Uri.IsHexDigit)
                || !Enum.IsDefined(e.Kind) || e.Tags.Length > 16 || e.Tags.Any(t => t is null || t.Length > 32)
                || e.Title.Length > 150 || e.Preview.Length > Limits.PreviewCharacters
                || e.SearchText.Length > Limits.SearchCharacters || e.SourceApp.Length > 100)
            || all.Select(e => e.Id).Distinct().Count() != all.Length
            || all.Select(e => e.BlobId).Distinct().Count() != all.Length
            || state.Items.Where(e => e.SnippetIdentity.HasValue).Select(e => e.SnippetIdentity).Distinct().Count()
                != state.Items.Count(e => e.SnippetIdentity.HasValue)
            || (state.Undo?.Items.Sum(e => e.ByteCount) ?? 0) > 128L * 1024 * 1024)
            throw new InvalidDataException("The history index is invalid. It has not been replaced.");
    }

    public Task SavePayloadAsync(Guid blobId, ClipPayload payload, CancellationToken ct = default) =>
        WriteAsync(BlobPath(blobId), payload, ct);

    public async Task<ClipPayload> LoadPayloadAsync(Guid blobId, CancellationToken ct = default)
    {
        var payload = await ReadAsync<ClipPayload>(BlobPath(blobId), 400L * 1024 * 1024, ct).ConfigureAwait(false);
        payload.Validate();
        return payload;
    }

    public Task SaveSettingsAsync(AppSettings settings, CancellationToken ct = default) =>
        WriteAsync(Path.Combine(_root, "settings.pdv"), settings.Normalize(), ct, 2L * 1024 * 1024);

    internal Task SaveDraftAsync(EditorDraft draft, CancellationToken ct)
    {
        draft.Validate();
        return WriteAsync(Path.Combine(_root, "editor-draft.pdv"), new EditorDraftState(1, draft), ct, 8L * 1024 * 1024);
    }

    internal async Task<EditorDraft?> LoadDraftAsync(CancellationToken ct)
    {
        var path = Path.Combine(_root, "editor-draft.pdv");
        if (!File.Exists(path)) return null;
        var state = await ReadAsync<EditorDraftState>(path, 8L * 1024 * 1024, ct).ConfigureAwait(false);
        if (state.Version != 1 || state.Draft is null)
            throw new InvalidDataException("Unsupported editor recovery record. It has not been replaced.");
        state.Draft.Validate();
        return state.Draft;
    }

    internal void DeleteDraft() => File.Delete(Path.Combine(_root, "editor-draft.pdv"));

    public async Task<AppSettings> LoadSettingsAsync(CancellationToken ct = default)
    {
        var path = Path.Combine(_root, "settings.pdv");
        if (!File.Exists(path)) return new AppSettings();
        var settings = await ReadAsync<AppSettings>(path, 2 * 1024 * 1024, ct).ConfigureAwait(false);
        if (settings.SchemaVersion != 1) throw new InvalidDataException("Unsupported settings version.");
        return settings.Normalize();
    }

    public void DeleteBlobBestEffort(Guid id)
    {
        try { File.Delete(BlobPath(id)); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void RemoveOrphans(IReadOnlyList<ClipEntry> committed)
    {
        var live = committed.Select(e => e.BlobId).ToHashSet();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(_root, "content"), "*.pdv"))
            if (Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out var id) && !live.Contains(id))
                DeleteBlobBestEffort(id);
        foreach (var path in Directory.EnumerateFiles(_root, "*.tmp", SearchOption.AllDirectories))
        {
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private string BlobPath(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("Invalid content identifier.", nameof(id));
        return Path.Combine(_root, "content", id.ToString("N") + ".pdv");
    }

    private async Task WriteAsync<T>(string path, T value, CancellationToken ct, long maxFileBytes = 400L * 1024 * 1024)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        byte[] ciphertext;
        try
        {
            if (plaintext.LongLength > maxFileBytes)
                throw new InvalidDataException("Serialized metadata exceeds the storage safety limit. The previous file is unchanged.");
            ciphertext = _protector.Protect(plaintext);
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
        if (ciphertext.LongLength > maxFileBytes)
            throw new InvalidDataException("The encrypted file exceeds its read safety limit. The previous file is unchanged.");
        await AtomicFile.WriteAsync(path, ciphertext, ct).ConfigureAwait(false);
    }

    private async Task<T> ReadAsync<T>(string path, long maxSize, CancellationToken ct)
    {
        // Bound the opened handle, not an earlier FileInfo (avoids a replacement race).
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length is <= 0 || stream.Length > maxSize)
            throw new InvalidDataException("The encrypted file exceeds its safety limit.");
        var encrypted = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(encrypted, ct).ConfigureAwait(false);
        var plaintext = _protector.Unprotect(encrypted);
        try { return JsonSerializer.Deserialize<T>(plaintext, Json) ?? throw new InvalidDataException("The stored value is empty."); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

}
