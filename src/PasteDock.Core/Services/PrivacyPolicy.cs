using System.Text.RegularExpressions;
using PasteDock.Core.Models;

namespace PasteDock.Core.Services;

public readonly record struct CaptureDecision(bool Allowed, string Reason)
{
    public static CaptureDecision Permit => new(true, "");
    public static CaptureDecision Skip(string reason) => new(false, reason);
}

public static partial class PrivacyPolicy
{
    public static CaptureDecision Evaluate(ClipPayload payload, string sourceApp, AppSettings settings,
        bool paused = false, bool sessionLocked = false, bool sensitiveFormat = false)
    {
        if (paused) return CaptureDecision.Skip("Capture is paused.");
        if (sessionLocked) return CaptureDecision.Skip("Windows is locked.");
        if (sensitiveFormat) return CaptureDecision.Skip("The source marked this clipboard item as private.");
        var app = Path.GetFileNameWithoutExtension(sourceApp);
        if (settings.ExcludedApps.Contains(app, StringComparer.OrdinalIgnoreCase))
            return CaptureDecision.Skip("An excluded application copied an item.");
        if (!settings.CaptureImages && payload.Png is not null) return CaptureDecision.Skip("Image capture is disabled.");
        if (!settings.CaptureFiles && payload.Files.Length > 0) return CaptureDecision.Skip("File capture is disabled.");
        if (payload.GetByteCount() > settings.MaxItemMb * 1024L * 1024)
            return CaptureDecision.Skip("An item exceeded the size limit. No truncated copy was saved.");
        if (settings.BlockRecognizableSecrets)
        {
            // This is deliberately conservative, not a promise to recognize every credential.
            foreach (var text in new[] { payload.Text, payload.Html, payload.Rtf, payload.OcrText })
            {
                if (text is null) continue;
                try { if (SecretPattern().IsMatch(text)) return CaptureDecision.Skip("A recognizable secret was not saved."); }
                catch (RegexMatchTimeoutException) { return CaptureDecision.Skip("Secret inspection timed out; capture was skipped."); }
            }
        }
        return CaptureDecision.Permit;
    }

    [GeneratedRegex(@"-----BEGIN (?:[A-Z0-9 ]* )?PRIVATE KEY-----|\b(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{40,}|AKIA[A-Z0-9]{16})\b", RegexOptions.CultureInvariant, 150)]
    private static partial Regex SecretPattern();
}
