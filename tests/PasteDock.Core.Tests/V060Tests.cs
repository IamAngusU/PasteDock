using System.Security.Cryptography;
using System.Text;
using PasteDock.Core.Models;
using PasteDock.Core.Services;
using PasteDock.Core.Storage;

namespace PasteDock.Core.Tests;

internal static partial class Program
{
    private static EditorDraft Draft(string text = "An unfinished local draft", ClipEntry? original = null) =>
        new(Guid.NewGuid(), original?.Id, original?.BlobId, text, Now);

    private sealed class PausingProtector(IContentProtector inner) : IContentProtector, IDisposable
    {
        private int _first = 1;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Resume { get; } = new();
        public byte[] Protect(byte[] plaintext)
        {
            if (Interlocked.Exchange(ref _first, 0) == 1)
            {
                Entered.TrySetResult();
                if (!Resume.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Synthetic storage gate was not released.");
            }
            return inner.Protect(plaintext);
        }
        public byte[] Unprotect(byte[] ciphertext) => inner.Unprotect(ciphertext);
        public void Dispose() => Resume.Dispose();
    }

    private static void RegisterV060()
    {
        TestAsync("Encrypted editor recovery survives reopening with exact Unicode", async () =>
        {
            using var f = new Fixture(); var clock = new ManualTimeProvider(Now);
            var draft = Draft("Überarbeitet\r\n日本語 😀\t");
            await new DraftRepository(f.Store, clock).SaveAsync(draft, Settings);
            var restored = await new DraftRepository(f.Store, clock).LoadAsync(Settings);
            Equal(draft, restored);
            True(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(f.Root, "editor-draft.pdv"))).Contains("Überarbeitet"));
        });
        TestAsync("Recovery expires at exactly seven days", async () =>
        {
            using var f = new Fixture(); var clock = new ManualTimeProvider(Now); var drafts = new DraftRepository(f.Store, clock);
            await drafts.SaveAsync(Draft(), Settings); clock.Advance(EditorDraft.Lifetime - TimeSpan.FromTicks(1));
            True(await drafts.LoadAsync(Settings) is not null); clock.Advance(TimeSpan.FromTicks(1));
            Equal(null, await drafts.LoadAsync(Settings)); True(!File.Exists(Path.Combine(f.Root, "editor-draft.pdv")));
        });
        TestAsync("A newer draft revision restarts the recovery lifetime", async () =>
        {
            using var f = new Fixture(); var clock = new ManualTimeProvider(Now); var drafts = new DraftRepository(f.Store, clock);
            var draft = Draft(); await drafts.SaveAsync(draft, Settings); clock.Advance(TimeSpan.FromDays(6));
            await drafts.SaveAsync(draft with { Text = "Revised" }, Settings); clock.Advance(TimeSpan.FromDays(2));
            Equal("Revised", (await drafts.LoadAsync(Settings))!.Text);
        });
        TestAsync("Recognizable secrets remove the previous recoverable revision", async () =>
        {
            using var f = new Fixture(); var drafts = new DraftRepository(f.Store);
            await drafts.SaveAsync(Draft(), Settings);
            Equal(DraftSaveStatus.Blocked, await drafts.SaveAsync(Draft("ghp_" + new string('x', 36)), Settings));
            Equal(null, await drafts.LoadAsync(Settings));
        });
        TestAsync("Recovery opt-out deletes the old copy", async () =>
        {
            using var f = new Fixture(); var drafts = new DraftRepository(f.Store); await drafts.SaveAsync(Draft(), Settings);
            Equal(DraftSaveStatus.Disabled, await drafts.SaveAsync(Draft(), Settings with { RecoverEditorDrafts = false }));
            Equal(null, await drafts.LoadAsync(Settings));
        });
        TestAsync("Oversized editor recovery is refused without stale fallback", async () =>
        {
            using var f = new Fixture(); var drafts = new DraftRepository(f.Store); await drafts.SaveAsync(Draft(), Settings);
            Equal(DraftSaveStatus.TooLarge, await drafts.SaveAsync(Draft(new string('x', EditorDraft.MaxCharacters + 1)), Settings));
            Equal(null, await drafts.LoadAsync(Settings));
        });
        TestAsync("Blank draft removes its encrypted recovery copy", async () =>
        {
            using var f = new Fixture(); var drafts = new DraftRepository(f.Store); await drafts.SaveAsync(Draft(), Settings);
            Equal(DraftSaveStatus.Empty, await drafts.SaveAsync(Draft("\t \r\n"), Settings)); Equal(null, await drafts.LoadAsync(Settings));
        });
        TestAsync("Recovery applies the current maximum item bytes", async () =>
        {
            using var f = new Fixture(); var drafts = new DraftRepository(f.Store);
            Equal(DraftSaveStatus.Blocked, await drafts.SaveAsync(Draft(new string('界', 400_000)), Settings with { MaxItemMb = 1 }));
            Equal(null, await drafts.LoadAsync(Settings));
        });
        TestAsync("Startup opt-out clears an existing encrypted draft", async () =>
        {
            using var f = new Fixture(); var drafts = new DraftRepository(f.Store); await drafts.SaveAsync(Draft(), Settings);
            Equal(null, await drafts.LoadAsync(Settings with { RecoverEditorDrafts = false }));
            Equal(null, await drafts.LoadAsync(Settings));
        });
        TestAsync("Recovery rechecks tightened privacy rules on load", async () =>
        {
            using var f = new Fixture(); var drafts = new DraftRepository(f.Store);
            await drafts.SaveAsync(Draft("-----BEGIN PRIVATE KEY-----\nsynthetic"), Settings with { BlockRecognizableSecrets = false });
            Equal(null, await drafts.LoadAsync(Settings));
        });
        TestAsync("Unreadable recovery data is preserved for an explicit decision", async () =>
        {
            using var f = new Fixture(); var path = Path.Combine(f.Root, "editor-draft.pdv"); File.WriteAllBytes(path, [1, 2, 3]);
            await ThrowsAsync<CryptographicException>(() => new DraftRepository(f.Store).LoadAsync(Settings));
            True(File.ReadAllBytes(path).SequenceEqual(new byte[] { 1, 2, 3 }));
        });
        TestAsync("Unknown draft versions are not silently replaced", async () =>
        {
            using var f = new Fixture(); var path = Path.Combine(f.Root, "editor-draft.pdv");
            var bytes = f.Protector.Protect(Encoding.UTF8.GetBytes("{\"Version\":99,\"Draft\":null}")); File.WriteAllBytes(path, bytes);
            await ThrowsAsync<InvalidDataException>(() => new DraftRepository(f.Store).LoadAsync(Settings));
            True(File.ReadAllBytes(path).SequenceEqual(bytes));
        });
        TestAsync("Cancelled recovery writes preserve the committed draft", async () =>
        {
            using var f = new Fixture(); var drafts = new DraftRepository(f.Store); await drafts.SaveAsync(Draft("Before"), Settings);
            using var ct = new CancellationTokenSource(); ct.Cancel();
            await ThrowsAsync<OperationCanceledException>(() => drafts.SaveAsync(Draft("After"), Settings, ct.Token));
            Equal("Before", (await drafts.LoadAsync(Settings))!.Text);
        });
        TestAsync("Failed encryption keeps the previous recovery revision readable", async () =>
        {
            using var f = new Fixture(); var drafts = new DraftRepository(f.Store); await drafts.SaveAsync(Draft("Before"), Settings);
            f.Protector.FailOnProtectCall = f.Protector.ProtectCalls + 1;
            await ThrowsAsync<IOException>(() => drafts.SaveAsync(Draft("After"), Settings));
            Equal("Before", (await drafts.LoadAsync(Settings))!.Text);
        });
        TestAsync("A queued discard wins over an in-flight draft write", async () =>
        {
            using var f = new Fixture(); var drafts = new DraftRepository(f.Store);
            var writing = drafts.SaveAsync(Draft(new string('x', 100_000)), Settings);
            var clearing = drafts.ClearAsync(); await Task.WhenAll(writing, clearing);
            Equal(null, await drafts.LoadAsync(Settings));
        });
        TestAsync("Queued autosave rechecks privacy settings after waiting for storage", async () =>
        {
            using var f = new Fixture(); using var protector = new PausingProtector(f.Protector);
            var drafts = new DraftRepository(new EncryptedFileStore(f.Root, protector));
            var currentSettings = Settings;
            var first = Task.Run(() => drafts.SaveCurrentAsync(Draft("First revision"), () => currentSettings));
            try
            {
                await protector.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                var queued = drafts.SaveCurrentAsync(Draft("Queued revision"), () => currentSettings);
                currentSettings = Settings with { RecoverEditorDrafts = false };
                protector.Resume.Set(); await first;
                Equal(DraftSaveStatus.Disabled, await queued); Equal(null, await drafts.LoadAsync(Settings));
            }
            finally { protector.Resume.Set(); await first; }
        });
        TestAsync("Invalid base identities cannot replace a recoverable draft", async () =>
        {
            using var f = new Fixture(); var drafts = new DraftRepository(f.Store); await drafts.SaveAsync(Draft("Before"), Settings);
            await ThrowsAsync<InvalidDataException>(() => drafts.SaveAsync(Draft("After") with { ClipId = Guid.NewGuid() }, Settings));
            Equal("Before", (await drafts.LoadAsync(Settings))!.Text);
        });
        TestAsync("Recovery file size is bounded before decryption", async () =>
        {
            using var f = new Fixture(); var path = Path.Combine(f.Root, "editor-draft.pdv");
            using (var file = File.Create(path)) file.SetLength(8L * 1024 * 1024 + 1);
            await ThrowsAsync<InvalidDataException>(() => new DraftRepository(f.Store).LoadAsync(Settings)); True(File.Exists(path));
        });
        TestAsync("Intentional snippets with identical text keep separate identities", async () =>
        {
            using var f = new Fixture(); await f.Open();
            var a = await f.Repository.CreateSnippetAsync(Text("Same text"), Settings);
            var b = await f.Repository.CreateSnippetAsync(Text("Same text"), Settings);
            True(a.Id != b.Id && a.SnippetIdentity != b.SnippetIdentity); Equal(2, f.Repository.Snapshot.Count);
        });
        TestAsync("Backup import preserves identical named snippets and remains idempotent", async () =>
        {
            using var f = new Fixture(); await f.Open(); using var target = new Fixture(); await target.Open();
            var a = await f.Repository.CreateSnippetAsync(Text("Same text"), Settings);
            var b = await f.Repository.CreateSnippetAsync(Text("Same text"), Settings);
            await f.Repository.RenameAsync(a.Id, "First variant"); await f.Repository.RenameAsync(b.Id, "Second variant");
            var items = await f.Repository.ExportAsync();
            Equal(2, await target.Repository.ImportAsync(items, Settings)); Equal(0, await target.Repository.ImportAsync(items, Settings));
            Equal(2, target.Repository.Snapshot.Count); True(target.Repository.Snapshot.Any(e => e.Title == "Second variant"));
            Equal(0, await f.Repository.ImportAsync(await target.Repository.ExportAsync(), Settings));
        });
        TestAsync("Undo restores distinct snippets even when their content matches", async () =>
        {
            using var f = new Fixture(); await f.Open();
            var a = await f.Repository.CreateSnippetAsync(Text("Same text"), Settings);
            var b = await f.Repository.CreateSnippetAsync(Text("Same text"), Settings);
            await f.Repository.DeleteWithUndoAsync([a.Id, b.Id]); await f.Repository.UndoLastDeleteAsync(Settings);
            Equal(2, f.Repository.Snapshot.Count); True(f.Repository.Snapshot.Any(e => e.Id == a.Id));
        });
        TestAsync("Import during deletion undo does not duplicate a saved snippet", async () =>
        {
            using var f = new Fixture(); await f.Open(); var a = await f.Repository.CreateSnippetAsync(Text("Text"), Settings);
            var items = await f.Repository.ExportAsync(); await f.Repository.DeleteWithUndoAsync([a.Id]);
            Equal(1, await f.Repository.ImportAsync(items, Settings)); await f.Repository.UndoLastDeleteAsync(Settings);
            Equal(1, f.Repository.Snapshot.Count); Equal(a.SnippetIdentity, f.Repository.Snapshot.Single().SnippetIdentity);
        });
        TestAsync("Older snippet identity survives import recopy and edit", async () =>
        {
            using var f = new Fixture(); await f.Open(); var legacy = Entry("Legacy") with { SourceApp = "PasteDock", IsSnippet = false, SnippetId = null };
            Equal(1, await f.Repository.ImportAsync([new(legacy, Text("Legacy"))], Settings));
            var copy = await f.Repository.CaptureAsync(Text("Legacy"), "Editor", Settings);
            await f.Repository.ReplaceRevisionAsync(copy.Id, copy.BlobId, Text("Changed"), Settings);
            Equal(legacy.Id, f.Repository.Snapshot.Single().SnippetIdentity);
            Equal(0, await f.Repository.ImportAsync([new(legacy, Text("Legacy"))], Settings));
            Equal("Changed", (await f.Repository.LoadAsync(copy.Id)).Text);
        });
        TestAsync("Stale editor revisions cannot overwrite a newer payload", async () =>
        {
            using var f = new Fixture(); await f.Open(); var a = await f.Repository.CaptureAsync(Text("Original"), "Editor", Settings);
            var revision = await f.Repository.LoadRevisionAsync(a.Id); Equal(a.BlobId, revision.Entry.BlobId); Equal("Original", revision.Payload.Text);
            await f.Repository.ReplacePayloadAsync(a.Id, Text("Newer"), Settings);
            await ThrowsAsync<InvalidOperationException>(() => f.Repository.ReplaceRevisionAsync(a.Id, a.BlobId, Text("Stale"), Settings));
            Equal("Newer", (await f.Repository.LoadAsync(a.Id)).Text);
        });
        TestAsync("A deleted original leaves an editor save recoverable", async () =>
        {
            using var f = new Fixture(); await f.Open(); var a = await f.Repository.CaptureAsync(Text("Original"), "Editor", Settings);
            await f.Repository.DeleteAsync([a.Id]);
            await ThrowsAsync<InvalidOperationException>(() => f.Repository.ReplaceRevisionAsync(a.Id, a.BlobId, Text("Draft"), Settings));
            Equal(0, f.Repository.Snapshot.Count);
        });
        TestAsync("Metadata-only updates do not create false edit conflicts", async () =>
        {
            using var f = new Fixture(); await f.Open(); var a = await f.Repository.CaptureAsync(Text("Original"), "Editor", Settings);
            await f.Repository.RenameAsync(a.Id, "Durable name");
            await f.Repository.ReplaceRevisionAsync(a.Id, a.BlobId, Text("Changed"), Settings);
            Equal("Durable name", f.Repository.Snapshot.Single().Title);
        });
        TestAsync("Manual snippet saves never silently evict other clips", async () =>
        {
            using var f = new Fixture(); await f.Open(); var a = await f.Repository.CaptureAsync(Text("Original"), "Editor", Settings);
            await ThrowsAsync<InvalidOperationException>(() => f.Repository.CreateSnippetAsync(Text("New"), Settings with { HistoryCapacity = 1 }));
            Equal(a.Id, f.Repository.Snapshot.Single().Id);
        });
        TestAsync("Manual snippet saves apply the same secret filter", async () =>
        {
            using var f = new Fixture(); await f.Open();
            await ThrowsAsync<InvalidOperationException>(() => f.Repository.CreateSnippetAsync(Text("ghp_" + new string('x', 36)), Settings));
            Equal(0, f.Repository.Snapshot.Count);
        });
        TestAsync("Undo keeps custom names durable through the next edit", async () =>
        {
            using var f = new Fixture(); await f.Open(); var a = await f.Repository.CaptureAsync(Text("Content"), "Editor", Settings);
            await f.Repository.RenameAsync(a.Id, "My name"); await f.Repository.DeleteWithUndoAsync([a.Id]);
            var copy = await f.Repository.CaptureAsync(Text("Content"), "Editor", Settings);
            await f.Repository.UndoLastDeleteAsync(Settings); await f.Repository.ReplacePayloadAsync(copy.Id, Text("Edited"), Settings);
            Equal("My name", f.Repository.Snapshot.Single().Title); True(f.Repository.Snapshot.Single().HasCustomTitle);
        });
        TestAsync("Editor recovery is excluded from portable history backups", async () =>
        {
            using var f = new Fixture(); await f.Open(); await new DraftRepository(f.Store).SaveAsync(Draft("Only in editor"), Settings);
            Equal(0, (await f.Repository.ExportAsync()).Count);
        });
    }
}
