using System.Windows.Threading;

namespace PasteDock.Desktop.ViewModels;

internal sealed partial class ShellViewModel
{
    private DispatcherTimer _draftTimer = null!;
    private readonly SemaphoreSlim _draftGate = new(1, 1);
    private EditorDraft? _recoveryDraft;
    private bool _draftRecoveryProblem, _discardRecoveryOpen, _editingHasOtherFormats;
    private Guid _draftId;
    private long _draftRevision;
    private string _draftStatus = "Write to start an encrypted recovery copy.";
    public string DraftStatus { get => _draftStatus; private set => Set(ref _draftStatus, value); }
    public bool DraftRecoveryPending => _recoveryDraft is not null || _draftRecoveryProblem;
    public bool DiscardRecoveryOpen { get => _discardRecoveryOpen; set => Set(ref _discardRecoveryOpen, value); }
    public bool EditingExisting => _editingId.HasValue;
    public string EditorSaveLabel => EditingExisting ? "Save changes" : "Save snippet";
    public string EditorHeading => EditingExisting ? "Edit a saved clip" : "Make it worth keeping.";
    public string EditorLengthLabel => $"{EditText.Length:N0} characters";
    public string EditorFormatHint => EditingExisting && _editingHasOtherFormats
        ? "Saving changes replaces the clip's other formats with plain text. Save as new keeps the original."
        : "Saves plain text. The clipboard stays unchanged.";
    private static bool HasOtherFormats(ClipPayload payload) => payload.Html is not null || payload.Rtf is not null
        || payload.Png is not null || payload.Files.Length > 0 || payload.OcrText is not null;
    public string RecoveryHeading => _draftRecoveryProblem ? "An editor recovery copy needs attention" : "Your unfinished draft is here";
    public string RecoveryDescription => _draftRecoveryProblem
        ? "The recovery copy could not be read. Retry after checking storage access, or discard it to start a new edit. Saved history is available."
        : $"Saved locally {_recoveryDraft?.SavedAt.ToLocalTime():dd MMM, HH:mm}. Restore it to continue. Recovery copies expire after seven days.";
    public AsyncRelayCommand RestoreDraftCommand { get; private set; } = null!;
    public AsyncRelayCommand RetryDraftCommand { get; private set; } = null!;
    public RelayCommand DiscardRecoveryCommand { get; private set; } = null!;
    public RelayCommand KeepRecoveryCommand { get; private set; } = null!;
    public AsyncRelayCommand ConfirmDiscardRecoveryCommand { get; private set; } = null!;
    public AsyncRelayCommand SaveEditAsNewCommand { get; private set; } = null!;

