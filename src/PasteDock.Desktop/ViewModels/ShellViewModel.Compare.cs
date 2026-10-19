using Microsoft.Win32;

namespace PasteDock.Desktop.ViewModels;

internal sealed record DiffLineItem(string Prefix, string Text, bool IsAdded, bool IsRemoved);

internal sealed partial class ShellViewModel
{
    private bool _compareOpen, _compareIgnoreWhitespace;
    private string _compareText = "", _compareSummary = "", _compareLeftTitle = "", _compareRightTitle = "";
    private DiffLineItem[] _compareLines = [];
    private string? _compareLeftSource, _compareRightSource;

    public AsyncRelayCommand CompareCommand { get; }
    public AsyncRelayCommand RecompareCommand { get; }
    public AsyncRelayCommand CopyDiffCommand { get; }
    public AsyncRelayCommand SaveDiffCommand { get; }

    public bool CompareOpen { get => _compareOpen; set => Set(ref _compareOpen, value); }
    public bool CompareIgnoreWhitespace { get => _compareIgnoreWhitespace; set => Set(ref _compareIgnoreWhitespace, value); }
    public string CompareText { get => _compareText; private set => Set(ref _compareText, value); }
    public string CompareSummary { get => _compareSummary; private set => Set(ref _compareSummary, value); }
    public string CompareLeftTitle { get => _compareLeftTitle; private set => Set(ref _compareLeftTitle, value); }
    public string CompareRightTitle { get => _compareRightTitle; private set => Set(ref _compareRightTitle, value); }
    public IReadOnlyList<DiffLineItem> CompareLines => _compareLines;
    public bool CanCompareSelection => CanCompare();

    private bool CanCompare()
    {
        if (IsWorking || IsEditing || _workspace.Clipboard.IsLocked) return false;
        var ids = SelectedIds();
        if (ids.Length != 2) return false;
        var selected = ids.ToHashSet();
        var entries = _workspace.History.Snapshot.Where(entry => selected.Contains(entry.Id)).ToArray();
        return entries.Length == 2 && entries.All(entry => entry.Kind != ClipKind.Files && (entry.Kind != ClipKind.Image || entry.HasOcr));
    }

    private async Task OpenCompareAsync()
    {
        var ids = SelectedIds();
        if (ids.Length != 2) throw new InvalidOperationException("Select exactly two text-capable clips to compare.");
        var selected = ids.ToHashSet();
        var entries = _workspace.History.Snapshot.Where(entry => selected.Contains(entry.Id)).ToDictionary(entry => entry.Id);
        if (entries.Count != 2) throw new KeyNotFoundException();

        ClosePopovers();
        IsWorking = true;
        try
        {
            var leftPayload = await Task.Run(() => _workspace.History.LoadAsync(ids[0]));
            var rightPayload = await Task.Run(() => _workspace.History.LoadAsync(ids[1]));
            _compareLeftSource = leftPayload.GetPlainText();
            _compareRightSource = rightPayload.GetPlainText();
            CompareLeftTitle = entries[ids[0]].Title;
            CompareRightTitle = entries[ids[1]].Title;
            await ComputeDiffAsync();
            CompareOpen = true;
        }
        finally { IsWorking = false; }
    }

    private async Task RecompareAsync()
    {
        if (_compareLeftSource is null || _compareRightSource is null) return;
        IsWorking = true;
        try { await ComputeDiffAsync(); }
        finally { IsWorking = false; }
    }

    private async Task ComputeDiffAsync()
    {
        var left = _compareLeftSource ?? "";
        var right = _compareRightSource ?? "";
        var ignore = CompareIgnoreWhitespace;
        var result = await Task.Run(() => TextDiff.Compare(left, right, ignore));
        CompareText = result.ToUnifiedText(CompareLeftTitle, CompareRightTitle);
        _compareLines = result.Lines.Select(line => new DiffLineItem(
            line.Kind == DiffLineKind.Added ? "+" : line.Kind == DiffLineKind.Removed ? "−" : "",
            line.Text, line.Kind == DiffLineKind.Added, line.Kind == DiffLineKind.Removed)).ToArray();
        Raise(nameof(CompareLines));
        CompareSummary = $"{result.Added:N0} added · {result.Removed:N0} removed · {result.Unchanged:N0} unchanged"
            + (result.UsedFallback ? " · simplified large-change diff" : "");
    }

    private async Task CopyDiffAsync()
    {
        if (string.IsNullOrEmpty(CompareText)) return;
        await _workspace.Clipboard.WriteAsync(new ClipPayload { Text = CompareText }, _workspace.Settings.PreventCloudAndSystemHistory);
        Notify("Diff copied as plain text.");
    }

    private async Task SaveDiffAsync()
    {
        if (string.IsNullOrEmpty(CompareText)) return;
        var dialog = new SaveFileDialog
        {
            Title = "Save text comparison",
            FileName = $"PasteDock-diff-{DateTime.Now:yyyy-MM-dd-HHmmss}.diff",
            Filter = "Unified diff|*.diff|Text file|*.txt",
            AddExtension = true
        };
        if (dialog.ShowDialog() != true) return;
        await ExportService.WriteTextAsync(dialog.FileName, CompareText);
        Notify("Diff exported.");
    }

    private void ClearCompareState()
    {
        CompareOpen = false;
        _compareLeftSource = _compareRightSource = null;
        _compareLines = []; Raise(nameof(CompareLines));
        CompareText = CompareSummary = CompareLeftTitle = CompareRightTitle = "";
    }
}
