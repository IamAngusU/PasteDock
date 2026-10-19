using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PasteDock.Desktop.Clipboard;
using PasteDock.Desktop.Interop;

namespace PasteDock.Desktop.Services;

internal static class NativeDiagnostics
{
    /// <summary>Read-only smoke checks. Does not replace the user's clipboard or synthesize input.</summary>
    public static async Task<int> RunAsync(string reportPath)
    {
        var checks = new List<object>();
        var failed = false;
        void Check(string name, Action action)
        {
            try { action(); checks.Add(new { name, status = "passed", detail = "" }); }
            catch (Exception exception) { failed = true; checks.Add(new { name, status = "failed", detail = exception.GetType().Name }); }
        }
        Check("Windows input structure layout", () =>
        {
            var expected = IntPtr.Size == 8 ? 40 : 28;
            if (Marshal.SizeOf<NativeMethods.Input>() != expected) throw new InvalidOperationException("INPUT layout mismatch.");
        });
        Check("Current-user DPAPI round-trip and tamper rejection", () =>
        {
            var protector = new DpapiProtector();
            var original = Encoding.UTF8.GetBytes("PasteDock diagnostic · 雪 · 🧪");
            var encrypted = protector.Protect(original);
            var restored = protector.Unprotect(encrypted);
            if (!original.AsSpan().SequenceEqual(restored)) throw new CryptographicException();
            encrypted[^1] ^= 0x40;
            try { protector.Unprotect(encrypted); throw new InvalidOperationException("Tampered data was accepted."); }
            catch (CryptographicException) { }
            CryptographicOperations.ZeroMemory(original); CryptographicOperations.ZeroMemory(restored);
        });
        checks.Add(new { name = "Interactive Default desktop", status = NativeMethods.IsInteractiveDesktop() ? "available" : "unavailable", detail = "Informational only; no input is sent." });
        Check("Clipboard sequence API", () => { _ = NativeMethods.GetClipboardSequenceNumber(); });
        try
        {
            await using var clipboard = new WindowsClipboardService();
            clipboard.SetPaused(true); // Do not persist, inspect or export the user's clipboard.
            await clipboard.StartAsync(new AppSettings());
            checks.Add(new { name = "STA clipboard listener registration", status = "passed", detail = "" });
        }
        catch (Exception exception)
        { failed = true; checks.Add(new { name = "STA clipboard listener registration", status = "failed", detail = exception.GetType().Name }); }
        try
        {
            var languages = Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages.Select(l => l.LanguageTag).ToArray();
            checks.Add(new { name = "Installed offline OCR languages", status = languages.Length > 0 ? "available" : "unavailable", detail = string.Join(", ", languages) });
        }
        catch (Exception exception)
        { checks.Add(new { name = "Installed offline OCR languages", status = "unavailable", detail = exception.GetType().Name }); }
        if (!await PasteDiagnostics.RunAsync(checks)) failed = true;
        var directory = Path.GetDirectoryName(Path.GetFullPath(reportPath));
        if (directory is not null) Directory.CreateDirectory(directory);
        await ExportService.WriteTextAsync(reportPath, JsonSerializer.Serialize(new
        {
            timestampUtc = DateTimeOffset.UtcNow, status = failed ? "failed" : "passed",
            runtime = RuntimeInformation.FrameworkDescription,
            os = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            clipboardModified = false, inputSynthesized = false, checks
        }, new JsonSerializerOptions { WriteIndented = true }));
        return failed ? 1 : 0;
    }
}
