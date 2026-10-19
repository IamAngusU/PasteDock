using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using PasteDock.Core.Search;
using PasteDock.Desktop.Interop;
using PasteDock.Desktop.Services;

namespace PasteDock.Desktop.ViewModels;

internal sealed partial class ShellViewModel : ObservableObject, IDisposable
{
    private readonly WorkspaceService _workspace;
    private readonly Dispatcher _dispatcher;
    private readonly List<AsyncRelayCommand> _asyncCommands = [];
    private readonly PasteQueue _queue = new();
    private readonly DispatcherTimer _clock;
    private CancellationTokenSource? _searchCancellation, _previewCancellation;
    private ClipPayload? _loaded;
    private ClipItemViewModel? _selectedItem;
    private Guid[] _selectedIds = [], _pendingDeleteIds = [];
    private string _deleteDescription = "Delete keeps an encrypted undo copy for ten minutes, up to 128 MiB. A new deletion replaces the previous undo copy. The system clipboard is unchanged.";
    private PasteTarget? _target;
    private Guid? _editingId;
    private int _previewLength = 65_536;
    private bool _disposed;
    private string _transformResult = "";
    public bool HasRunningActions => IsWorking || _asyncCommands.Any(c => c.IsRunning);
    private string _searchText = "", _filterKey = "all", _status = "Ready. Copy something to begin.", _previewText = "", _editText = "", _tagInput = "", _transformPreview = "", _commandSearch = "";
    private bool _deepSearch, _searching, _previewBusy, _isEditing, _showImage, _settingsOpen, _tagsOpen, _deleteOpen, _toolsOpen, _helpOpen, _paletteOpen, _transformReady, _working;
    private BitmapSource? _previewImage;
    private double _previewFontSize = 13;
    private TransformOption? _selectedTransform;

    public ObservableCollection<ClipItemViewModel> Items { get; } = [];
    public ObservableCollection<FilterOption> Filters { get; } =
    [
        new("all", "All history", "\uE81C"), new("pinned", "Pinned", "\uE718"), new("snippets", "My snippets", "\uE70F"), new("favorites", "Favorites", "\uE734"),
        new("text", "Text & code", "\uE8A5"), new("image", "Images", "\uEB9F"), new("link", "Links", "\uE71B"), new("files", "Files", "\uE8B7")
    ];
    public ObservableCollection<string> AvailableTags { get; } = [];
    public ObservableCollection<PaletteAction> PaletteItems { get; } = [];
    private PaletteAction[] _allPaletteItems = [];
    public SettingsViewModel Settings { get; } = new();
    public TransformOption[] Transforms { get; } =
    [
        new("Format JSON", TextTransform.JsonPretty), new("Compact JSON", TextTransform.JsonCompact),
        new("Trim lines", TextTransform.TrimLines), new("Remove duplicate lines", TextTransform.UniqueLines),
        new("Sort lines", TextTransform.SortLines), new("Normalize to LF", TextTransform.NormalizeLineEndings),
        new("UPPERCASE", TextTransform.Uppercase), new("lowercase", TextTransform.Lowercase),
        new("URL encode", TextTransform.UrlEncode), new("URL decode", TextTransform.UrlDecode),
        new("Base64 encode", TextTransform.Base64Encode), new("Base64 decode", TextTransform.Base64Decode)
    ];
    public ShortcutInfo[] Shortcuts { get; } =
    [
        new("Ctrl Alt V", "Open or hide PasteDock (configurable)"), new("Ctrl Alt N", "Paste the next queued item globally"),
        new("Enter", "Paste selection"), new("Shift Enter", "Paste as plain text"), new("Ctrl C", "Copy selected clips"),
        new("Ctrl P / Ctrl B", "Pin / favorite"), new("Ctrl E / Ctrl N", "Edit / new snippet"),
        new("F2", "Rename the current clip"),
        new("Alt 1...9", "Paste one of the first nine visible results immediately"), new("Ctrl D", "Compare exactly two selected clips"),
        new("Ctrl Z", "Undo the last deletion (outside text inputs)"), new("Ctrl T", "Add tags"), new("Ctrl Q", "Queue selected clips, top to bottom"), new("Delete", "Review deletion"),
        new("Ctrl K", "Command menu"), new("Ctrl F", "Focus search"), new("Ctrl ,", "Settings"),
        new("Ctrl Enter", "Save inline edit"), new("Escape", "Cancel edit, close popover or hide window")
    ];

    public event Action? HideRequested;
    public event Action? ShowRequested;
    public event Action? SearchFocusRequested;
    public event Action? EditorFocusRequested;
    public event Action? QuitRequested;
    public Func<AppSettings, Task>? ApplySettings { get; set; }

