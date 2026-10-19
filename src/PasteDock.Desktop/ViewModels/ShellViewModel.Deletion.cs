namespace PasteDock.Desktop.ViewModels;

internal sealed partial class ShellViewModel
{
    public AsyncRelayCommand UndoDeleteCommand { get; }
    public AsyncRelayCommand ForgetDeletionCommand { get; }
    public AsyncRelayCommand PermanentDeleteCommand { get; }
    public bool HasDeletionUndo => _workspace.History.DeletionUndo is not null;
    public string DeletionUndoLabel
    {
        get
        {
            var undo = _workspace.History.DeletionUndo;
            if (undo is null) return "";
            var minutes = Math.Max(1, (int)Math.Ceiling((undo.ExpiresAt - DateTimeOffset.UtcNow).TotalMinutes));
            return $"{undo.ItemCount} deleted · {minutes}m left";
        }
    }
    public string UndoStorageLabel
    {
        get
        {
            var bytes = _workspace.History.PendingDeletionBytes;
            return bytes > 0 ? $"+ {ClipItemViewModel.FormatBytes(bytes)} undo copy" : "";
        }
    }

    private bool CanUndoDelete() => HasDeletionUndo && !IsWorking && !IsEditing && !_workspace.Clipboard.IsLocked;
    private void RefreshDeletion()
    {
        Raise(nameof(HasDeletionUndo)); Raise(nameof(DeletionUndoLabel)); Raise(nameof(UndoStorageLabel));
        UndoDeleteCommand?.Refresh(); ForgetDeletionCommand?.Refresh();
    }
    private async Task UndoDeleteAsync()
    {
        IsWorking = true;
        try
        {
            var result = await Task.Run(() => _workspace.History.UndoLastDeleteAsync(_workspace.Settings));
            Notify($"Restored {result.Restored} clips; merged {result.Merged} recaptured duplicates. The clipboard is unchanged.");
        }
        finally { IsWorking = false; RefreshDeletion(); }
    }
    private async Task ForgetDeletionAsync()
    {
        IsWorking = true;
        try
        {
            await Task.Run(() => _workspace.History.ForgetDeletionAsync());
            Notify("The encrypted undo copy was discarded. The system clipboard is unchanged.");
        }
        finally { IsWorking = false; RefreshDeletion(); }
    }
    private async Task PermanentDeleteAsync()
    {
        var ids = _pendingDeleteIds;
        IsWorking = true;
        try
        {
            await Task.Run(() => _workspace.History.DeleteAsync(ids));
            DeleteOpen = false; _pendingDeleteIds = [];
            Notify("Selected clips and the previous undo copy were deleted. The system clipboard is unchanged.");
        }
        finally { IsWorking = false; RefreshDeletion(); }
    }
}
