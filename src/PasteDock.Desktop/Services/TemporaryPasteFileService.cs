using PasteDock.Core.Services;

namespace PasteDock.Desktop.Services;

/// <summary>
/// Materializes clipboard content as a short-lived local file so Windows targets that accept
/// file drops can receive it through the normal clipboard. Files are never uploaded and are
/// cleaned on later app starts/maintenance after the retention window.
/// </summary>
internal sealed class TemporaryPasteFileService
{
    public static readonly TimeSpan Retention = TimeSpan.FromHours(24);
    private readonly string _root;

    public TemporaryPasteFileService(string workspaceRoot)
    {
        _root = Path.Combine(workspaceRoot, "paste-files");
    }

    public async Task<string> MaterializeAsync(ClipPayload payload, ClipKind kind, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        payload.Validate();
        if (payload.Files.Length > 0)
            throw new InvalidOperationException("This clip already contains file references. Paste it normally instead.");

        Directory.CreateDirectory(_root);
        CleanupBestEffort();
        var stem = $"PasteDock-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..45];
        string path;
        if (payload.Png is not null)
        {
            path = Path.Combine(_root, stem + ".png");
            await ExportService.WriteBytesAsync(path, payload.Png, ct).ConfigureAwait(false);
        }
        else if (kind == ClipKind.Json)
        {
            path = Path.Combine(_root, stem + ".json");
            await ExportService.WriteTextAsync(path, payload.GetPlainText(), ct).ConfigureAwait(false);
        }
        else if (payload.Text is null && !string.IsNullOrEmpty(payload.Html))
        {
            path = Path.Combine(_root, stem + ".html");
            await ExportService.WriteTextAsync(path, payload.Html, ct).ConfigureAwait(false);
        }
        else
        {
            var text = payload.GetPlainText();
            var extension = TextFileFormatGuesser.GuessExtension(text, kind);
            path = Path.Combine(_root, stem + extension);
            await ExportService.WriteTextAsync(path, text, ct).ConfigureAwait(false);
        }
        MarkTemporaryBestEffort(path);
        return path;
    }

    private static void MarkTemporaryBestEffort(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            File.SetAttributes(path, attributes | FileAttributes.Temporary | FileAttributes.NotContentIndexed);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static DateTime CreatedUtc(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        const string prefix = "PasteDock-";
        const int stampLength = 15; // yyyyMMdd-HHmmss
        if (name.StartsWith(prefix, StringComparison.Ordinal) && name.Length >= prefix.Length + stampLength
            && DateTime.TryParseExact(name.AsSpan(prefix.Length, stampLength), "yyyyMMdd-HHmmss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var timestamp))
            return timestamp;
        return File.GetCreationTimeUtc(path);
    }

    public void CleanupBestEffort()
    {
        try
        {
            if (!Directory.Exists(_root)) return;
            var threshold = DateTime.UtcNow - Retention;
            foreach (var path in Directory.EnumerateFiles(_root, "PasteDock-*", SearchOption.TopDirectoryOnly))
            {
                try { if (CreatedUtc(path) < threshold) File.Delete(path); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