    public RelayCommand FilterCommand { get; }
    public RelayCommand PauseCommand { get; }
    public RelayCommand NewSnippetCommand { get; }
    public RelayCommand EditCommand { get; }
    public RelayCommand CancelEditCommand { get; }
    public RelayCommand OpenSettingsCommand { get; }
    public RelayCommand CloseSettingsCommand { get; }
    public RelayCommand OpenTagsCommand { get; }
    public RelayCommand RequestDeleteCommand { get; }
    public RelayCommand CancelDeleteCommand { get; }
    public RelayCommand QueueCommand { get; }
    public RelayCommand ClearQueueCommand { get; }
    public RelayCommand ShowMoreCommand { get; }
    public RelayCommand OpenToolsCommand { get; }
    public RelayCommand OpenHelpCommand { get; }
    public RelayCommand OpenPaletteCommand { get; }
    public RelayCommand ClearSearchCommand { get; }
    public RelayCommand ResetViewCommand { get; }
    public RelayCommand UseTagCommand { get; }
    public RelayCommand ZoomTextInCommand { get; }
    public RelayCommand ZoomTextOutCommand { get; }
    public RelayCommand QuitCommand { get; }
    public RelayCommand RemoveTagCommand { get; }
    public string[] SelectedTags => SelectedItem?.Entry.Tags ?? [];
    public AsyncRelayCommand CopyCommand { get; }
    public AsyncRelayCommand PasteCommand { get; }
    public AsyncRelayCommand PlainPasteCommand { get; }
    public AsyncRelayCommand PinCommand { get; }
    public AsyncRelayCommand FavoriteCommand { get; }
    public AsyncRelayCommand SaveEditCommand { get; }
    public AsyncRelayCommand AddTagsCommand { get; }
    public AsyncRelayCommand ConfirmDeleteCommand { get; }
    public AsyncRelayCommand QueueNextCommand { get; }
    public AsyncRelayCommand ExtractTextCommand { get; }
    public AsyncRelayCommand SaveFileCommand { get; }
    public AsyncRelayCommand TransformCommand { get; }
    public AsyncRelayCommand SaveTransformCommand { get; }
    public AsyncRelayCommand CopyTransformCommand { get; }
    public AsyncRelayCommand SaveSettingsCommand { get; }

