using System.Windows;
using System.Windows.Media;
using PasteDock.Core.Search;

namespace PasteDock.Desktop.ViewModels;

internal sealed record SortOption(SearchSort Sort, string Label);
internal sealed record QueueItem(Guid Ticket, Guid ClipId, int Position, string Title, string Details, bool Missing);

internal sealed partial class ShellViewModel
{
    private bool _queueOpen, _renameOpen, _searchHelpOpen, _wrapText = true, _discardEditOpen;
    private string _renameText = "", _editOriginal = "";
    private Guid? _renameId;
    private QueueItem? _selectedQueueItem;
    private SortOption _selectedSort = new(SearchSort.BestMatch, "Best match");
    public SortOption[] SortOptions { get; } =
    [ new(SearchSort.BestMatch, "Best match"), new(SearchSort.RecentlyCopied, "Recently copied"),
      new(SearchSort.MostCopied, "Most copied"), new(SearchSort.Newest, "Newest saved"), new(SearchSort.Largest, "Largest first") ];
    public SortOption SelectedSort { get => _selectedSort; set { if (value is not null && Set(ref _selectedSort, value)) Search(); } }
    public FilterOption ActiveFilter
    {
        get => Filters.FirstOrDefault(filter => filter.Key == _filterKey) ?? Filters[0];
        set { if (value is not null && value.Key != _filterKey) SetFilter(value.Key); }
    }
    public ObservableCollection<QueueItem> QueueItems { get; } = [];
    public QueueItem? SelectedQueueItem
    {
        get => _selectedQueueItem;
        set { if (Set(ref _selectedQueueItem, value)) RefreshQueueCommands(); }
    }
    public bool QueueOpen { get => _queueOpen; set { if (Set(ref _queueOpen, value)) Raise(nameof(QueueToggleLabel)); } }
    public string QueueToggleLabel => QueueOpen ? "Hide" : "Review";
    public bool RenameOpen { get => _renameOpen; set => Set(ref _renameOpen, value); }
    public string RenameText { get => _renameText; set => Set(ref _renameText, value); }
    public bool SearchHelpOpen { get => _searchHelpOpen; set => Set(ref _searchHelpOpen, value); }
    public bool WrapText { get => _wrapText; set { if (Set(ref _wrapText, value)) Raise(nameof(PreviewWrapping)); } }
    public TextWrapping PreviewWrapping => WrapText ? TextWrapping.Wrap : TextWrapping.NoWrap;
    public bool DiscardEditOpen { get => _discardEditOpen; set => Set(ref _discardEditOpen, value); }
    public bool EditDirty => IsEditing && !string.Equals(EditText, _editOriginal, StringComparison.Ordinal);
    public string NextQueueTitle => QueueItems.FirstOrDefault()?.Title ?? "Choose clips, then press Ctrl+Q";
    public string SelectedUsage => SelectedItem is null ? "" : $"Copied {SelectedItem.Entry.CopyCount:N0} time{(SelectedItem.Entry.CopyCount == 1 ? "" : "s")}";
    public string SelectedCopiedAt => SelectedItem?.Entry.LastCopiedAt.ToLocalTime().ToString("dd MMM yyyy, HH:mm") ?? "";
    public bool ShowColor => _loaded?.Text is not null && SelectedItem?.Entry.Kind == ClipKind.Color && HasPreview;
    public Brush ColorPreview
    {
        get
        {
            return HexColor.TryParse(_loaded?.Text, out var color)
                ? new SolidColorBrush(Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue)) : Brushes.Transparent;
        }
    }
    public RelayCommand RenameCommand { get; private set; } = null!;
    public AsyncRelayCommand SaveRenameCommand { get; private set; } = null!;
    public RelayCommand CancelRenameCommand { get; private set; } = null!;
    public RelayCommand FilterSourceCommand { get; private set; } = null!;
    public RelayCommand OpenSearchHelpCommand { get; private set; } = null!;
    public RelayCommand ToggleQueueCommand { get; private set; } = null!;
    public RelayCommand QueueUpCommand { get; private set; } = null!;
    public RelayCommand QueueDownCommand { get; private set; } = null!;
    public RelayCommand QueueRemoveCommand { get; private set; } = null!;
    public AsyncRelayCommand CopyQueueNextCommand { get; private set; } = null!;
    public RelayCommand DismissErrorCommand { get; private set; } = null!;
    public RelayCommand DiscardEditCommand { get; private set; } = null!;
    public RelayCommand KeepEditingCommand { get; private set; } = null!;
    private void InitializeWorkspaceCommands()
    {
        _selectedSort = SortOptions.First(option => option.Sort == _workspace.Settings.HistorySort);
        _wrapText = _workspace.Settings.PreviewWrap;
        RenameCommand = new RelayCommand(() =>
        {
            ClosePopovers(); _renameId = SelectedItem!.Id; RenameText = SelectedItem.Title; RenameOpen = true;
        }, () => CanAct() && SelectedIds().Length == 1);
        CancelRenameCommand = new RelayCommand(() => RenameOpen = false, () => !IsWorking);
        SaveRenameCommand = new AsyncRelayCommand(SaveRenameAsync, ReportError,
            () => RenameOpen && _renameId.HasValue && !IsWorking && !_workspace.Clipboard.IsLocked);
        _asyncCommands.Add(SaveRenameCommand);
        FilterSourceCommand = new RelayCommand(() => { var source = SelectedItem!.Source; SetFilter("all"); SearchText = SearchQuery.AppFilter(source); },
            () => SelectedItem is not null && !IsEditing && !_workspace.Clipboard.IsLocked);
        OpenSearchHelpCommand = new RelayCommand(() => { ClosePopovers(); SearchHelpOpen = true; });
        ToggleQueueCommand = new RelayCommand(() => QueueOpen = !QueueOpen, () => HasQueue);
        QueueUpCommand = new RelayCommand(() => MoveQueue(-1), () => CanUseQueue() && SelectedQueueItem is { Position: > 1 });
        QueueDownCommand = new RelayCommand(() => MoveQueue(1), () => CanUseQueue() && SelectedQueueItem is { } item && item.Position < _queue.Count);
        QueueRemoveCommand = new RelayCommand(() =>
        {
            if (SelectedQueueItem is { } item) { _queue.Remove(item.Ticket); QueueChanged(); }
        }, () => CanUseQueue() && SelectedQueueItem is not null);
        CopyQueueNextCommand = new AsyncRelayCommand(CopyQueueNextAsync, ReportError, CanUseQueue);
        _asyncCommands.Add(CopyQueueNextCommand);
        DismissErrorCommand = new RelayCommand(() => Notify("Ready."));
        DiscardEditCommand = new RelayCommand(CancelEdit, () => !IsWorking);
        KeepEditingCommand = new RelayCommand(() => { DiscardEditOpen = false; EditorFocusRequested?.Invoke(); });
    }
    private async Task SaveRenameAsync()
    {
        var id = _renameId; if (id is null) return;
        var title = RenameText;
        var decision = PrivacyPolicy.Evaluate(new ClipPayload { Text = title }, "PasteDock", _workspace.Settings);
        if (!decision.Allowed) throw new InvalidOperationException(decision.Reason);
        IsWorking = true;
        try
        {
            await Task.Run(() => _workspace.History.RenameAsync(id.Value, title));
            RenameOpen = false; Notify("Clip renamed. Its complete content is unchanged.");
        }
        finally { IsWorking = false; }
    }
    private void RequestCancelEdit()
    {
        if (EditDirty && !string.IsNullOrWhiteSpace(EditText)) { DiscardEditOpen = true; return; }
        CancelEdit();
    }
    private bool CanUseQueue() => HasQueue && !IsWorking && !IsEditing && !_workspace.Clipboard.IsLocked;
    private void RefreshWorkspaceCommands()
    {
        RenameCommand?.Refresh(); CancelRenameCommand?.Refresh(); FilterSourceCommand?.Refresh(); CancelEditCommand?.Refresh();
        DiscardEditCommand?.Refresh(); ToggleQueueCommand?.Refresh(); RefreshQueueCommands();
        Raise(nameof(SelectedUsage)); Raise(nameof(SelectedCopiedAt)); Raise(nameof(ShowColor)); Raise(nameof(ShowText)); Raise(nameof(ColorPreview));
    }
    private void RefreshQueueCommands()
    { QueueUpCommand?.Refresh(); QueueDownCommand?.Refresh(); QueueRemoveCommand?.Refresh(); }
    private void Enqueue()
    {
        var ids = SelectedIds();
        try { _queue.Add(ids); }
        catch (InvalidOperationException error) { ReportError(error); return; }
        QueueChanged(); QueueOpen = true;
        Notify($"{ids.Length} added to the queue. Review its order below, then paste one at a time.");
    }
    private void MoveQueue(int direction)
    {
        if (SelectedQueueItem is { } item && _queue.Move(item.Ticket, direction)) QueueChanged();
    }
    private void RefreshQueueItems()
    {
        var selected = SelectedQueueItem?.Ticket;
        var entries = _workspace.History.Snapshot.ToDictionary(entry => entry.Id);
        QueueItems.Clear(); var position = 0;
        foreach (var item in _queue.Snapshot)
        {
            entries.TryGetValue(item.ClipId, out var entry);
            QueueItems.Add(new(item.Ticket, item.ClipId, ++position, entry?.Title ?? "Clip no longer available",
                entry is null ? "Remove this item or advance to skip it" : $"{entry.Kind} · {entry.SourceApp}", entry is null));
        }
        SelectedQueueItem = QueueItems.FirstOrDefault(item => item.Ticket == selected) ?? QueueItems.FirstOrDefault();
        Raise(nameof(NextQueueTitle)); RefreshQueueCommands();
    }
    private void QueueChanged()
    {
        RefreshQueueItems(); if (_queue.Count == 0) QueueOpen = false;
        Raise(nameof(QueueLabel)); Raise(nameof(HasQueue)); RefreshCommands();
    }
    private async Task CopyQueueNextAsync()
    {
        if (!_queue.TryPeek(out var item)) return;
        IsWorking = true;
        try
        {
            ClipPayload payload;
            try { payload = await Task.Run(() => _workspace.History.LoadAsync(item.ClipId)); }
            catch (KeyNotFoundException) { _queue.Complete(item.Ticket); QueueChanged(); Notify("A deleted queue item was skipped. Choose Copy & advance again for the next item."); return; }
            await _workspace.Clipboard.WriteAsync(payload, _workspace.Settings.PreventCloudAndSystemHistory);
            _queue.Complete(item.Ticket); QueueChanged();
            Notify($"Copied the next clip. {_queue.Count} remain queued. Paste it manually with Ctrl+V.");
        }
        finally { IsWorking = false; }
    }
    private async Task PasteNextAsync()
    {
        if (!_queue.TryPeek(out var item)) return;
        var target = _target; IsWorking = true;
        try
        {
            ClipPayload payload;
            try { payload = await Task.Run(() => _workspace.History.LoadAsync(item.ClipId)); }
            catch (KeyNotFoundException) { _queue.Complete(item.Ticket); QueueChanged(); Notify("A deleted queue item was skipped. Press the shortcut again for the next item."); ShowRequested?.Invoke(); return; }
            var forcePlain = ShouldForcePlain(target, [item.ClipId]);
            if (forcePlain) payload = new ClipPayload { Text = payload.GetPlainText() };
            var result = await _workspace.Paste.PasteAsync(payload, target, _workspace.Settings.PreventCloudAndSystemHistory, () => HideRequested?.Invoke());
            if (result.InputSent) { _queue.Complete(item.Ticket); QueueChanged(); }
            else ShowRequested?.Invoke();
            Notify(result.InputSent ? $"Paste sent. {_queue.Count} remaining in queue." + (forcePlain ? $" Plain-text rule applied for {target!.AppName}." : "")
                : result.Message + " The item remains queued.");
        }
        finally { IsWorking = false; }
    }
}
