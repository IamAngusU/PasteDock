using PasteDock.Core.Models;
using PasteDock.Core.Search;
using PasteDock.Core.Services;

namespace PasteDock.Core.Tests;

internal static partial class Program
{
    private static void RegisterV050()
    {
        Test("Quoted operator-shaped text stays literal", () =>
        {
            var query = SearchQuery.Parse("\"app:literal\" \"#tag\" \"is:pinned\"");
            Equal(null, query.App); Equal(0, query.Tags.Length); True(!query.PinnedOnly);
            Equal("app:literal", query.Terms[0]); Equal("#tag", query.Terms[1]);
        });
        Test("Negative quoted phrases remain complete exclusions", () =>
        {
            var query = SearchQuery.Parse("release -\"old version\" -\"app:literal\"");
            Equal("release", query.Terms.Single()); Equal("old version", query.ExcludedTerms[0]);
            Equal("app:literal", query.ExcludedTerms[1]); Equal(0, query.ExcludedApps.Length);
        });
        Test("A quoted leading minus is literal content", () =>
        {
            var query = SearchQuery.Parse("\"-draft\"");
            Equal("-draft", query.Terms.Single()); Equal(0, query.ExcludedTerms.Length);
        });
        Test("Source filter quotes spaces quotes and backslashes safely", () =>
        {
            var app = "Studio \\\"project\\\" \\ tools";
            Equal(app, SearchQuery.Parse(SearchQuery.AppFilter(app)).App);
        });
        Test("Incomplete and numeric type operators stay searchable", () =>
        {
            var query = SearchQuery.Parse("app: type:3 unknown:value");
            Equal(3, query.Terms.Length); Equal(null, query.App); Equal(null, query.Kind);
        });
        Test("Negative app tag and type filters combine case insensitively", () =>
        {
            var query = SearchQuery.Parse("-app:BROWSER -#archive -type:image");
            True(!SearchMatcher.MatchesMetadata(Entry() with { SourceApp = "Browser" }, query));
            True(!SearchMatcher.MatchesMetadata(Entry() with { Tags = ["Archive"] }, query));
            True(!SearchMatcher.MatchesMetadata(Entry() with { Kind = ClipKind.Image }, query));
            True(SearchMatcher.MatchesMetadata(Entry(), query));
        });
        Test("Contradictory metadata filters produce no matches", () =>
        {
            var query = SearchQuery.Parse("is:pinned -is:pinned");
            True(!SearchMatcher.MatchesMetadata(Entry(pin: true), query));
            True(!SearchMatcher.MatchesMetadata(Entry(), query));
        });
        Test("Snippet filters match only the exact local source", () =>
        {
            var clip = Entry() with { SourceApp = "PasteDock" };
            True(SearchMatcher.MatchesMetadata(clip, SearchQuery.Parse("is:snippet")));
            True(!SearchMatcher.MatchesMetadata(clip, SearchQuery.Parse("-is:snippet")));
            True(!SearchMatcher.MatchesMetadata(clip with { SourceApp = "PasteDock helper" }, SearchQuery.Parse("is:snippet")));
        });
        Test("Exclusions also inspect names and tags", () =>
        {
            var query = SearchQuery.Parse("-draft");
            True(!SearchMatcher.MatchesText(Entry() with { Title = "Draft outline" }, query));
            True(!SearchMatcher.MatchesText(Entry() with { Tags = ["draft"] }, query));
        });
        TestAsync("Deep exclusions inspect content beyond the instant index", async () =>
        {
            using var fixture = new Fixture(); await fixture.Open();
            await fixture.Repository.CaptureAsync(Text("alpha " + new string('x', 5000) + " forbidden"), "Editor", Settings);
            var query = SearchQuery.Parse("alpha -forbidden");
            Equal(1, (await fixture.Repository.SearchAsync(query)).Count);
            Equal(0, (await fixture.Repository.SearchAsync(query with { DeepSearch = true })).Count);
        });
        TestAsync("Negative-only deep search reads the full payload", async () =>
        {
            using var fixture = new Fixture(); await fixture.Open();
            await fixture.Repository.CaptureAsync(Text(new string('x', 5000) + " unwanted"), "Editor", Settings);
            Equal(0, (await fixture.Repository.SearchAsync(SearchQuery.Parse("-unwanted") with { DeepSearch = true })).Count);
        });
        Test("Most copied order preserves pins and ranks actual counts", () =>
        {
            var pinned = Entry("pinned", pin: true); var frequent = Entry("frequent") with { CopyCount = 8 };
            var ordered = SearchOrdering.Apply([Entry(), frequent, pinned], new SearchQuery(), SearchSort.MostCopied);
            Equal(pinned.Id, ordered[0].Id); Equal(frequent.Id, ordered[1].Id);
        });
        Test("Newest order uses creation rather than re-copy time", () =>
        {
            var old = Entry("old", daysOld: 3) with { LastCopiedAt = Now.AddHours(1) };
            var recent = Entry("recent");
            Equal(recent.Id, SearchOrdering.Apply([old, recent], new SearchQuery(), SearchSort.Newest)[0].Id);
            Equal(old.Id, SearchOrdering.Apply([old, recent], new SearchQuery(), SearchSort.RecentlyCopied)[0].Id);
        });
        Test("Largest order uses complete byte counts", () =>
        {
            var large = Entry("small label", bytes: 10000); var small = Entry("longer label", bytes: 10);
            Equal(large.Id, SearchOrdering.Apply([small, large], new SearchQuery(), SearchSort.Largest)[0].Id);
        });
        Test("Best match ranks an exact title before a newer partial match", () =>
        {
            var exact = Entry("release", daysOld: 1); var partial = Entry("release draft");
            Equal(exact.Id, SearchOrdering.Apply([partial, exact], SearchQuery.Parse("release"))[0].Id);
        });
        Test("Identical sort keys use stable identity ordering", () =>
        {
            var first = Entry() with { Id = new Guid("00000000-0000-0000-0000-000000000001") };
            var second = first with { Id = new Guid("00000000-0000-0000-0000-000000000002") };
            Equal(first.Id, SearchOrdering.Apply([second, first], new SearchQuery())[0].Id);
        });
        Test("Repeated queued clips have separate completion tickets", () =>
        {
            var queue = new PasteQueue(); var id = Guid.NewGuid(); queue.Add([id, id]);
            var before = queue.Snapshot; True(before[0].Ticket != before[1].Ticket);
            True(queue.Complete(before[0].Ticket)); True(!queue.Complete(before[0].Ticket));
            Equal(1, queue.Count); Equal(before[1].Ticket, queue.Snapshot[0].Ticket);
        });
        Test("Queue reordering cannot let a stale completion remove the new head", () =>
        {
            var queue = new PasteQueue(); queue.Add([Guid.NewGuid(), Guid.NewGuid()]);
            var first = queue.Snapshot[0]; var second = queue.Snapshot[1];
            True(queue.Move(second.Ticket, -1)); True(!queue.Complete(first.Ticket));
            Equal(second.Ticket, queue.Snapshot[0].Ticket);
        });
        Test("Removing one duplicate leaves the other queued", () =>
        {
            var queue = new PasteQueue(); var id = Guid.NewGuid(); queue.Add([id, id]);
            var second = queue.Snapshot[1]; True(queue.Remove(queue.Snapshot[0].Ticket));
            Equal(second.Ticket, queue.Snapshot[0].Ticket);
        });
        Test("Queue overflow is atomic and preserves all prior tickets", () =>
        {
            var queue = new PasteQueue(); queue.Add(Enumerable.Repeat(Guid.NewGuid(), PasteQueue.Capacity));
            var head = queue.Snapshot[0];
            Throws<InvalidOperationException>(() => queue.Add([Guid.NewGuid()]));
            Equal(PasteQueue.Capacity, queue.Count); Equal(head.Ticket, queue.Snapshot[0].Ticket);
        });
        Test("Invalid queued identities reject the whole batch", () =>
        {
            var queue = new PasteQueue();
            Throws<ArgumentException>(() => queue.Add([Guid.NewGuid(), Guid.Empty])); Equal(0, queue.Count);
        });
        Test("Queue boundaries and snapshots do not mutate the queue", () =>
        {
            var queue = new PasteQueue(); queue.Add([Guid.NewGuid()]); var item = queue.Snapshot[0];
            True(!queue.Move(item.Ticket, -1)); True(!queue.Move(item.Ticket, 1));
            var snapshot = (QueuedClip[])queue.Snapshot; snapshot[0] = new(Guid.NewGuid(), Guid.NewGuid());
            Equal(item.Ticket, queue.Snapshot[0].Ticket);
        });
        TestAsync("Renaming preserves full payload identity and encryption", async () =>
        {
            using var fixture = new Fixture(); await fixture.Open();
            var original = Text("Complete payload with more than a display title.");
            var clip = await fixture.Repository.CaptureAsync(original, "Editor", Settings);
            await fixture.Repository.RenameAsync(clip.Id, "Deployment notes");
            var renamed = fixture.Repository.Snapshot.Single();
            Equal(clip.BlobId, renamed.BlobId); Equal(clip.Fingerprint, renamed.Fingerprint); True(renamed.HasCustomTitle);
            Equal(original.Text, (await fixture.Repository.LoadAsync(clip.Id)).Text);
            Equal("Deployment notes", (await fixture.Reopen()).Snapshot.Single().Title);
        });
        TestAsync("Custom names survive editing and duplicate re-copy", async () =>
        {
            using var fixture = new Fixture(); await fixture.Open();
            var clip = await fixture.Repository.CaptureAsync(Text("original"), "Editor", Settings);
            await fixture.Repository.RenameAsync(clip.Id, "Reusable snippet");
            await fixture.Repository.ReplacePayloadAsync(clip.Id, Text("edited"), Settings);
            var duplicate = await fixture.Repository.CaptureAsync(Text("edited"), "Editor", Settings);
            Equal(clip.Id, duplicate.Id); Equal("Reusable snippet", duplicate.Title); True(duplicate.HasCustomTitle);
        });
        TestAsync("Invalid rename leaves title and content unchanged", async () =>
        {
            using var fixture = new Fixture(); await fixture.Open();
            var clip = await fixture.Repository.CaptureAsync(Text("original"), "Editor", Settings);
            foreach (var value in new[] { "", " ", "two\nlines", new string('x', 151) })
                await ThrowsAsync<ArgumentException>(() => fixture.Repository.RenameAsync(clip.Id, value));
            Equal(clip.Title, fixture.Repository.Snapshot.Single().Title);
        });
        TestAsync("Failed rename commit retains the old name on disk", async () =>
        {
            using var fixture = new Fixture(); await fixture.Open();
            var clip = await fixture.Repository.CaptureAsync(Text("original"), "Editor", Settings);
            fixture.Protector.FailOnProtectCall = fixture.Protector.ProtectCalls + 1;
            await ThrowsAsync<IOException>(() => fixture.Repository.RenameAsync(clip.Id, "New name"));
            Equal(clip.Title, fixture.Repository.Snapshot.Single().Title);
            Equal(clip.Title, (await fixture.Reopen()).Snapshot.Single().Title);
        });
        TestAsync("Encrypted backup retains custom names for later edits", async () =>
        {
            using var source = new Fixture(); using var target = new Fixture(); await source.Open(); await target.Open();
            var clip = await source.Repository.CaptureAsync(Text("original"), "Editor", Settings);
            await source.Repository.RenameAsync(clip.Id, "Portable name");
            var restored = BackupService.Decrypt(BackupService.Encrypt(await source.Repository.ExportAsync(), "a long test passphrase"), "a long test passphrase");
            await target.Repository.ImportAsync(restored, Settings);
            var imported = target.Repository.Snapshot.Single();
            await target.Repository.ReplacePayloadAsync(imported.Id, Text("changed"), Settings);
            Equal("Portable name", target.Repository.Snapshot.Single().Title);
        });
        Test("CSS shorthand color expands all RGBA components", () =>
        {
            True(HexColor.TryParse("#1234", out var color)); Equal(new HexColor(17, 34, 51, 68), color);
        });
        Test("Eight-digit CSS color treats the last pair as alpha", () =>
        {
            True(HexColor.TryParse("#FF008080", out var color)); Equal(new HexColor(255, 0, 128, 128), color);
            True(HexColor.TryParse("#abc", out color)); Equal((byte)255, color.Alpha);
        });
        Test("Invalid color text never reaches the native converter", () =>
        {
            foreach (var value in new[] { "red", "#12", "#GG0000", "#12345", "url(example)" })
                True(!HexColor.TryParse(value, out _));
        });
        TestAsync("A snippet remains a snippet after capture from another app", async () =>
        {
            using var fixture = new Fixture(); await fixture.Open();
            var payload = Text("Reusable local snippet");
            var original = await fixture.Repository.CaptureAsync(payload, "PasteDock", Settings);
            var copied = await fixture.Repository.CaptureAsync(payload, "Editor", Settings);
            Equal(original.Id, copied.Id); True(copied.IsSnippet); Equal("Editor", copied.SourceApp);
            Equal(1, (await fixture.Repository.SearchAsync(SearchQuery.Parse("is:snippet"))).Count);
            await fixture.Repository.ReplacePayloadAsync(copied.Id, Text("Edited reusable snippet"), Settings);
            True(fixture.Repository.Snapshot.Single().IsSnippet);
        });
        Test("Unknown stored sort values recover to a usable default", () =>
        {
            var settings = new AppSettings { HistorySort = (SearchSort)999, PreviewWrap = false }.Normalize();
            Equal(SearchSort.BestMatch, settings.HistorySort); True(!settings.PreviewWrap);
        });
        Test("Saved snippets survive automatic age retention without a pin", () =>
        {
            var snippet = Entry("saved note", daysOld: 90) with { IsSnippet = true };
            var history = Entry("old capture", daysOld: 90);
            var plan = RetentionPolicy.Plan([history, snippet], new AppSettings { RetentionDays = 30 }, Now);
            Equal(snippet.Id, plan.Kept.Single().Id); True(plan.Removed.Any(e => e.Id == history.Id));
        });
    }
}
