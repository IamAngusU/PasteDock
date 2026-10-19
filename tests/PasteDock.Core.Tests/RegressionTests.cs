using System.Text;
using System.Text.Json;
using PasteDock.Core.Models;
using PasteDock.Core.Search;
using PasteDock.Core.Services;

namespace PasteDock.Core.Tests;

internal static partial class Program
{
    private static string Blob(Fixture f, ClipEntry entry) => Path.Combine(f.Root, "content", entry.BlobId.ToString("N") + ".pdv");
    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }

    private static void RegisterV020()
    {
        TestAsync("Import preserves historical dates and copy count", async () =>
        {
            using var f = new Fixture(); await f.Open();
            var original = Entry("archived", 90) with { CopyCount = 12, LastPastedAt = Now.AddDays(-80) };
            await f.Repository.ImportAsync([new(original, Text("archived"))], Settings);
            var imported = f.Repository.Snapshot.Single();
            Equal(original.CreatedAt, imported.CreatedAt); Equal(original.LastCopiedAt, imported.LastCopiedAt);
            Equal(original.LastPastedAt, imported.LastPastedAt); Equal(12, imported.CopyCount);
            True(imported.Id != original.Id); True(imported.BlobId != original.BlobId);
        });
        TestAsync("Undo deletion hides content from search and backups", async () =>
        {
            using var f = new Fixture(); await f.Open();
            var item = await f.Repository.CaptureAsync(Text("private deletion"), "test", Settings);
            Equal(1, await f.Repository.DeleteWithUndoAsync([item.Id]));
            Equal(0, f.Repository.Snapshot.Count);
            Equal(0, (await f.Repository.SearchAsync(new SearchQuery())).Count);
            Equal(0, (await f.Repository.ExportAsync()).Count);
            True(File.Exists(Blob(f, item))); Equal(1, f.Repository.DeletionUndo!.ItemCount);
        });
        TestAsync("Undo restores exact identity rich formats and annotations", async () =>
        {
            using var f = new Fixture(); await f.Open();
            var payload = new ClipPayload { Text = "rich", Html = "<b>rich</b>", Rtf = "{\\rtf1 rich}" };
            var item = await f.Repository.CaptureAsync(payload, "editor", Settings);
            await f.Repository.UpdateMetadataAsync([item.Id], e => e with { Title = "My snippet", IsPinned = true, Tags = ["work"] });
            await f.Repository.DeleteWithUndoAsync([item.Id]);
            var result = await f.Repository.UndoLastDeleteAsync(Settings);
            Equal(1, result.Restored); Equal(0, result.Merged);
            var restored = f.Repository.Snapshot.Single();
            Equal(item.Id, restored.Id); Equal(item.BlobId, restored.BlobId); Equal(item.CreatedAt, restored.CreatedAt);
            Equal("My snippet", restored.Title); True(restored.IsPinned); Equal("work", restored.Tags.Single());
            Equal(payload.Html, (await f.Repository.LoadAsync(item.Id)).Html);
            True(f.Repository.DeletionUndo is null);
        });
        TestAsync("Undo survives restart without orphan cleanup deleting its blobs", async () =>
        {
            using var f = new Fixture(); await f.Open();
            var item = await f.Repository.CaptureAsync(Text("restart undo"), "test", Settings);
            await f.Repository.DeleteWithUndoAsync([item.Id]);
            var reopened = await f.Reopen();
            True(File.Exists(Blob(f, item))); Equal(1, reopened.DeletionUndo!.ItemCount);
            await reopened.UndoLastDeleteAsync(Settings);
            Equal("restart undo", (await reopened.LoadAsync(item.Id)).Text);
        });
        TestAsync("A later deletion replaces only the previous undo batch", async () =>
        {
            using var f = new Fixture(); await f.Open();
            var first = await f.Repository.CaptureAsync(Text("first"), "test", Settings);
            var second = await f.Repository.CaptureAsync(Text("second"), "test", Settings);
            await f.Repository.DeleteWithUndoAsync([first.Id]);
            await f.Repository.DeleteWithUndoAsync([second.Id]);
            True(!File.Exists(Blob(f, first))); True(File.Exists(Blob(f, second)));
            await f.Repository.UndoLastDeleteAsync(Settings);
            Equal(second.Id, f.Repository.Snapshot.Single().Id);
        });
        TestAsync("Failed undo commit preserves both deletion state and stored content", async () =>
        {
            using var f = new Fixture(); await f.Open();
            var item = await f.Repository.CaptureAsync(Text("fail restore"), "test", Settings);
            await f.Repository.DeleteWithUndoAsync([item.Id]);
            f.Protector.FailOnProtectCall = f.Protector.ProtectCalls + 1;
            await ThrowsAsync<IOException>(() => f.Repository.UndoLastDeleteAsync(Settings));
            Equal(0, f.Repository.Snapshot.Count); True(File.Exists(Blob(f, item)));
            Equal(1, (await f.Reopen()).DeletionUndo!.ItemCount);
        });
        TestAsync("Failed replacement deletion retains previous undo and live history", async () =>
        {
            using var f = new Fixture(); await f.Open();
            var first = await f.Repository.CaptureAsync(Text("previous undo"), "test", Settings);
            var second = await f.Repository.CaptureAsync(Text("still live"), "test", Settings);
            await f.Repository.DeleteWithUndoAsync([first.Id]);
            f.Protector.FailOnProtectCall = f.Protector.ProtectCalls + 1;
            await ThrowsAsync<IOException>(() => f.Repository.DeleteWithUndoAsync([second.Id]));
            Equal(second.Id, f.Repository.Snapshot.Single().Id);
            True(File.Exists(Blob(f, first))); True(File.Exists(Blob(f, second)));
            var reopened = await f.Reopen(); await reopened.UndoLastDeleteAsync(Settings);
            Equal(2, reopened.Snapshot.Count);
        });
        TestAsync("Cancelled deletion does not mutate the index", async () =>
        {
            using var f = new Fixture(); await f.Open();
            var item = await f.Repository.CaptureAsync(Text("cancel delete"), "test", Settings);
            using var cts = new CancellationTokenSource(); cts.Cancel();
            await ThrowsAsync<OperationCanceledException>(() => f.Repository.DeleteWithUndoAsync([item.Id], cts.Token));
            Equal(1, f.Repository.Snapshot.Count); True(f.Repository.DeletionUndo is null);
        });
        TestAsync("Cancelled undo leaves restoration available", async () =>
        {
            using var f = new Fixture(); await f.Open();
            var item = await f.Repository.CaptureAsync(Text("cancel undo"), "test", Settings);
            await f.Repository.DeleteWithUndoAsync([item.Id]);
            using var cts = new CancellationTokenSource(); cts.Cancel();
            await ThrowsAsync<OperationCanceledException>(() => f.Repository.UndoLastDeleteAsync(Settings, cts.Token));
            Equal(1, f.Repository.DeletionUndo!.ItemCount); Equal(0, f.Repository.Snapshot.Count);
        });
        TestAsync("Undo expires exactly at the ten minute boundary", async () =>
        {
            var clock = new ManualTimeProvider(Now);
            using var f = new Fixture(clock); await f.Open();
            var item = await f.Repository.CaptureAsync(Text("boundary"), "test", Settings);
            await f.Repository.DeleteWithUndoAsync([item.Id]);
            clock.Advance(HistoryRepository.UndoLifetime - TimeSpan.FromTicks(1));
            True(f.Repository.DeletionUndo is not null);
            clock.Advance(TimeSpan.FromTicks(1)); True(f.Repository.DeletionUndo is null);
            await f.Repository.PruneAsync(Settings);
            True(!File.Exists(Blob(f, item))); Equal(0L, f.Repository.PendingDeletionBytes);
        });
        TestAsync("Restart expires an old undo batch before orphan cleanup", async () =>
        {
            var clock = new ManualTimeProvider(Now);
            using var f = new Fixture(clock); await f.Open();
            var item = await f.Repository.CaptureAsync(Text("expire on restart"), "test", Settings);
            await f.Repository.DeleteWithUndoAsync([item.Id]); clock.Advance(TimeSpan.FromHours(1));
            var reopened = await f.Reopen();
            True(reopened.DeletionUndo is null); True(!File.Exists(Blob(f, item)));
        });
        TestAsync("Failed expiry commit never deletes referenced blobs", async () =>
        {
            var clock = new ManualTimeProvider(Now);
            using var f = new Fixture(clock); await f.Open();
            var item = await f.Repository.CaptureAsync(Text("expiry fault"), "test", Settings);
            await f.Repository.DeleteWithUndoAsync([item.Id]); clock.Advance(TimeSpan.FromHours(1));
            f.Protector.FailOnProtectCall = f.Protector.ProtectCalls + 1;
            await ThrowsAsync<IOException>(() => f.Repository.PruneAsync(Settings));
            True(File.Exists(Blob(f, item))); True(f.Repository.PendingDeletionBytes > 0);
        });
        TestAsync("Over quota restoration preserves undo rather than evicting live clips", async () =>
        {
            using var f = new Fixture(); await f.Open();
            var first = await f.Repository.CaptureAsync(Text("deleted"), "test", Settings);
            await f.Repository.DeleteWithUndoAsync([first.Id]);
            var second = await f.Repository.CaptureAsync(Text("live"), "test", Settings);
            await ThrowsAsync<InvalidOperationException>(() => f.Repository.UndoLastDeleteAsync(Settings with { HistoryCapacity = 1 }));
            Equal(second.Id, f.Repository.Snapshot.Single().Id); True(f.Repository.DeletionUndo is not null);
        });
        TestAsync("Deleting unknown IDs does not replace the undo batch", async () =>
        {
            using var f = new Fixture(); await f.Open();
            var item = await f.Repository.CaptureAsync(Text("keep undo"), "test", Settings);
            await f.Repository.DeleteWithUndoAsync([item.Id]);
            Equal(0, await f.Repository.DeleteWithUndoAsync([Guid.NewGuid()]));
            await f.Repository.UndoLastDeleteAsync(Settings); Equal(item.Id, f.Repository.Snapshot.Single().Id);
        });
        TestAsync("Permanent deletion also discards the previous undo batch", async () =>
        {
            using var f = new Fixture(); await f.Open();
            var first = await f.Repository.CaptureAsync(Text("undo copy"), "test", Settings);
            var second = await f.Repository.CaptureAsync(Text("permanent"), "test", Settings);
            await f.Repository.DeleteWithUndoAsync([first.Id]); await f.Repository.DeleteAsync([second.Id]);
            True(f.Repository.DeletionUndo is null); Equal(0, (await f.Reopen()).Snapshot.Count);
            True(!File.Exists(Blob(f, first))); True(!File.Exists(Blob(f, second)));
        });
        TestAsync("Discarding undo never deletes unrelated live clips", async () =>
        {
            using var f = new Fixture(); await f.Open();
            var first = await f.Repository.CaptureAsync(Text("discard"), "test", Settings);
            var second = await f.Repository.CaptureAsync(Text("remain"), "test", Settings);
            await f.Repository.DeleteWithUndoAsync([first.Id]); await f.Repository.ForgetDeletionAsync();
            Equal(second.Id, f.Repository.Snapshot.Single().Id); True(File.Exists(Blob(f, second)));
            True(!File.Exists(Blob(f, first)));
        });
        TestAsync("Undo merges a recaptured duplicate without losing annotations", async () =>
        {
            var clock = new ManualTimeProvider(Now);
            using var f = new Fixture(clock); await f.Open();
            var original = await f.Repository.CaptureAsync(Text("same content"), "old app", Settings);
            await f.Repository.UpdateMetadataAsync([original.Id], e => e with { IsPinned = true, Tags = ["original"] });
            await f.Repository.DeleteWithUndoAsync([original.Id]); clock.Advance(TimeSpan.FromMinutes(1));
            var fresh = await f.Repository.CaptureAsync(Text("same content"), "new app", Settings);
            await f.Repository.UpdateMetadataAsync([fresh.Id], e => e with { Tags = ["recent"] });
            var result = await f.Repository.UndoLastDeleteAsync(Settings);
            Equal(0, result.Restored); Equal(1, result.Merged);
            var current = f.Repository.Snapshot.Single();
            Equal(fresh.Id, current.Id); True(current.IsPinned); Equal(2, current.Tags.Length);
            Equal(original.CreatedAt, current.CreatedAt); Equal(fresh.LastCopiedAt, current.LastCopiedAt);
            Equal(2, current.CopyCount); True(!File.Exists(Blob(f, original)));
        });
        TestAsync("Undo metadata and payload remain encrypted at rest", async () =>
        {
            using var f = new Fixture(); await f.Open();
            const string canary = "undo-private-content-canary-not-on-disk";
            var item = await f.Repository.CaptureAsync(Text(canary), "test", Settings);
            await f.Repository.DeleteWithUndoAsync([item.Id]);
            foreach (var path in Directory.GetFiles(f.Root, "*.pdv", SearchOption.AllDirectories))
                True(!Encoding.UTF8.GetString(await File.ReadAllBytesAsync(path)).Contains(canary, StringComparison.Ordinal));
        });
        TestAsync("Version one history migrates only through a successful commit", async () =>
        {
            using var f = new Fixture(); await f.Open();
            var item = await f.Repository.CaptureAsync(Text("legacy"), "test", Settings);
            var old = JsonSerializer.SerializeToUtf8Bytes(new { Version = 1, Items = new[] { item } });
            await File.WriteAllBytesAsync(Path.Combine(f.Root, "index.pdv"), f.Protector.Protect(old));
            var reopened = await f.Reopen();
            Equal("legacy", (await reopened.LoadAsync(item.Id)).Text);
            await reopened.DeleteWithUndoAsync([item.Id]);
            var raw = f.Protector.Unprotect(await File.ReadAllBytesAsync(Path.Combine(f.Root, "index.pdv")));
            using var doc = JsonDocument.Parse(raw); Equal(2, doc.RootElement.GetProperty("Version").GetInt32());
        });
        TestAsync("Unknown index version cannot trigger orphan cleanup", async () =>
        {
            using var f = new Fixture(); await f.Open();
            var item = await f.Repository.CaptureAsync(Text("future"), "test", Settings);
            var index = JsonSerializer.SerializeToUtf8Bytes(new { Version = 99, Items = new[] { item } });
            await File.WriteAllBytesAsync(Path.Combine(f.Root, "index.pdv"), f.Protector.Protect(index));
            await ThrowsAsync<InvalidDataException>(() => f.Reopen()); True(File.Exists(Blob(f, item)));
        });
        TestAsync("Aliased blob IDs in an index are rejected", async () =>
        {
            using var f = new Fixture(); await f.Open();
            var item = await f.Repository.CaptureAsync(Text("alias"), "test", Settings);
            await ThrowsAsync<InvalidDataException>(() => f.Store.SaveIndexAsync([item, item with { Id = Guid.NewGuid() }]));
            Equal(1, (await f.Reopen()).Snapshot.Count);
        });
        TestAsync("Invalid index fingerprints and copy counts are rejected", async () =>
        {
            using var f = new Fixture(); await f.Open();
            var item = await f.Repository.CaptureAsync(Text("validation"), "test", Settings);
            await ThrowsAsync<InvalidDataException>(() => f.Store.SaveIndexAsync([item with { Fingerprint = "wrong" }]));
            await ThrowsAsync<InvalidDataException>(() => f.Store.SaveIndexAsync([item with { CopyCount = 0 }]));
        });
        TestAsync("Snapshot collection cannot be replaced by a caller", async () =>
        {
            using var f = new Fixture(); await f.Open();
            var item = await f.Repository.CaptureAsync(Text("snapshot"), "test", Settings);
            True(f.Repository.Snapshot is not ClipEntry[]);
            var list = (IList<ClipEntry>)f.Repository.Snapshot;
            Throws<NotSupportedException>(() => list[0] = item with { Title = "modified" });
        });
        TestAsync("Atomic text export preserves Unicode without adding a BOM", async () =>
        {
            using var f = new Fixture(); var path = Path.Combine(f.Root, "export.txt");
            const string text = "Ä雪😀\r\ncomplete";
            await ExportService.WriteTextAsync(path, text);
            var bytes = await File.ReadAllBytesAsync(path);
            True(bytes.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(text)));
        });
        TestAsync("Cancelled export preserves an existing backup and removes temporary files", async () =>
        {
            using var f = new Fixture(); var path = Path.Combine(f.Root, "existing.pdbackup");
            await ExportService.WriteBytesAsync(path, [1, 2, 3]);
            using var cts = new CancellationTokenSource(); cts.Cancel();
            await ThrowsAsync<OperationCanceledException>(() => ExportService.WriteBytesAsync(path, [4, 5, 6], cts.Token));
            var bytes = await File.ReadAllBytesAsync(path); True(bytes.AsSpan().SequenceEqual(new byte[] { 1, 2, 3 }));
            Equal(0, Directory.GetFiles(f.Root, "*.tmp", SearchOption.AllDirectories).Length);
        });
        TestAsync("Failed export rename cleans the staging file", async () =>
        {
            using var f = new Fixture(); var path = Path.Combine(f.Root, "directory-not-file"); Directory.CreateDirectory(path);
            // Windows reports a directory destination as access denied; Unix reports an I/O error.
            if (OperatingSystem.IsWindows())
                await ThrowsAsync<UnauthorizedAccessException>(() => ExportService.WriteBytesAsync(path, [1, 2]));
            else
                await ThrowsAsync<IOException>(() => ExportService.WriteBytesAsync(path, [1, 2]));
            True(Directory.Exists(path)); Equal(0, Directory.GetFiles(f.Root, "*.tmp", SearchOption.AllDirectories).Length);
        });
        Test("Composition counts separator and UTF8 bytes before joining", () =>
        {
            Throws<InvalidDataException>(() => PayloadComposer.Combine([Text("雪"), Text("é")], "-", true, maxBytes: 5));
            Equal("雪é", PayloadComposer.Combine([Text("雪"), Text("é")], "", true, maxBytes: 5).Text);
        });
        TestAsync("Bounded composition stops loading after exceeding its limit", async () =>
        {
            var loaded = 0;
            await ThrowsAsync<InvalidDataException>(() => PayloadComposer.CombineAsync(
                [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()], (_, _) => { loaded++; return Task.FromResult(Text("abcd")); },
                " ", true, maxBytes: 5));
            Equal(2, loaded);
        });
        TestAsync("Composition cancellation prevents even the first content load", async () =>
        {
            var loaded = 0; using var cts = new CancellationTokenSource(); cts.Cancel();
            await ThrowsAsync<OperationCanceledException>(() => PayloadComposer.CombineAsync([Guid.NewGuid()],
                (_, _) => { loaded++; return Task.FromResult(Text("test")); }, "", true, cts.Token));
            Equal(0, loaded);
        });
        TestAsync("Async composition preserves selection order", async () =>
        {
            var a = Guid.NewGuid(); var b = Guid.NewGuid();
            var result = await PayloadComposer.CombineAsync([b, a], (id, _) => Task.FromResult(Text(id == a ? "A" : "B")), "|", false);
            Equal("B|A", result.Text);
        });
        Test("Composition cannot bypass the hard size limit", () =>
            Throws<ArgumentOutOfRangeException>(() => PayloadComposer.Combine([Text("test")], "", true, Limits.MaxPayloadBytes + 1)));
        TestAsync("Oversized undo is rejected without allocating or deleting content", async () =>
        {
            using var f = new Fixture(); await f.Open();
            var items = new List<ClipEntry>();
            for (var i = 0; i < 3; i++)
                items.Add(await f.Repository.CaptureAsync(Text("quota-" + i), "test", Settings));
            // Simulate large payload metadata without consuming hundreds of megabytes in the test.
            await f.Store.SaveIndexAsync(items.Select(e => e with { ByteCount = Limits.MaxPayloadBytes }).ToArray());
            var reopened = await f.Reopen();
            await ThrowsAsync<InvalidOperationException>(() => reopened.DeleteWithUndoAsync(items.Select(e => e.Id)));
            Equal(3, reopened.Snapshot.Count); True(reopened.DeletionUndo is null);
            True(items.All(e => File.Exists(Blob(f, e))));
        });
        TestAsync("A failing observer cannot make undo appear uncommitted", async () =>
        {
            using var f = new Fixture(); await f.Open();
            var item = await f.Repository.CaptureAsync(Text("observer undo"), "test", Settings);
            await f.Repository.DeleteWithUndoAsync([item.Id]);
            f.Repository.Changed += () => throw new InvalidOperationException("observer");
            await f.Repository.UndoLastDeleteAsync(Settings);
            Equal(item.Id, (await f.Reopen()).Snapshot.Single().Id);
        });
    }
}
