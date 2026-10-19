using PasteDock.Core.Models;
using PasteDock.Core.Services;

namespace PasteDock.Core.Tests;

internal static partial class Program
{
    private static void RegisterV040()
    {
        Test("File reference check accepts a mixed file and directory selection", () =>
        {
            using var fixture = new Fixture();
            var file = Path.Combine(fixture.Root, "example.txt"); File.WriteAllText(file, "content");
            FileReferencePolicy.EnsureAvailable([file, fixture.Root]);
        });
        Test("Missing file references reject the whole selection with an actionable error", () =>
        {
            try { FileReferencePolicy.EnsureAvailable(["exists", "missing", "also-missing"], p => p == "exists"); }
            catch (InvalidOperationException error)
            {
                True(error.Message.StartsWith("2 file references are", StringComparison.Ordinal));
                True(error.Message.Contains("plain text", StringComparison.Ordinal)); return;
            }
            throw new Exception("A partially missing selection must be rejected.");
        });
        Test("Cancelled file inspection never probes the filesystem", () =>
        {
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            var probes = 0;
            Throws<OperationCanceledException>(() => FileReferencePolicy.EnsureAvailable(["test"], _ => { probes++; return true; }, cancellation.Token));
            Equal(0, probes);
        });
        Test("File inspection stops between references after cancellation", () =>
        {
            using var cancellation = new CancellationTokenSource(); var probes = 0;
            Throws<OperationCanceledException>(() => FileReferencePolicy.EnsureAvailable(["first", "second"], _ =>
            { probes++; cancellation.Cancel(); return true; }, cancellation.Token));
            Equal(1, probes);
        });
        Test("Text-only clips do not need a filesystem probe", () =>
            FileReferencePolicy.EnsureAvailable([], _ => throw new Exception("Unexpected probe.")));
        Test("Unified diff has portable LF boundaries for Windows and Unix inputs", () =>
        {
            var windows = TextDiff.Compare("alpha\r\nbeta", "alpha\r\ngamma").ToUnifiedText();
            var unix = TextDiff.Compare("alpha\nbeta", "alpha\ngamma").ToUnifiedText();
            Equal(windows, unix); True(!windows.Contains('\r'));
            True(windows.Contains("\n-beta\n+gamma\n", StringComparison.Ordinal));
        });
        TestAsync("Missing file clips remain recoverable after reopening history", async () =>
        {
            using var fixture = new Fixture();
            var path = Path.Combine(fixture.Root, "source.txt"); File.WriteAllText(path, "original");
            await fixture.Open();
            var entry = await fixture.Repository.CaptureAsync(new ClipPayload { Files = [path] }, "Explorer", Settings);
            File.Delete(path);
            var reopened = new HistoryRepository(fixture.Store); await reopened.InitializeAsync();
            var payload = await reopened.LoadAsync(entry.Id);
            Throws<InvalidOperationException>(() => FileReferencePolicy.EnsureAvailable(payload.Files));
            Equal(path, payload.GetPlainText()); Equal(1, reopened.Snapshot.Count);
        });
    }
}
