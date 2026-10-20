using System.Globalization;

namespace PasteDock.Desktop.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private string _capacity = "2000", _days = "30", _storage = "512", _itemSize = "64", _excluded = "", _plainTargets = "", _theme = "Light", _error = "", _hotkey = "";
    private bool _captureImages, _captureFiles, _startup, _topmost, _secrets, _cloud, _motion, _recoverDrafts;
    private uint _modifiers, _key;
    public string Capacity { get => _capacity; set => Set(ref _capacity, value); }
    public string RetentionDays { get => _days; set => Set(ref _days, value); }
    public string StorageMb { get => _storage; set => Set(ref _storage, value); }
    public string MaxItemMb { get => _itemSize; set => Set(ref _itemSize, value); }
    public string ExcludedApps { get => _excluded; set => Set(ref _excluded, value); }
    public string PlainTextPasteApps { get => _plainTargets; set => Set(ref _plainTargets, value); }
    public string Theme { get => _theme; set => Set(ref _theme, value); }
    public string Error { get => _error; set => Set(ref _error, value); }
    public string HotkeyLabel { get => _hotkey; private set => Set(ref _hotkey, value); }
    public bool CaptureImages { get => _captureImages; set => Set(ref _captureImages, value); }
    public bool CaptureFiles { get => _captureFiles; set => Set(ref _captureFiles, value); }
    public bool StartWithWindows { get => _startup; set => Set(ref _startup, value); }
    public bool KeepOnTop { get => _topmost; set => Set(ref _topmost, value); }
    public bool BlockSecrets { get => _secrets; set => Set(ref _secrets, value); }
    public bool PreventCloud { get => _cloud; set => Set(ref _cloud, value); }
    public bool ReducedMotion { get => _motion; set => Set(ref _motion, value); }
    public bool RecoverDrafts { get => _recoverDrafts; set => Set(ref _recoverDrafts, value); }
    public string[] Themes { get; } = ["Light", "Dark"];

    public void Load(AppSettings settings)
    {
        Capacity = settings.HistoryCapacity.ToString(CultureInfo.InvariantCulture);
        RetentionDays = settings.RetentionDays.ToString(CultureInfo.InvariantCulture);
        StorageMb = settings.StorageLimitMb.ToString(CultureInfo.InvariantCulture);
        MaxItemMb = settings.MaxItemMb.ToString(CultureInfo.InvariantCulture);
        ExcludedApps = string.Join(Environment.NewLine, settings.ExcludedApps);
        PlainTextPasteApps = string.Join(Environment.NewLine, settings.PlainTextPasteApps);
        Theme = settings.Theme;
        CaptureImages = settings.CaptureImages; CaptureFiles = settings.CaptureFiles;
        StartWithWindows = settings.StartWithWindows; KeepOnTop = settings.KeepWindowOnTop;
        BlockSecrets = settings.BlockRecognizableSecrets; PreventCloud = settings.PreventCloudAndSystemHistory;
        ReducedMotion = settings.ReducedMotion;
        RecoverDrafts = settings.RecoverEditorDrafts;
        SetHotkey(settings.HotkeyModifiers, settings.HotkeyKey, settings.HotkeyLabel);
        Error = "";
    }

    public void SetHotkey(uint modifiers, uint key, string label) { _modifiers = modifiers; _key = key; HotkeyLabel = label; }

    public AppSettings Build(AppSettings current)
    {
        static int Number(string text, int min, int max, string label)
        {
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < min || number > max)
                throw new ArgumentException($"{label} must be between {min:N0} and {max:N0}.");
            return number;
        }
        return current with
        {
            HistoryCapacity = Number(Capacity, 100, Limits.MaxItems, "History capacity"),
            RetentionDays = Number(RetentionDays, 0, 3650, "Retention days"),
            StorageLimitMb = Number(StorageMb, 64, 4096, "Storage limit"),
            MaxItemMb = Number(MaxItemMb, 1, 64, "Item limit"),
            ExcludedApps = ExcludedApps.Split(['\n', '\r', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            PlainTextPasteApps = PlainTextPasteApps.Split(['\n', '\r', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            Theme = Theme, CaptureImages = CaptureImages, CaptureFiles = CaptureFiles,
            StartWithWindows = StartWithWindows, KeepWindowOnTop = KeepOnTop,
            BlockRecognizableSecrets = BlockSecrets, PreventCloudAndSystemHistory = PreventCloud,
            RecoverEditorDrafts = RecoverDrafts,
            ReducedMotion = ReducedMotion, HotkeyModifiers = _modifiers, HotkeyKey = _key, HotkeyLabel = HotkeyLabel
        };
    }
}
