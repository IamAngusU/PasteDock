using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PasteDock.Core.Models;
using PasteDock.Core.Search;
using PasteDock.Core.Services;
using PasteDock.Core.Storage;

namespace PasteDock.Core.Tests;

internal static partial class Program
{
    private static readonly List<(string Name, Func<Task> Run)> Cases = [];
    private static readonly AppSettings Settings = new() { RetentionDays = 0 };
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
    private static ClipPayload Text(string value) => new() { Text = value };
    private static ClipEntry Entry(string text = "sample", int daysOld = 0, bool pin = false, bool favorite = false, long? bytes = null)
    {
        var item = ClipFactory.Create(Text(text), "editor", Now.AddDays(-daysOld));
        return item with { IsPinned = pin, IsFavorite = favorite, ByteCount = bytes ?? item.ByteCount };
    }
    private static void Test(string name, Action test) => Cases.Add((name, () => { test(); return Task.CompletedTask; }));
    private static void TestAsync(string name, Func<Task> test) => Cases.Add((name, test));
    private static void True(bool condition, string? message = null) { if (!condition) throw new Exception(message ?? "Expected true."); }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}."); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }
    private static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); } catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }

    public static async Task<int> Main(string[] args)
    {
        Register();
        RegisterV020();
        RegisterV030();
        RegisterV040();
        RegisterV050();
        RegisterV060();
        var results = new List<object>();
        var failed = 0;
        foreach (var (name, run) in Cases)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                await run();
                Console.WriteLine($"PASS {name}");
                results.Add(new { name, status = "passed", milliseconds = watch.ElapsedMilliseconds, error = (string?)null });
            }
            catch (Exception exception)
            {
                failed++;
                Console.Error.WriteLine($"FAIL {name}: {exception}");
                results.Add(new { name, status = "failed", milliseconds = watch.ElapsedMilliseconds, error = exception.ToString() });
            }
        }
        Console.WriteLine($"{Cases.Count - failed}/{Cases.Count} passed.");
        var jsonIndex = Array.IndexOf(args, "--json");
        if (jsonIndex >= 0 && jsonIndex + 1 < args.Length)
        {
            var path = Path.GetFullPath(args[jsonIndex + 1]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
            { timestampUtc = DateTimeOffset.UtcNow, total = Cases.Count, passed = Cases.Count - failed, failed, results }, new JsonSerializerOptions { WriteIndented = true }));
        }
        return failed == 0 ? 0 : 1;
    }

    private static void Register()
    {
        Test("Classify JSON before generic code", () => Equal(ClipKind.Json, ClipFactory.Classify(Text("{\"value\":42}"))));
        Test("Classify code", () => Equal(ClipKind.Code, ClipFactory.Classify(Text("public class Example { }"))));
        Test("Classify https URL", () => Equal(ClipKind.Link, ClipFactory.Classify(Text("https://example.test/path?a=1"))));
        Test("Executable schemes are not links", () => True(ClipFactory.Classify(Text("javascript:alert(1)")) != ClipKind.Link));
        Test("Classify RGBA color", () => Equal(ClipKind.Color, ClipFactory.Classify(Text("#A1B2C3FF"))));
        Test("Invalid color is text", () => Equal(ClipKind.Text, ClipFactory.Classify(Text("#GGGGGG"))));
        Test("Plain text remains text", () => Equal(ClipKind.Text, ClipFactory.Classify(Text("remember the meeting"))));
        Test("File reference name supports Windows paths", () => Equal("report.txt", ClipFactory.Create(new ClipPayload { Files = [@"C:\Work\report.txt"] }, "Explorer", Now).Title));
        Test("Fingerprint distinguishes format boundaries", () =>
        {
            True(ClipFactory.Fingerprint(new ClipPayload { Text = "ab", Html = "c" }) != ClipFactory.Fingerprint(new ClipPayload { Text = "a", Html = "bc" }));
        });
        Test("Fingerprint includes rich text", () => True(ClipFactory.Fingerprint(Text("hello")) != ClipFactory.Fingerprint(new ClipPayload { Text = "hello", Html = "<b>hello</b>" })));
        Test("Fingerprint ignores derived OCR text", () => Equal(ClipFactory.Fingerprint(Text("hello")), ClipFactory.Fingerprint(new ClipPayload { Text = "hello", OcrText = "derived" })));
        Test("UTF-8 size is not UTF-16 length", () => Equal(Encoding.UTF8.GetByteCount("Ä雪😀"), checked((int)Text("Ä雪😀").GetByteCount())));
        Test("Truncation keeps surrogate pairs intact", () => Equal("a", ClipFactory.Truncate("a😀z", 2)));
        Test("Content is not truncated by metadata preview", () =>
        {
            var payload = Text(new string('a', Limits.SearchCharacters + 100) + "end");
            var entry = ClipFactory.Create(payload, "test", Now);
            Equal(Limits.SearchCharacters, entry.SearchText.Length);
            True(payload.Text!.EndsWith("end", StringComparison.Ordinal));
        });
        Test("Empty payload is rejected", () => Throws<InvalidDataException>(() => new ClipPayload().Validate()));
        Test("Payload size rejection is explicit", () => Throws<InvalidDataException>(() => Text("12345").Validate(4)));
        Test("Invalid file references are rejected", () => Throws<InvalidDataException>(() => new ClipPayload { Files = [null!] }.Validate()));
        Test("Malformed PNG is rejected", () => Throws<InvalidDataException>(() => new ClipPayload { Png = [1, 2, 3], ImageWidth = 1, ImageHeight = 1 }.Validate()));
        Test("Tags normalize, deduplicate and reject whitespace", () => Equal("work,dev_ops", string.Join(',', ClipFactory.NormalizeTags(["#Work", "work", "dev_ops", "two words"]))));
        Test("Tags have a fixed maximum count", () => Equal(16, ClipFactory.NormalizeTags(Enumerable.Range(0, 30).Select(i => $"tag{i}")).Length));
        Test("Settings normalize bounds and nonfinite geometry", () =>
        {
            var settings = (new AppSettings { HistoryCapacity = int.MaxValue, WindowWidth = double.NaN, RetentionDays = -1 }).Normalize();
            Equal(Limits.MaxItems, settings.HistoryCapacity); Equal(1080d, settings.WindowWidth); Equal(0, settings.RetentionDays);
        });
        Test("Settings normalize exclusions", () => Equal("notepad", (new AppSettings { ExcludedApps = ["notepad.exe", "notepad.exe"] }).Normalize().ExcludedApps.Single()));
        Test("Quoted search and filters parse together", () =>
        {
            var query = SearchQuery.Parse("\"hello world\" #Work app:Code type:json is:pinned after:2026-01-01");
            Equal("hello world", query.Terms.Single()); Equal("Work", query.Tags.Single()); Equal("Code", query.App);
            Equal(ClipKind.Json, query.Kind!.Value); True(query.PinnedOnly); Equal(2026, query.After!.Value.Year);
        });
        Test("Unknown filter is a literal search term", () => Equal("type:unknown", SearchQuery.Parse("type:unknown").Terms.Single()));
        Test("Blank search matches metadata", () => True(SearchMatcher.MatchesMetadata(Entry(), SearchQuery.Parse(""))));
        Test("Search terms are AND, not OR", () => True(!SearchMatcher.MatchesText(Entry("alpha beta"), SearchQuery.Parse("alpha gamma"))));
        Test("Metadata filters use tag and app case insensitively", () =>
        {
            var entry = Entry("sample", pin: true) with { Tags = ["work"], SourceApp = "VisualStudio" };
            True(SearchMatcher.MatchesMetadata(entry, SearchQuery.Parse("#WORK app:studio is:pinned")));
        });
        Test("Date filter rejects older copies", () => True(!SearchMatcher.MatchesMetadata(Entry(daysOld: 365), SearchQuery.Parse("after:2026-01-01"))));
        Test("Secret filtering rejects private key markers", () => True(!PrivacyPolicy.Evaluate(Text("-----BEGIN PRIVATE KEY-----\ntest"), "editor", Settings).Allowed));
        Test("Secret filtering rejects recognizable token shape", () => True(!PrivacyPolicy.Evaluate(Text("ghp_" + new string('x', 36)), "editor", Settings).Allowed));
        Test("No claim that ordinary passwords can be recognized", () => True(PrivacyPolicy.Evaluate(Text("correct horse battery staple"), "editor", Settings).Allowed));
        Test("Excluded source is not captured", () => True(!PrivacyPolicy.Evaluate(Text("sample"), "Bitwarden.exe", Settings).Allowed));
        Test("Paused and locked states block capture", () =>
        {
            True(!PrivacyPolicy.Evaluate(Text("sample"), "editor", Settings, paused: true).Allowed);
            True(!PrivacyPolicy.Evaluate(Text("sample"), "editor", Settings, sessionLocked: true).Allowed);
        });
        Test("Sensitive clipboard format blocks capture", () => True(!PrivacyPolicy.Evaluate(Text("sample"), "editor", Settings, sensitiveFormat: true).Allowed));
        Test("Retention removes old unprotected content", () =>
        {
            var result = RetentionPolicy.Plan([Entry("old", 90), Entry("new")], Settings with { RetentionDays = 30 }, Now);
            Equal("new", result.Kept.Single().Title); Equal(1, result.Removed.Count);
        });
        Test("Retention protects pins and favorites", () =>
        {
            var result = RetentionPolicy.Plan([Entry("pin", 90, pin: true), Entry("favorite", 90, favorite: true)], Settings with { RetentionDays = 1 }, Now);
            Equal(2, result.Kept.Count); Equal(0, result.Removed.Count);
        });
        Test("Retention capacity evicts oldest first", () =>
        {
            var result = RetentionPolicy.Plan([Entry("old", 10), Entry("new")], Settings with { HistoryCapacity = 1 }, Now);
            Equal("new", result.Kept.Single().Title);
        });
        Test("Protected quota overflow is reported, not deleted", () =>
        {
            var result = RetentionPolicy.Plan([Entry("pin", pin: true), Entry("favorite", favorite: true)], Settings with { HistoryCapacity = 1 }, Now);
            True(result.ProtectedQuotaExceeded); Equal(2, result.Kept.Count);
        });
        Test("Storage budget is independent of item count", () =>
        {
            var result = RetentionPolicy.Plan([Entry("old", 1, bytes: 800_000), Entry("new", bytes: 800_000)], Settings with { StorageLimitMb = 1 }, Now);
            Equal("new", result.Kept.Single().Title);
        });
        Test("Multi-paste preserves order", () => Equal("one\n\ntwo", PayloadComposer.Combine([Text("one"), Text("two")], "\n\n", false).Text));
        Test("Single rich item retains formats", () => Equal("<b>one</b>", PayloadComposer.Combine([new ClipPayload { Text = "one", Html = "<b>one</b>" }], "", false).Html));
        Test("Plain paste explicitly discards rich formatting", () => True(PayloadComposer.Combine([new ClipPayload { Text = "one", Html = "<b>one</b>" }], "", true).Html is null));
        Test("File multi-paste deduplicates Windows paths", () => Equal(1, PayloadComposer.Combine([new ClipPayload { Files = [@"C:\A.txt"] }, new ClipPayload { Files = [@"c:\a.txt"] }], "", false).Files.Length));
        Test("Mixed text and files requires queue", () => Throws<InvalidOperationException>(() => PayloadComposer.Combine([Text("text"), new ClipPayload { Files = [@"C:\A.txt"] }], "", false)));
        Test("JSON pretty and compact preserve values", () => Equal("{\"a\":1}", TextTransforms.Apply(TextTransforms.Apply("{\"a\":1}", TextTransform.JsonPretty), TextTransform.JsonCompact)));
        Test("Invalid JSON never silently changes", () => Throws<JsonException>(() => TextTransforms.Apply("{broken", TextTransform.JsonPretty)));
        Test("Trim and duplicate line transforms", () => Equal("a\nb", TextTransforms.Apply(TextTransforms.Apply(" a \r\n a \r\n b ", TextTransform.TrimLines), TextTransform.UniqueLines)));
        Test("Base64 round-trip supports Unicode", () => Equal("雪 😀", TextTransforms.Apply(TextTransforms.Apply("雪 😀", TextTransform.Base64Encode), TextTransform.Base64Decode)));
        Test("Base64 invalid UTF-8 is rejected", () => Throws<DecoderFallbackException>(() => TextTransforms.Apply("/w==", TextTransform.Base64Decode)));
        Test("URL transform round-trip", () => Equal("a b/雪", TextTransforms.Apply(TextTransforms.Apply("a b/雪", TextTransform.UrlEncode), TextTransform.UrlDecode)));

        TestAsync("PNG bytes and dimensions survive encrypted round-trip", async () =>
        {
            using var f = new Fixture(); await f.Open();
            var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAAEUlEQVR4nGPgUbLwi0rJqwAACBUCWf3LPG8AAAAASUVORK5CYII=");
            var payload = new ClipPayload { Png = png, ImageWidth = 2, ImageHeight = 1 };
            var entry = await f.Repository.CaptureAsync(payload, "image-test", Settings);
            var restored = await (await f.Reopen()).LoadAsync(entry.Id);
            True(png.AsSpan().SequenceEqual(restored.Png)); Equal(2, restored.ImageWidth); Equal(1, restored.ImageHeight);
            Equal(ClipKind.Image, entry.Kind);
        });
        Test("PNG dimensions must match the file header", () =>
        {
            var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAAEUlEQVR4nGPgUbLwi0rJqwAACBUCWf3LPG8AAAAASUVORK5CYII=");
            Throws<InvalidDataException>(() => new ClipPayload { Png = png, ImageWidth = 20, ImageHeight = 1 }.Validate());
        });

        TestAsync("Encrypted repository round-trip preserves complete content", async () =>
        {
            using var f = new Fixture(); await f.Open();
            var text = new string('x', 150_000) + "\n終わり";
            var entry = await f.Repository.CaptureAsync(Text(text), "editor", Settings);
            var reopened = await f.Reopen();
            Equal(text, (await reopened.LoadAsync(entry.Id)).Text);
        });
        TestAsync("Index and blobs contain no plaintext canary", async () =>
        {
            using var f = new Fixture(); await f.Open();
            const string canary = "very-distinct-private-canary-for-storage-test";
            await f.Repository.CaptureAsync(Text(canary), "editor", Settings);
            foreach (var path in Directory.GetFiles(f.Root, "*.pdv", SearchOption.AllDirectories))
                True(!Encoding.UTF8.GetString(await File.ReadAllBytesAsync(path)).Contains(canary, StringComparison.Ordinal));
        });
        TestAsync("Duplicate copies reuse identity and preserve tags", async () =>
        {
            using var f = new Fixture(); await f.Open();
            var first = await f.Repository.CaptureAsync(Text("duplicate"), "first", Settings);
            await f.Repository.UpdateMetadataAsync([first.Id], e => e with { IsPinned = true, Tags = ["work"] });
            var second = await f.Repository.CaptureAsync(Text("duplicate"), "second", Settings);
            Equal(first.Id, second.Id); Equal(first.BlobId, second.BlobId); Equal(2, second.CopyCount);
            True(second.IsPinned); Equal("work", second.Tags.Single()); Equal(1, f.Repository.Snapshot.Count);
        });
        TestAsync("Concurrent capture is serialized", async () =>
        {
            using var f = new Fixture(); await f.Open();
            await Task.WhenAll(Enumerable.Range(0, 20).Select(i => f.Repository.CaptureAsync(Text($"entry-{i}"), "test", Settings)));
            Equal(20, f.Repository.Snapshot.Count); Equal(20, (await f.Reopen()).Snapshot.Count);
        });
        TestAsync("Cancelled capture does not commit", async () =>
        {
            using var f = new Fixture(); await f.Open();
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            await ThrowsAsync<OperationCanceledException>(() => f.Repository.CaptureAsync(Text("no"), "test", Settings, cancellation.Token));
            Equal(0, f.Repository.Snapshot.Count);
        });
        TestAsync("Editing changes blob revision, not item identity", async () =>
        {
            using var f = new Fixture(); await f.Open();
            var old = await f.Repository.CaptureAsync(Text("before"), "test", Settings);
            await f.Repository.UpdateMetadataAsync([old.Id], e => e with { IsFavorite = true, Tags = ["saved"] });
            await f.Repository.ReplacePayloadAsync(old.Id, Text("after"), Settings);
            var updated = f.Repository.Snapshot.Single();
            Equal(old.Id, updated.Id); True(old.BlobId != updated.BlobId); True(updated.IsFavorite);
            Equal("saved", updated.Tags.Single()); Equal("after", (await f.Repository.LoadAsync(old.Id)).Text);
            True(!File.Exists(Path.Combine(f.Root, "content", old.BlobId.ToString("N") + ".pdv")));
        });
        TestAsync("Metadata callback cannot replace identity or content", async () =>
        {
            using var f = new Fixture(); await f.Open();
            var old = await f.Repository.CaptureAsync(Text("original"), "test", Settings);
            await f.Repository.UpdateMetadataAsync([old.Id], e => e with { Id = Guid.NewGuid(), BlobId = Guid.NewGuid(), Title = "renamed" });
            var updated = f.Repository.Snapshot.Single(); Equal(old.Id, updated.Id); Equal(old.BlobId, updated.BlobId); Equal("renamed", updated.Title);
        });
        TestAsync("Deletion survives reopening and removes blob", async () =>
        {
            using var f = new Fixture(); await f.Open();
            var old = await f.Repository.CaptureAsync(Text("delete me"), "test", Settings);
            await f.Repository.DeleteAsync([old.Id]);
            Equal(0, (await f.Reopen()).Snapshot.Count);
            await ThrowsAsync<KeyNotFoundException>(() => f.Repository.LoadAsync(old.Id));
            Equal(0, Directory.GetFiles(Path.Combine(f.Root, "content"), "*.pdv").Length);
        });
        TestAsync("Failed index protection leaves old transaction intact", async () =>
        {
            using var f = new Fixture(); await f.Open();
            await f.Repository.CaptureAsync(Text("committed"), "test", Settings);
            f.Protector.FailOnProtectCall = f.Protector.ProtectCalls + 2; // New payload succeeds, new index fails.
            await ThrowsAsync<IOException>(() => f.Repository.CaptureAsync(Text("not committed"), "test", Settings));
            Equal(1, f.Repository.Snapshot.Count); Equal("committed", f.Repository.Snapshot.Single().Title);
            Equal(1, (await f.Reopen()).Snapshot.Count);
            Equal(1, Directory.GetFiles(Path.Combine(f.Root, "content"), "*.pdv").Length);
        });
        TestAsync("Failed edited index keeps original blob readable", async () =>
        {
            using var f = new Fixture(); await f.Open();
            var old = await f.Repository.CaptureAsync(Text("before"), "test", Settings);
            f.Protector.FailOnProtectCall = f.Protector.ProtectCalls + 2;
            await ThrowsAsync<IOException>(() => f.Repository.ReplacePayloadAsync(old.Id, Text("after"), Settings));
            Equal("before", (await f.Repository.LoadAsync(old.Id)).Text);
            Equal("before", (await (await f.Reopen()).LoadAsync(old.Id)).Text);
        });
        TestAsync("Faulty observer cannot roll back a committed item", async () =>
        {
            using var f = new Fixture(); await f.Open();
            f.Repository.Changed += () => throw new InvalidOperationException("observer error");
            await f.Repository.CaptureAsync(Text("committed"), "test", Settings);
            Equal(1, (await f.Reopen()).Snapshot.Count);
        });
        TestAsync("Tampered index is not replaced with empty history", async () =>
        {
            using var f = new Fixture(); await f.Open();
            await f.Repository.CaptureAsync(Text("retained"), "test", Settings);
            var path = Path.Combine(f.Root, "index.pdv"); var bytes = await File.ReadAllBytesAsync(path); bytes[^1] ^= 1;
            await File.WriteAllBytesAsync(path, bytes);
            await ThrowsAsync<CryptographicException>(() => f.Reopen());
            var onDisk = await File.ReadAllBytesAsync(path);
            True(bytes.AsSpan().SequenceEqual(onDisk));
            Equal(1, Directory.GetFiles(Path.Combine(f.Root, "content"), "*.pdv").Length);
        });
        TestAsync("Missing index never triggers destructive orphan cleanup", async () =>
        {
            using var f = new Fixture(); await f.Open();
            await f.Repository.CaptureAsync(Text("retained"), "test", Settings);
            File.Delete(Path.Combine(f.Root, "index.pdv"));
            await ThrowsAsync<InvalidDataException>(() => f.Reopen());
            Equal(1, Directory.GetFiles(Path.Combine(f.Root, "content"), "*.pdv").Length);
        });
        TestAsync("Missing committed payload blocks startup cleanup", async () =>
        {
            using var f = new Fixture(); await f.Open();
            var entry = await f.Repository.CaptureAsync(Text("retained"), "test", Settings);
            File.Delete(Path.Combine(f.Root, "content", entry.BlobId.ToString("N") + ".pdv"));
            await ThrowsAsync<InvalidDataException>(() => f.Reopen());
            True(File.Exists(Path.Combine(f.Root, "index.pdv")));
        });
        TestAsync("Orphans are removed only after a valid index loads", async () =>
        {
            using var f = new Fixture(); await f.Open();
            await f.Repository.CaptureAsync(Text("valid"), "test", Settings);
            var orphan = Guid.NewGuid(); await f.Store.SavePayloadAsync(orphan, Text("orphan"));
            await f.Reopen();
            True(!File.Exists(Path.Combine(f.Root, "content", orphan.ToString("N") + ".pdv")));
        });
        TestAsync("Deep search finds text beyond the bounded index", async () =>
        {
            using var f = new Fixture(); await f.Open();
            await f.Repository.CaptureAsync(Text(new string('x', Limits.SearchCharacters + 100) + " rare-tail-needle"), "test", Settings);
            var query = SearchQuery.Parse("rare-tail-needle");
            Equal(0, (await f.Repository.SearchAsync(query)).Count);
            Equal(1, (await f.Repository.SearchAsync(query with { DeepSearch = true })).Count);
        });
        TestAsync("Pins sort above recency", async () =>
        {
            using var f = new Fixture(); await f.Open();
            var pinned = await f.Repository.CaptureAsync(Text("pin"), "test", Settings);
            await f.Repository.UpdateMetadataAsync([pinned.Id], e => e with { IsPinned = true });
            await f.Repository.CaptureAsync(Text("newer"), "test", Settings);
            Equal(pinned.Id, (await f.Repository.SearchAsync(new SearchQuery()))[0].Id);
        });
        TestAsync("Full protected history rejects capture without eviction", async () =>
        {
            using var f = new Fixture(); await f.Open();
            var pinned = await f.Repository.CaptureAsync(Text("pin"), "test", Settings);
            await f.Repository.UpdateMetadataAsync([pinned.Id], e => e with { IsPinned = true });
            await ThrowsAsync<InvalidOperationException>(() => f.Repository.CaptureAsync(Text("new"), "test", Settings with { HistoryCapacity = 1 }));
            Equal(pinned.Id, f.Repository.Snapshot.Single().Id);
        });
        Test("Backup requires a meaningful passphrase length", () => Throws<ArgumentException>(() => BackupService.Encrypt([], "short")));
        Test("Backup authenticates content, passphrase and header", () =>
        {
            const string password = "a distinct test-only passphrase";
            var item = new BackupItem(Entry("backup"), Text("backup"));
            var archive = BackupService.Encrypt([item], password);
            Equal("backup", BackupService.Decrypt(archive, password).Single().Payload.Text);
            Throws<CryptographicException>(() => BackupService.Decrypt(archive, "a different test-only passphrase"));
            var altered = archive.ToArray(); altered[^1] ^= 1;
            Throws<CryptographicException>(() => BackupService.Decrypt(altered, password));
            var header = archive.ToArray(); header[8] ^= 1;
            Throws<CryptographicException>(() => BackupService.Decrypt(header, password));
        });
        Test("Backup format rejects an arbitrary file", () => Throws<InvalidDataException>(() => BackupService.Decrypt([0, 1, 2], "test")));
        TestAsync("Import merges without duplicating existing clips", async () =>
        {
            using var f = new Fixture(); await f.Open();
            await f.Repository.CaptureAsync(Text("same"), "test", Settings);
            var count = await f.Repository.ImportAsync([new(Entry("same"), Text("same")), new(Entry("new", pin: true), Text("new"))], Settings);
            Equal(1, count); Equal(2, f.Repository.Snapshot.Count); True(f.Repository.Snapshot.Single(e => e.Title == "new").IsPinned);
        });
        TestAsync("Over-quota import is atomic and does not evict originals", async () =>
        {
            using var f = new Fixture(); await f.Open();
            await f.Repository.CaptureAsync(Text("original"), "test", Settings);
            var items = new BackupItem[] { new(Entry("one"), Text("one")), new(Entry("two"), Text("two")) };
            await ThrowsAsync<InvalidOperationException>(() => f.Repository.ImportAsync(items, Settings with { HistoryCapacity = 2 }));
            Equal("original", f.Repository.Snapshot.Single().Title);
            Equal(1, (await f.Reopen()).Snapshot.Count);
            Equal(1, Directory.GetFiles(Path.Combine(f.Root, "content"), "*.pdv").Length);
        });
        TestAsync("Encrypted settings round-trip", async () =>
        {
            using var f = new Fixture();
            await f.Store.SaveSettingsAsync(Settings with { Theme = "Dark", ExcludedApps = ["test.exe"] });
            var restored = await f.Store.LoadSettingsAsync();
            Equal("Dark", restored.Theme); Equal("test", restored.ExcludedApps.Single());
        });
        TestAsync("Atomic writer keeps destination on cancellation", async () =>
        {
            using var f = new Fixture(); var path = Path.Combine(f.Root, "atomic.bin");
            await AtomicFile.WriteAsync(path, [1, 2, 3]);
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            await ThrowsAsync<OperationCanceledException>(() => AtomicFile.WriteAsync(path, [4, 5, 6], cancellation.Token));
            var restored = await File.ReadAllBytesAsync(path);
            True(new byte[] { 1, 2, 3 }.AsSpan().SequenceEqual(restored));
            Equal(0, Directory.GetFiles(f.Root, "*.tmp", SearchOption.AllDirectories).Length);
        });
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "PasteDock-tests-" + Guid.NewGuid().ToString("N"));
        public TestProtector Protector { get; } = new();
        public EncryptedFileStore Store { get; }
        public HistoryRepository Repository { get; }
        private readonly TimeProvider? _clock;
        public Fixture(TimeProvider? clock = null)
        { _clock = clock; Store = new EncryptedFileStore(Root, Protector); Repository = new HistoryRepository(Store, clock); }
        public Task Open() => Repository.InitializeAsync();
        public async Task<HistoryRepository> Reopen()
        {
            var repository = new HistoryRepository(new EncryptedFileStore(Root, Protector), _clock);
            await repository.InitializeAsync(); return repository;
        }
        public void Dispose() { Protector.Dispose(); try { Directory.Delete(Root, true); } catch (IOException) { } }
    }

    /// <summary>Test-only portable protector. Production uses current-user Windows DPAPI, not this key.</summary>
    private sealed class TestProtector : IContentProtector, IDisposable
    {
        private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
        public int ProtectCalls { get; private set; }
        public int FailOnProtectCall { get; set; } = -1;
        public byte[] Protect(byte[] plaintext)
        {
            ProtectCalls++;
            if (ProtectCalls == FailOnProtectCall) throw new IOException("Injected storage fault.");
            var result = new byte[12 + 16 + plaintext.Length];
            RandomNumberGenerator.Fill(result.AsSpan(0, 12));
            using var aes = new AesGcm(_key, 16);
            aes.Encrypt(result.AsSpan(0, 12), plaintext, result.AsSpan(28), result.AsSpan(12, 16));
            return result;
        }
        public byte[] Unprotect(byte[] ciphertext)
        {
            if (ciphertext.Length < 28) throw new CryptographicException("Invalid test ciphertext.");
            var result = new byte[ciphertext.Length - 28];
            using var aes = new AesGcm(_key, 16);
            aes.Decrypt(ciphertext.AsSpan(0, 12), ciphertext.AsSpan(28), ciphertext.AsSpan(12, 16), result);
            return result;
        }
        public void Dispose() => CryptographicOperations.ZeroMemory(_key);
    }
}
