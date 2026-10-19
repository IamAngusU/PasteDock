namespace PasteDock.Desktop.ViewModels;

public sealed class ClipItemViewModel : ObservableObject
{
    private string _quickKey = "";
    public ClipEntry Entry { get; private set; }
    public Guid Id => Entry.Id;
    public string Title => Entry.Title;
    public string Preview => Entry.Kind == ClipKind.Image && !Entry.HasOcr ? "Image preserved as PNG" : ClipFactory.SingleLine(Entry.Preview, 180);
    public string Source => Entry.SourceApp;
    public bool IsPinned => Entry.IsPinned;
    public bool IsFavorite => Entry.IsFavorite;
    public string TagsLabel => string.Join("  ", Entry.Tags.Select(t => "#" + t));
    public string KindLabel => Entry.Kind switch { ClipKind.Json => "JSON", ClipKind.Link => "Link", ClipKind.Files => "Files", _ => Entry.Kind.ToString() };
    public string Glyph => Entry.Kind switch
    {
        ClipKind.Image => "\uEB9F", ClipKind.Files => "\uE8B7", ClipKind.Link => "\uE71B",
        ClipKind.Code or ClipKind.Json => "\uE943", ClipKind.Color => "\uE790", _ => "\uE8A5"
    };
    public string Age
    {
        get
        {
            var age = DateTimeOffset.UtcNow - Entry.LastCopiedAt;
            return age.TotalMinutes < 1 ? "now" : age.TotalHours < 1 ? $"{(int)age.TotalMinutes}m" :
                age.TotalDays < 1 ? $"{(int)age.TotalHours}h" : age.TotalDays < 7 ? $"{(int)age.TotalDays}d" : Entry.LastCopiedAt.ToLocalTime().ToString("dd MMM");
        }
    }
    public string Size => FormatBytes(Entry.ByteCount);
    public string QuickKey { get => _quickKey; private set => Set(ref _quickKey, value); }
    public ClipItemViewModel(ClipEntry entry) => Entry = entry;
    public void Update(ClipEntry entry) { Entry = entry; Raise(null); }
    public void SetQuickIndex(int index) => QuickKey = index is >= 0 and < 9 ? (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
    public static string FormatBytes(long value) => value < 1024 ? $"{value} B" : value < 1024 * 1024 ? $"{value / 1024d:0.#} KB" : $"{value / (1024d * 1024):0.#} MB";
}

public sealed class FilterOption(string key, string label, string glyph) : ObservableObject
{
    public string Key { get; } = key;
    public string Label { get; } = label;
    public string Glyph { get; } = glyph;
    private bool _isActive;
    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }
    private int _count;
    public int Count { get => _count; set => Set(ref _count, value); }
}

public sealed record PaletteAction(string Label, string Shortcut, System.Windows.Input.ICommand Command);
public sealed record ShortcutInfo(string Keys, string Description);
public sealed record TransformOption(string Label, TextTransform Transform);