    private void InitializeDraftRecovery()
    {
        _recoveryDraft = _workspace.RecoveryDraft; _draftRecoveryProblem = _workspace.DraftRecoveryProblem;
        _draftTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = TimeSpan.FromSeconds(1) };
        _draftTimer.Tick += async (_, _) => { _draftTimer.Stop(); await FlushEditorDraftAsync(); };
        RestoreDraftCommand = new AsyncRelayCommand(RestoreDraftAsync, ReportError,
            () => _recoveryDraft is not null && !IsEditing && !IsWorking && !_workspace.Clipboard.IsLocked);
        RetryDraftCommand = new AsyncRelayCommand(async () =>
        {
            IsWorking = true;
            try
            {
                await _workspace.LoadDraftRecoveryAsync();
                _recoveryDraft = _workspace.RecoveryDraft; _draftRecoveryProblem = _workspace.DraftRecoveryProblem;
                RefreshDraftRecovery();
            }
            finally { IsWorking = false; }
        }, ReportError, () => DraftRecoveryPending && !IsWorking && !_workspace.Clipboard.IsLocked);
        DiscardRecoveryCommand = new RelayCommand(() => DiscardRecoveryOpen = true, () => DraftRecoveryPending && !IsWorking);
        KeepRecoveryCommand = new RelayCommand(() => DiscardRecoveryOpen = false);
        ConfirmDiscardRecoveryCommand = new AsyncRelayCommand(async () =>
        {
            IsWorking = true;
            try { await ClearEditorRecoveryAsync(); DiscardRecoveryOpen = false; Notify("Recovery copy discarded. Your saved clips are unchanged."); }
            finally { IsWorking = false; }
        }, ReportError, () => DraftRecoveryPending && DiscardRecoveryOpen && !IsWorking && !_workspace.Clipboard.IsLocked);
        SaveEditAsNewCommand = new AsyncRelayCommand(() => SaveEditorAsync(true), ReportError,
            () => IsEditing && EditingExisting && !IsWorking && !_workspace.Clipboard.IsLocked);
        _asyncCommands.AddRange([RestoreDraftCommand, RetryDraftCommand, ConfirmDiscardRecoveryCommand, SaveEditAsNewCommand]);
        RefreshDraftRecovery();
    }

    private void RaiseEditorIdentity()
    { Raise(nameof(EditingExisting)); Raise(nameof(EditorSaveLabel)); Raise(nameof(EditorHeading)); Raise(nameof(EditorFormatHint)); SaveEditAsNewCommand?.Refresh(); }

    private void StartDraftSession()
    {
        _draftId = Guid.NewGuid(); RaiseEditorIdentity();
        DraftStatus = _workspace.Settings.RecoverEditorDrafts
            ? "Changes get an encrypted recovery copy after one second."
            : "Recovery is off. Keep PasteDock open until you save.";
        if (EditDirty) ScheduleDraftSave();
    }

    private void ScheduleDraftSave()
    {
        _draftRevision++;
        Raise(nameof(EditorLengthLabel));
        if (!IsEditing || _disposed || _draftTimer is null) return;
        DraftStatus = "Changes waiting for an encrypted recovery copy...";
        if (!_draftTimer.IsEnabled) _draftTimer.Start();
    }

    // All snapshots and completion checks run on the dispatcher. Storage runs off the UI thread.
    // Recheck editor state after the gate: an old timer must never recreate a discarded draft.
    public async Task FlushEditorDraftAsync()
    {
        await _draftGate.WaitAsync();
        try
        {
            if (_disposed || !IsEditing) return;
            var revision = _draftRevision;
            if (!EditDirty)
            {
                await Task.Run(() => _workspace.Drafts.ClearAsync());
                if (revision == _draftRevision) DraftStatus = "No unsaved changes.";
                return;
            }
            var draft = new EditorDraft(_draftId, _editingId, _editingBlobId, EditText, DateTimeOffset.UtcNow);
            var result = await Task.Run(() => _workspace.Drafts.SaveCurrentAsync(draft, () => _workspace.Settings));
            if (revision != _draftRevision || !IsEditing) return;
            DraftStatus = result switch
            {
                DraftSaveStatus.Saved => $"Recovery copy encrypted at {DateTime.Now:HH:mm:ss} on this device.",
                DraftSaveStatus.Empty => "Empty draft. No recovery copy stored.",
                DraftSaveStatus.Disabled => "Recovery is off. Keep PasteDock open until you save.",
                DraftSaveStatus.TooLarge => "Too large for recovery (1,048,576 characters). Save your snippet before closing.",
                _ => "Privacy rules blocked recovery. Your text is only in this open editor."
            };
        }
        catch (Exception exception)
        {
            _workspace.Log.Write("DRAFT_SAVE_FAILED", exception);
            DraftStatus = "Recovery could not be saved. Keep PasteDock open, or save your snippet and check free disk space.";
        }
        finally { _draftGate.Release(); }
    }

    private async Task ClearEditorRecoveryAsync()
    {
        _draftTimer.Stop();
        await _draftGate.WaitAsync();
        try
        {
            await Task.Run(() => _workspace.Drafts.ClearAsync());
            _recoveryDraft = null; _draftRecoveryProblem = false; RefreshDraftRecovery();
        }
        finally { _draftGate.Release(); }
    }

    private async Task DiscardEditorAsync()
    {
        IsWorking = true;
        try { await ClearEditorRecoveryAsync(); CancelEdit(); }
        finally { IsWorking = false; }
    }

    private async Task RestoreDraftAsync()
    {
        IsWorking = true;
        try
        {
            // Read again so expiry and current privacy settings also apply after a long-running session.
            var draft = await Task.Run(() => _workspace.Drafts.LoadAsync(_workspace.Settings));
            if (draft is null) { _recoveryDraft = null; RefreshDraftRecovery(); Notify("This recovery copy expired or was removed by your privacy settings."); return; }
            _editingId = null; _editingBlobId = null; _editOriginal = ""; _editingHasOtherFormats = false;
            if (draft.ClipId is Guid id)
            {
                try
                {
                    var original = await Task.Run(() => _workspace.History.LoadRevisionAsync(id));
                    if (original.Entry.BlobId == draft.BaseBlobId && original.Payload.Text is not null)
                    { _editingId = id; _editingBlobId = draft.BaseBlobId; _editOriginal = original.Payload.Text; _editingHasOtherFormats = HasOtherFormats(original.Payload); }
                }
                catch (KeyNotFoundException) { }
            }
            _recoveryDraft = null; _draftRecoveryProblem = false; RefreshDraftRecovery(); ClosePopovers();
            EditText = draft.Text; IsEditing = true; StartDraftSession();
            Notify(draft.ClipId.HasValue && !EditingExisting
                ? "Draft restored as a new snippet because the original changed or was removed."
                : "Draft restored. Review it before saving.");
            EditorFocusRequested?.Invoke();
        }
        finally { IsWorking = false; }
    }

    private void RefreshDraftRecovery()
    {
        Raise(nameof(DraftRecoveryPending)); Raise(nameof(RecoveryHeading)); Raise(nameof(RecoveryDescription));
        DiscardRecoveryCommand?.Refresh();
        Raise(nameof(CanEdit)); RefreshCommands();
    }
}
