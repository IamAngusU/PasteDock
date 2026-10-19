namespace PasteDock.Core.Models;

public sealed record AppSettings
{
    public int SchemaVersion { get; init; } = 1;
    public int HistoryCapacity { get; init; } = 2000;
    public int RetentionDays { get; init; } = 30;
    public int StorageLimitMb { get; init; } = 512;
    public int MaxItemMb { get; init; } = 64;
    public string[] ExcludedApps { get; init; } = ["1Password", "Bitwarden", "KeePass", "KeePassXC", "ProtonPass"];
    public string[] PlainTextPasteApps { get; init; } = [];
    public bool BlockRecognizableSecrets { get; init; } = true;
    public bool PreventCloudAndSystemHistory { get; init; } = true;
    public bool CaptureImages { get; init; } = true;
    public bool CaptureFiles { get; init; } = true;
    public bool StartWithWindows { get; init; }
    public bool KeepWindowOnTop { get; init; } = true;
    public bool ReducedMotion { get; init; }
    public string Theme { get; init; } = "Light";
    public uint HotkeyModifiers { get; init; } = 3; // MOD_ALT | MOD_CONTROL
    public uint HotkeyKey { get; init; } = 0x56;    // V
    public string HotkeyLabel { get; init; } = "Ctrl + Alt + V";
    public string JoinSeparator { get; init; } = "\n\n";
    public double WindowWidth { get; init; } = 1080;
    public double WindowHeight { get; init; } = 700;
    public double ListWidth { get; init; } = 350;

    public AppSettings Normalize() => this with
    {
        HistoryCapacity = Math.Clamp(HistoryCapacity, 100, Limits.MaxItems),
        RetentionDays = Math.Clamp(RetentionDays, 0, 3650),
        StorageLimitMb = Math.Clamp(StorageLimitMb, 64, 4096),
        MaxItemMb = Math.Clamp(MaxItemMb, 1, 64),
        ExcludedApps = NormalizeApps(ExcludedApps),
        PlainTextPasteApps = NormalizeApps(PlainTextPasteApps),
        Theme = Theme == "Dark" ? "Dark" : "Light",
        JoinSeparator = (JoinSeparator ?? "\n\n")[..Math.Min(JoinSeparator?.Length ?? 2, 80)],
        WindowWidth = double.IsFinite(WindowWidth) ? Math.Clamp(WindowWidth, 860, 2200) : 1080,
        WindowHeight = double.IsFinite(WindowHeight) ? Math.Clamp(WindowHeight, 560, 1500) : 700,
        ListWidth = double.IsFinite(ListWidth) ? Math.Clamp(ListWidth, 280, 600) : 350
    };

    private static string[] NormalizeApps(string[]? apps) => (apps ?? [])
        .Where(s => !string.IsNullOrWhiteSpace(s))
        .Select(s => Path.GetFileNameWithoutExtension(s.Trim()))
        .Where(s => s.Length is > 0 and <= 128)
        .Distinct(StringComparer.OrdinalIgnoreCase).Take(256).ToArray();
}