    public string SearchText { get => _searchText; set { if (Set(ref _searchText, value)) { Raise(nameof(HasSearch)); Search(); } } }
    public bool HasSearch => !string.IsNullOrWhiteSpace(SearchText);
    public bool DeepSearch { get => _deepSearch; set { if (Set(ref _deepSearch, value)) Search(); } }
    public bool IsSearching { get => _searching; private set { if (Set(ref _searching, value)) Raise(nameof(IsEmpty)); } }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public ClipItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set { if (Set(ref _selectedItem, value)) { RaiseSelection(); LoadPreview(); } }
    }
    public string PreviewText { get => _previewText; private set => Set(ref _previewText, value); }
    public BitmapSource? PreviewImage { get => _previewImage; private set => Set(ref _previewImage, value); }
    public bool PreviewBusy { get => _previewBusy; private set => Set(ref _previewBusy, value); }
    public bool IsEditing { get => _isEditing; private set { if (Set(ref _isEditing, value)) { Raise(nameof(IsBrowsing)); Raise(nameof(HasPreview)); RefreshCommands(); } } }
    public bool IsBrowsing => !IsEditing;
    public string EditText { get => _editText; set { if (Set(ref _editText, value)) Raise(nameof(EditDirty)); } }
    public bool ShowImage { get => _showImage; set { if (Set(ref _showImage, value)) Raise(nameof(ShowText)); } }
    public bool ShowText => !ShowImage && !ShowColor;
    public bool SettingsOpen { get => _settingsOpen; set => Set(ref _settingsOpen, value); }
    public bool TagsOpen { get => _tagsOpen; set => Set(ref _tagsOpen, value); }
    public bool DeleteOpen { get => _deleteOpen; set => Set(ref _deleteOpen, value); }
    public bool ToolsOpen { get => _toolsOpen; set => Set(ref _toolsOpen, value); }
    public bool HelpOpen { get => _helpOpen; set => Set(ref _helpOpen, value); }
    public bool PaletteOpen { get => _paletteOpen; set => Set(ref _paletteOpen, value); }
    public string TagInput { get => _tagInput; set => Set(ref _tagInput, value); }
    public string TransformPreview { get => _transformPreview; private set => Set(ref _transformPreview, value); }
    public bool TransformReady { get => _transformReady; private set { if (Set(ref _transformReady, value)) RefreshCommands(); } }
    public TransformOption? SelectedTransform { get => _selectedTransform; set { if (Set(ref _selectedTransform, value)) TransformReady = false; } }
    public string CommandSearch { get => _commandSearch; set { if (Set(ref _commandSearch, value)) RefreshPalette(); } }
    public double PreviewFontSize { get => _previewFontSize; set => Set(ref _previewFontSize, Math.Clamp(value, 10, 24)); }
    public bool IsWorking { get => _working; private set { if (Set(ref _working, value)) RefreshCommands(); } }
    public bool HasSelection => _selectedIds.Length > 0 || SelectedItem is not null;
    public bool HasPreview => SelectedItem is not null && !IsEditing;
    public bool IsEmpty => !IsSearching && Items.Count == 0;
    public bool IsViewFiltered => HasSearch || _filterKey != "all";
    public string EmptyTitle => IsViewFiltered ? "No matching clips" : IsPaused ? "Capture is paused" : "Your next copy starts here";
    public string EmptyHint => IsViewFiltered ? "Try fewer words, switch off a filter, or search the full content with Deep."
        : IsPaused ? "Resume capture to collect text, images and files. Your saved clips stay available."
        : "Copy text, an image or a file in any app. PasteDock keeps it here, on this device.";
    public bool HasError { get; private set; }
    public bool HasImage => _loaded?.Png is not null;
    public bool HasExtractedText => !string.IsNullOrEmpty(_loaded?.OcrText);
    public bool CanEdit => _loaded?.Text is not null && !IsEditing && !IsWorking;
    public bool CanTransform => _loaded is not null && (_loaded.Text is not null || _loaded.OcrText is not null || _loaded.Files.Length > 0) && !IsEditing;
    public bool IsPaused => _workspace.Clipboard.IsPaused;
    public string CaptureLabel => _workspace.Clipboard.IsLocked ? "Locked" : IsPaused ? "Paused" : "Capturing";
    public string CaptureHint => IsPaused ? "Resume clipboard capture" : "Pause clipboard capture";
    public string HotkeyLabel => _workspace.Settings.HotkeyLabel;
    public bool ReducedMotion => _workspace.Settings.ReducedMotion;
    public string SelectionLabel => SelectedIds().Length > 1 ? $"{SelectedIds().Length} selected" : "Preview";
    public string DeleteLabel => $"Delete {_pendingDeleteIds.Length} item{(_pendingDeleteIds.Length == 1 ? "" : "s")}?";
    public string DeleteDescription => _deleteDescription;
    public string SelectedTitle => SelectedItem?.Title ?? "Nothing selected";
    public string SelectedDetails => SelectedItem is null ? "" : $"{SelectedItem.KindLabel}   ·   {SelectedItem.Source}   ·   {SelectedItem.Size}";
    public string TotalLabel => $"{_workspace.History.Snapshot.Count:N0} items";
    public string StorageLabel => $"{ClipItemViewModel.FormatBytes(_workspace.History.Snapshot.Sum(e => e.ByteCount))} / {_workspace.Settings.StorageLimitMb:N0} MB";
    public string ResultsLabel => $"{Items.Count:N0} {(Items.Count == 1 ? "item" : "items")}";
    public string FilterLabel => Filters.FirstOrDefault(f => f.Key == _filterKey)?.Label ?? "All history";
    public string TargetLabel => _target is null ? "Copy to clipboard" : $"Paste to {_target.AppName}";
    public string QueueLabel => $"{_queue.Count} queued";
    public bool HasQueue => _queue.Count > 0;
    public string PreviewLengthLabel
    {
        get
        {
            var length = PreviewSource().Length;
            return length > _previewLength ? $"Showing {_previewLength:N0} of {length:N0} characters" : $"{length:N0} characters";
        }
    }
    public bool HasMorePreview => PreviewSource().Length > _previewLength;

    public ShellViewModel(WorkspaceService workspace, Dispatcher dispatcher)
    {
        _workspace = workspace; _dispatcher = dispatcher;
        AsyncRelayCommand Async(Func<Task> action, Func<bool>? can = null)
        {
            var command = new AsyncRelayCommand(action, ReportError, can);
            _asyncCommands.Add(command); return command;
        }
        FilterCommand = new RelayCommand(parameter => SetFilter(parameter is FilterOption filter ? filter.Key : parameter?.ToString() ?? "all"));
        PauseCommand = new RelayCommand(() => { workspace.TogglePause(); });
        NewSnippetCommand = new RelayCommand(BeginNew, () => !IsEditing && !IsWorking && !workspace.Clipboard.IsLocked);
        EditCommand = new RelayCommand(BeginEdit, () => CanEdit);
        CancelEditCommand = new RelayCommand(RequestCancelEdit, () => !IsWorking);
        OpenSettingsCommand = new RelayCommand(() => { Settings.Load(workspace.Settings); ClosePopovers(); SettingsOpen = true; });
        CloseSettingsCommand = new RelayCommand(() => SettingsOpen = false);
        OpenTagsCommand = new RelayCommand(() => { if (!HasSelection) return; ClosePopovers(); TagInput = ""; TagsOpen = true; });
        RequestDeleteCommand = new RelayCommand(() => RequestDeletion(SelectedIds(),
            "Includes selected pins and favorites. Delete keeps an encrypted undo copy for ten minutes, up to 128 MiB. A new deletion replaces the previous undo copy. The system clipboard is unchanged."));
        RequestSourceDeleteCommand = new RelayCommand(RequestDeleteSource, CanManageSource);
        CancelDeleteCommand = new RelayCommand(() => DeleteOpen = false);
        QueueCommand = new RelayCommand(Enqueue, CanAct);
        ClearQueueCommand = new RelayCommand(() => { _queue.Clear(); QueueChanged(); }, () => !IsWorking);
        ShowMoreCommand = new RelayCommand(() => { _previewLength = Math.Min(PreviewSource().Length, _previewLength + 65_536); RefreshTextPreview(); });
        OpenToolsCommand = new RelayCommand(() => { if (!CanTransform) return; ClosePopovers(); TransformReady = false; ToolsOpen = true; }, () => CanTransform);
        OpenHelpCommand = new RelayCommand(() => { ClosePopovers(); HelpOpen = true; });
        OpenPaletteCommand = new RelayCommand(() => { ClosePopovers(); CommandSearch = ""; RefreshPalette(); PaletteOpen = true; });
        ClearSearchCommand = new RelayCommand(() => SearchText = "");
        ResetViewCommand = new RelayCommand(() => { SearchText = ""; SetFilter("all"); });
        UseTagCommand = new RelayCommand(parameter => { if (parameter is string tag) { SearchText = "#" + tag; SetFilter("all"); } });
        ZoomTextInCommand = new RelayCommand(() => PreviewFontSize++);
        ZoomTextOutCommand = new RelayCommand(() => PreviewFontSize--);
        QuitCommand = new RelayCommand(() => QuitRequested?.Invoke());
        RemoveTagCommand = new RelayCommand(RemoveTag, CanAct);
        CopyCommand = Async(CopyAsync, CanAct);
        PasteCommand = Async(() => PasteAsync(false), CanAct);
        PlainPasteCommand = Async(() => PasteAsync(true), CanAct);
        PasteAsFileCommand = Async(PasteAsFileAsync, CanPasteAsFile);
        PinCommand = Async(() => ToggleFlagAsync(true), CanAct);
        FavoriteCommand = Async(() => ToggleFlagAsync(false), CanAct);
        ExcludeSourceCommand = Async(ExcludeSourceAsync, CanManageSource);
        TogglePlainTargetCommand = Async(TogglePlainTargetAsync, CanManageTarget);
        SaveEditCommand = Async(SaveEditAsync, () => IsEditing && !IsWorking && !workspace.Clipboard.IsLocked);
        AddTagsCommand = Async(AddTagsAsync, CanAct);
        ConfirmDeleteCommand = Async(DeleteAsync, () => _pendingDeleteIds.Length > 0 && !IsWorking && !IsEditing && !workspace.Clipboard.IsLocked);
        PermanentDeleteCommand = Async(PermanentDeleteAsync, () => _pendingDeleteIds.Length > 0 && !IsWorking && !IsEditing && !workspace.Clipboard.IsLocked);
        UndoDeleteCommand = Async(UndoDeleteAsync, CanUndoDelete);
        ForgetDeletionCommand = Async(ForgetDeletionAsync, CanUndoDelete);
        QueueNextCommand = Async(PasteNextAsync, CanUseQueue);
        ExtractTextCommand = Async(ExtractTextAsync, () => HasImage && !IsWorking && !IsEditing);
        SaveFileCommand = Async(SaveFileAsync, () => _loaded is not null && !IsWorking && !IsEditing);
        TransformCommand = Async(PreviewTransformAsync, () => CanTransform && SelectedTransform is not null);
        SaveTransformCommand = Async(SaveTransformAsync, () => TransformReady && !IsWorking);
        CopyTransformCommand = Async(CopyTransformAsync, () => TransformReady && !IsWorking);
        SaveSettingsCommand = Async(SaveSettingsAsync, () => !IsWorking);
        CompareCommand = Async(OpenCompareAsync, CanCompare);
        RecompareCommand = Async(RecompareAsync, () => CompareOpen && !IsWorking && _compareLeftSource is not null && _compareRightSource is not null);
        CopyDiffCommand = Async(CopyDiffAsync, () => CompareOpen && !IsWorking && !string.IsNullOrEmpty(CompareText));
        SaveDiffCommand = Async(SaveDiffAsync, () => CompareOpen && !IsWorking && !string.IsNullOrEmpty(CompareText));
        InitializeWorkspaceCommands();
        _allPaletteItems =
        [
            new("New snippet", "Ctrl N", NewSnippetCommand), new("Pause / resume capture", "", PauseCommand),
            new("Rename clip", "F2", RenameCommand), new("Review paste queue", "", ToggleQueueCommand), new("Search tips", "", OpenSearchHelpCommand), new("Copy selected clips", "Ctrl C", CopyCommand), new("Paste as plain text", "Shift Enter", PlainPasteCommand),
            new("Paste selection as file", "", PasteAsFileCommand), new("Compare two clips", "Ctrl D", CompareCommand),
            new("Toggle plain-text rule for paste target", "", TogglePlainTargetCommand),
            new("Undo last deletion", "Ctrl Z", UndoDeleteCommand), new("Queue selection", "Ctrl Q", QueueCommand), new("Transform text", "", OpenToolsCommand),
            new("Settings", "Ctrl ,", OpenSettingsCommand), new("Keyboard shortcuts", "", OpenHelpCommand)
        ];
        SelectedTransform = Transforms[0];
        Filters[0].IsActive = true;
        workspace.History.Changed += OnHistoryChanged;
        workspace.Notice += OnNotice;
        workspace.CaptureStateChanged += OnCaptureState;
        _clock = new DispatcherTimer(TimeSpan.FromMinutes(1), DispatcherPriority.Background, (_, _) =>
        { foreach (var item in Items) item.Update(item.Entry); RefreshDeletion(); }, dispatcher);
        Search();
    }


    private void RequestDeletion(Guid[] ids, string description)
    {
        _pendingDeleteIds = ids;
        if (_pendingDeleteIds.Length == 0) return;
        _deleteDescription = description;
        ClosePopovers();
        Raise(nameof(DeleteLabel)); Raise(nameof(DeleteDescription));
        DeleteOpen = true;
    }

    private bool CanAct() => HasSelection && !IsEditing && !IsWorking && !_workspace.Clipboard.IsLocked;
    public void SetTarget(PasteTarget? target)
    {
        _target = target;
        Raise(nameof(TargetLabel)); Raise(nameof(TargetPlainRuleLabel));
        RefreshCommands();
    }
    public void SetSelection(IEnumerable<ClipItemViewModel> selected)
    {
        _selectedIds = selected.Select(e => e.Id).ToArray();
        RaiseSelection();
    }
    private Guid[] SelectedIds()
    {
        if (_selectedIds.Length == 0) return SelectedItem is null ? [] : [SelectedItem.Id];
        var selected = _selectedIds.ToHashSet();
        return Items.Where(item => selected.Contains(item.Id)).Select(item => item.Id).ToArray();
    }
    private void RaiseSelection()
    {
        Raise(nameof(SelectedTags));
        Raise(nameof(HasSelection)); Raise(nameof(HasPreview)); Raise(nameof(SelectionLabel)); Raise(nameof(SelectedTitle)); Raise(nameof(SelectedDetails));
        Raise(nameof(SelectedSourceApp)); Raise(nameof(ExcludeSourceLabel)); Raise(nameof(DeleteSourceLabel)); Raise(nameof(CanCompareSelection));
        RefreshCommands();
    }
    private void RefreshCommands()
    {
        foreach (var command in _asyncCommands) command.Refresh();
        EditCommand?.Refresh(); NewSnippetCommand?.Refresh(); QueueCommand?.Refresh(); ClearQueueCommand?.Refresh(); RemoveTagCommand?.Refresh(); RequestSourceDeleteCommand?.Refresh(); OpenToolsCommand?.Refresh();
        RefreshWorkspaceCommands();
        Raise(nameof(CanEdit)); Raise(nameof(CanTransform));
    }
    private void SetFilter(string key)
    {
        _filterKey = Filters.Any(filter => filter.Key == key) ? key : "all";
        foreach (var filter in Filters) filter.IsActive = filter.Key == key;
        Raise(nameof(FilterLabel)); Raise(nameof(ActiveFilter)); RaiseEmptyState(); Search();
    }
    private void OnHistoryChanged() { if (!_disposed) _dispatcher.BeginInvoke(() => { RefreshDeletion(); RefreshQueueItems(); Search(); }); }
    private void OnNotice(string message) { if (!_disposed) _dispatcher.BeginInvoke(() => Notify(message)); }
    private void OnCaptureState()
    {
        if (_disposed) return;
        _dispatcher.BeginInvoke(() => { Raise(nameof(IsPaused)); Raise(nameof(CaptureLabel)); Raise(nameof(CaptureHint)); RaiseEmptyState(); });
    }
    private void RaiseEmptyState()
    { Raise(nameof(EmptyTitle)); Raise(nameof(EmptyHint)); Raise(nameof(IsViewFiltered)); }
    public void Notify(string message) { HasError = false; Raise(nameof(HasError)); Status = message; }

    public async void Search()
    {
        if (_disposed || _workspace.Clipboard.IsLocked) return;
        _searchCancellation?.Cancel(); _searchCancellation?.Dispose();
        var cancellation = _searchCancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        IsSearching = true;
        try
        {
            await Task.Delay(100, token);
            var query = SearchQuery.Parse(SearchText) with { DeepSearch = DeepSearch };
            query = _filterKey switch
            {
                "pinned" => query with { PinnedOnly = true }, "snippets" => query with { SnippetsOnly = true }, "favorites" => query with { FavoritesOnly = true },
                "image" => query with { Kind = ClipKind.Image }, "link" => query with { Kind = ClipKind.Link },
                "files" => query with { Kind = ClipKind.Files }, _ => query
            };
            var sort = SelectedSort.Sort;
            var found = await Task.Run(() => _workspace.History.SearchAsync(query, token, sort), token);
            token.ThrowIfCancellationRequested();
            if (_filterKey == "text") found = found.Where(e => e.Kind is ClipKind.Text or ClipKind.Code or ClipKind.Json or ClipKind.Color).ToArray();
            Reconcile(found);
            var snapshot = _workspace.History.Snapshot;
            foreach (var filter in Filters) filter.Count = filter.Key switch
            {
                "pinned" => snapshot.Count(e => e.IsPinned), "snippets" => snapshot.Count(SearchMatcher.IsSnippet), "favorites" => snapshot.Count(e => e.IsFavorite),
                "image" => snapshot.Count(e => e.Kind == ClipKind.Image), "link" => snapshot.Count(e => e.Kind == ClipKind.Link),
                "files" => snapshot.Count(e => e.Kind == ClipKind.Files), "text" => snapshot.Count(e => e.Kind is ClipKind.Text or ClipKind.Code or ClipKind.Json or ClipKind.Color), _ => snapshot.Count
            };
            var tags = snapshot.SelectMany(e => e.Tags).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t).ToArray();
            AvailableTags.Clear(); foreach (var tag in tags) AvailableTags.Add(tag);
            Raise(nameof(TotalLabel)); Raise(nameof(StorageLabel)); Raise(nameof(ResultsLabel)); Raise(nameof(IsEmpty)); RaiseEmptyState(); RaiseSelection();
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { ReportError(exception); }
        finally { if (ReferenceEquals(_searchCancellation, cancellation)) IsSearching = false; }
    }

    private void Reconcile(IReadOnlyList<ClipEntry> found)
    {
        var desired = found.Select(e => e.Id).ToHashSet();
        for (var i = Items.Count - 1; i >= 0; i--) if (!desired.Contains(Items[i].Id)) Items.RemoveAt(i);
        var existingItems = Items.ToDictionary(item => item.Id);
        for (var i = 0; i < found.Count; i++)
        {
            existingItems.TryGetValue(found[i].Id, out var existing);
            if (existing is null) Items.Insert(i, new ClipItemViewModel(found[i]));
            else
            {
                existing.Update(found[i]);
                if (!ReferenceEquals(Items[i], existing)) { var index = Items.IndexOf(existing); Items.Move(index, i); }
            }
        }
        for (var i = 0; i < Items.Count; i++) Items[i].SetQuickIndex(i);
        if (SelectedItem is null || !desired.Contains(SelectedItem.Id)) SelectedItem = Items.FirstOrDefault();
    }

    private async void LoadPreview()
    {
        if (_disposed || _workspace.Clipboard.IsLocked) return;
        _previewCancellation?.Cancel(); _previewCancellation?.Dispose();
        var cancellation = _previewCancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        var selected = SelectedItem;
        _loaded = null; PreviewImage = null; PreviewText = ""; PreviewBusy = selected is not null;
        _previewLength = 65_536; TransformReady = false;
        try
        {
            if (selected is null) return;
            var payload = await Task.Run(() => _workspace.History.LoadAsync(selected.Id, token), token);
            BitmapSource? image = null;
            if (payload.Png is not null)
            {
                image = await Task.Run(() =>
                {
                    var bitmap = new BitmapImage();
                    using var stream = new MemoryStream(payload.Png, writable: false);
                    bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                    var scale = Math.Min(1d, 2560d / Math.Max(payload.ImageWidth, payload.ImageHeight));
                    bitmap.DecodePixelWidth = Math.Max(1, (int)(payload.ImageWidth * scale));
                    bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
                    return (BitmapSource)bitmap;
                }, token);
            }
            token.ThrowIfCancellationRequested();
            if (_workspace.Clipboard.IsLocked || _disposed) return;
            _loaded = payload; PreviewImage = image; ShowImage = image is not null;
            RefreshTextPreview();
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { ReportError(exception); }
        finally
        {
            if (ReferenceEquals(_previewCancellation, cancellation))
            {
                PreviewBusy = false; Raise(nameof(HasImage)); Raise(nameof(HasExtractedText)); RefreshCommands();
            }
        }
    }

    private string PreviewSource() => _loaded?.Text ?? _loaded?.OcrText ?? (_loaded?.Files.Length > 0 ? string.Join(Environment.NewLine, _loaded.Files) : "");
    private void RefreshTextPreview()
    {
        PreviewText = ClipFactory.Truncate(PreviewSource(), _previewLength);
        Raise(nameof(PreviewLengthLabel)); Raise(nameof(HasMorePreview));
    }
    private Task<ClipPayload> ComposeAsync(bool plain)
    {
        var ids = SelectedIds();
        var separator = _workspace.Settings.JoinSeparator;
        return Task.Run(() => PayloadComposer.CombineAsync(ids, _workspace.History.LoadAsync, separator, plain));
    }
    private async Task CopyAsync()
    {
        IsWorking = true;
        try { await _workspace.Clipboard.WriteAsync(await ComposeAsync(false), _workspace.Settings.PreventCloudAndSystemHistory); Notify("Copied. The complete content is on your clipboard."); }
        finally { IsWorking = false; }
    }
    private async Task PasteAsync(bool plain)
    {
        IsWorking = true;
        try
        {
            var ids = SelectedIds();
            var target = _target;
            var forcePlain = !plain && ShouldForcePlain(target, ids);
            var payload = await ComposeAsync(plain || forcePlain);
            var result = await _workspace.Paste.PasteAsync(payload, target, _workspace.Settings.PreventCloudAndSystemHistory, () => HideRequested?.Invoke());
            Notify(forcePlain && result.InputSent ? result.Message + $" Plain-text rule applied for {target!.AppName}." : result.Message);
            if (result.InputSent)
                await Task.Run(() => _workspace.History.UpdateMetadataAsync(ids, e => e with { LastPastedAt = DateTimeOffset.UtcNow }));
            else ShowRequested?.Invoke();
        }
        finally { IsWorking = false; }
    }
    private async Task ToggleFlagAsync(bool pin)
    {
        var ids = SelectedIds();
        var selected = ids.ToHashSet();
        var entries = _workspace.History.Snapshot.Where(e => selected.Contains(e.Id)).ToArray();
        var value = !entries.All(e => pin ? e.IsPinned : e.IsFavorite);
        await Task.Run(() => _workspace.History.UpdateMetadataAsync(ids, e => pin ? e with { IsPinned = value } : e with { IsFavorite = value }));
    }
    private void BeginNew()
    {
        ClosePopovers(); _editingId = null; _editOriginal = ""; EditText = ""; IsEditing = true; EditorFocusRequested?.Invoke();
    }
    private void BeginEdit()
    {
        if (!CanEdit) return;
        ClosePopovers(); _editingId = SelectedItem!.Id; _editOriginal = _loaded!.Text!; EditText = _editOriginal; IsEditing = true; EditorFocusRequested?.Invoke();
    }
    private void CancelEdit() { DiscardEditOpen = false; IsEditing = false; EditText = ""; _editingId = null; _editOriginal = ""; }
    private async Task SaveEditAsync()
    {
        IsWorking = true;
        try
        {
            if (string.IsNullOrWhiteSpace(EditText))
                throw new InvalidOperationException("Write something before saving this snippet. Your draft stays open.");
            var payload = new ClipPayload { Text = EditText };
            var decision = PrivacyPolicy.Evaluate(payload, "PasteDock", _workspace.Settings);
            if (!decision.Allowed) throw new InvalidOperationException(decision.Reason);
            if (_editingId is Guid id) await Task.Run(() => _workspace.History.ReplacePayloadAsync(id, payload, _workspace.Settings));
            else await Task.Run(() => _workspace.History.CaptureAsync(payload, "PasteDock", _workspace.Settings));
            CancelEdit(); LoadPreview(); Notify("Snippet saved. The system clipboard was not changed.");
        }
        finally { IsWorking = false; }
    }
    private async Task AddTagsAsync()
    {
        var tags = ClipFactory.NormalizeTags(TagInput.Split([',', ' ', '\n'], StringSplitOptions.RemoveEmptyEntries));
        if (tags.Length == 0) throw new InvalidOperationException("Use tags with letters, digits, underscores or hyphens, up to 32 characters each.");
        var ids = SelectedIds();
        await Task.Run(() => _workspace.History.UpdateMetadataAsync(ids, e => e with { Tags = e.Tags.Concat(tags).ToArray() }));
        TagsOpen = false; Notify("Tags added.");
    }
    private async void RemoveTag(object? parameter)
    {
        if (parameter is not string tag) return;
        IsWorking = true;
        try { await RemoveTagAsync(tag); }
        catch (Exception exception) { ReportError(exception); }
        finally { IsWorking = false; }
    }
    public async Task RemoveTagAsync(string tag)
    {
        var ids = SelectedIds();
        await Task.Run(() => _workspace.History.UpdateMetadataAsync(ids, e => e with { Tags = e.Tags.Where(t => t != tag).ToArray() }));
    }
    private async Task DeleteAsync()
    {
        var ids = _pendingDeleteIds;
        IsWorking = true;
        try
        {
            var count = await Task.Run(() => _workspace.History.DeleteWithUndoAsync(ids));
            DeleteOpen = false; _pendingDeleteIds = [];
            Notify($"Deleted {count} clips. Undo is available for ten minutes. The system clipboard is unchanged.");
        }
        finally { IsWorking = false; RefreshDeletion(); }
    }
    private async Task ExtractTextAsync()
    {
        if (_loaded?.Png is null || SelectedItem is null) return;
        var payload = _loaded; var id = SelectedItem.Id;
        IsWorking = true;
        try
        {
            Notify("Reading image text on this device...");
            var text = await Task.Run(() => _workspace.Ocr.RecognizeAsync(payload.Png));
            if (_workspace.Clipboard.IsLocked || _disposed) return;
            var updated = payload with { OcrText = text };
            var decision = PrivacyPolicy.Evaluate(updated, "PasteDock", _workspace.Settings);
            if (!decision.Allowed) throw new InvalidOperationException("The extracted text contains a recognizable secret and was not saved.");
            await Task.Run(() => _workspace.History.ReplacePayloadAsync(id, updated, _workspace.Settings));
            if (SelectedItem?.Id == id && !_workspace.Clipboard.IsLocked && !_disposed) { _loaded = updated; RefreshTextPreview(); ShowImage = false; Raise(nameof(HasExtractedText)); }
            Notify(text.Length == 0 ? "No readable text was found." : "Text extracted locally and added to search.");
        }
        finally { IsWorking = false; }
    }
    private async Task SaveFileAsync()
    {
        if (_loaded is null) return;
        var payload = _loaded;
        var isImage = payload.Png is not null;
        var dialog = new SaveFileDialog
        {
            Title = "Export original content", FileName = isImage ? "clipboard-image.png" : "clipboard-text.txt",
            Filter = isImage ? "PNG image|*.png" : "UTF-8 text|*.txt", AddExtension = true
        };
        if (dialog.ShowDialog() != true) return;
        if (isImage) await ExportService.WriteBytesAsync(dialog.FileName, payload.Png!);
        else await ExportService.WriteTextAsync(dialog.FileName, payload.GetPlainText());
        Notify("Exported the complete content. Exported files are not encrypted.");
    }
    private async Task PreviewTransformAsync()
    {
        if (_loaded is null || SelectedTransform is null) return;
        var source = _loaded; var text = source.GetPlainText(); var transform = SelectedTransform.Transform;
        var transformed = await Task.Run(() => TextTransforms.Apply(text, transform));
        if (!ReferenceEquals(source, _loaded) || SelectedTransform?.Transform != transform || _workspace.Clipboard.IsLocked || _disposed) return;
        _transformResult = transformed;
        TransformPreview = ClipFactory.Truncate(transformed, 65_536);
        TransformReady = true;
    }
    private async Task SaveTransformAsync()
    {
        var payload = new ClipPayload { Text = _transformResult };
        var decision = PrivacyPolicy.Evaluate(payload, "PasteDock", _workspace.Settings);
        if (!decision.Allowed) throw new InvalidOperationException(decision.Reason);
        await Task.Run(() => _workspace.History.CaptureAsync(payload, "PasteDock", _workspace.Settings));
        ToolsOpen = false; Notify("Saved as a new clip. The original is unchanged.");
    }
    private async Task CopyTransformAsync()
    {
        await _workspace.Clipboard.WriteAsync(new ClipPayload { Text = _transformResult }, _workspace.Settings.PreventCloudAndSystemHistory);
        ToolsOpen = false; Notify("Transformed text copied. The original is unchanged.");
    }
    private async Task SaveSettingsAsync()
    {
        Settings.Error = "";
        try
        {
            var next = Settings.Build(_workspace.Settings);
            if (ApplySettings is not null) await ApplySettings(next);
            else await _workspace.SaveSettingsAsync(next);
            SettingsOpen = false;
            Raise(nameof(HotkeyLabel)); Raise(nameof(ReducedMotion)); Raise(nameof(StorageLabel)); Raise(nameof(TargetPlainRuleLabel));
            Notify("Settings saved.");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        { Settings.Error = exception.Message; }
    }

    public async Task BackupAsync(bool import, string passphrase)
    {
        if (IsWorking) return;
        IsWorking = true;
        try
        {
            if (import)
            {
                var dialog = new OpenFileDialog { Title = "Import encrypted backup", Filter = "PasteDock encrypted backup|*.pdbackup", CheckFileExists = true };
                if (dialog.ShowDialog() != true) return;
                var archive = await BackupService.ReadFileAsync(dialog.FileName);
                var items = await Task.Run(() => BackupService.Decrypt(archive, passphrase));
                var count = await Task.Run(() => _workspace.History.ImportAsync(items, _workspace.Settings));
                Notify($"Imported {count} new clips. Existing duplicates were skipped.");
            }
            else
            {
                var dialog = new SaveFileDialog { Title = "Save encrypted backup", FileName = $"PasteDock-{DateTime.Now:yyyy-MM-dd}.pdbackup", Filter = "PasteDock encrypted backup|*.pdbackup", AddExtension = true };
                if (dialog.ShowDialog() != true) return;
                var items = await Task.Run(() => _workspace.History.ExportAsync());
                var archive = await Task.Run(() => BackupService.Encrypt(items, passphrase));
                await ExportService.WriteBytesAsync(dialog.FileName, archive);
                Notify("Encrypted backup saved. Keep the passphrase separately; it cannot be recovered.");
            }
        }
        catch (CryptographicException) { Settings.Error = "Incorrect passphrase or damaged backup. Nothing was imported."; }
        catch (Exception exception) { ReportError(exception); Settings.Error = Status; }
        finally { IsWorking = false; }
    }

    private void RefreshPalette()
    {
        PaletteItems.Clear();
        foreach (var item in _allPaletteItems.Where(p => p.Label.Contains(CommandSearch, StringComparison.OrdinalIgnoreCase))) PaletteItems.Add(item);
    }
    public void ExecutePalette(PaletteAction action)
    {
        if (!action.Command.CanExecute(null)) { Notify("This action is unavailable for the current selection."); return; }
        PaletteOpen = false; action.Command.Execute(null);
    }
    public void FocusSearch() => SearchFocusRequested?.Invoke();
    public bool ClosePopovers()
    {
        var wasOpen = TagsOpen || DeleteOpen || ToolsOpen || HelpOpen || PaletteOpen || CompareOpen || RenameOpen || SearchHelpOpen || DiscardEditOpen;
        TagsOpen = DeleteOpen = ToolsOpen = HelpOpen = PaletteOpen = CompareOpen = RenameOpen = SearchHelpOpen = DiscardEditOpen = false;
        return wasOpen;
    }
    public void OnSessionLock()
    {
        _previewCancellation?.Cancel(); _searchCancellation?.Cancel();
        _loaded = null; PreviewText = ""; PreviewImage = null;
        // Keep an unsaved editor draft in RAM; session lock must not silently discard work.
        TransformPreview = ""; _transformResult = ""; TransformReady = false;
        _queue.Clear(); QueueChanged(); ClosePopovers(); ClearCompareState(); SettingsOpen = false;
        HideRequested?.Invoke();
    }
    public void OnShown() { Search(); if (_loaded is null && SelectedItem is not null) LoadPreview(); RefreshCommands(); }
    public void ReportError(Exception exception)
    {
        _workspace.Log.Write("UI_ACTION_FAILED", exception);
        Notify(exception switch
        {
            OperationCanceledException => "The clipboard request expired or was cancelled. Check the clipboard before pasting manually.",
            JsonException => "This is not valid JSON. The original was not changed.",
            FormatException or DecoderFallbackException => "The input is not valid for this transformation.",
            CryptographicException => "Windows could not decrypt this item. Check that this is the original Windows user profile.",
            InvalidDataException or InvalidOperationException or ArgumentException => exception.Message,
            KeyNotFoundException => "This item no longer exists.",
            IOException => "The file operation failed. Check free disk space and whether another app is using the file.",
            UnauthorizedAccessException => "Access was denied. Choose a writable location.",
            _ => "The operation could not be completed. No clipboard content was written to the diagnostic log."
        });
        HasError = true; Raise(nameof(HasError));
    }
    public void Dispose()
    {
        _disposed = true; _clock.Stop();
        _searchCancellation?.Cancel(); _previewCancellation?.Cancel();
        _searchCancellation?.Dispose(); _previewCancellation?.Dispose();
        _workspace.History.Changed -= OnHistoryChanged; _workspace.Notice -= OnNotice; _workspace.CaptureStateChanged -= OnCaptureState;
    }
}
