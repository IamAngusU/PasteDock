using PasteDock.Core.Models;
using PasteDock.Core.Storage;

namespace PasteDock.Core.Services;

public enum DraftSaveStatus { Saved, Empty, Disabled, Blocked, TooLarge }

/// <summary>One bounded encrypted draft, independent of history and portable backups.</summary>
public sealed class DraftRepository(EncryptedFileStore store, TimeProvider? timeProvider = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<EditorDraft?> LoadAsync(AppSettings settings, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!settings.RecoverEditorDrafts) { store.DeleteDraft(); return null; }
            var draft = await store.LoadDraftAsync(ct).ConfigureAwait(false);
            if (draft is null) return null;
            if (_time.GetUtcNow() - draft.SavedAt >= EditorDraft.Lifetime
                || !PrivacyPolicy.Evaluate(new ClipPayload { Text = draft.Text }, "PasteDock", settings).Allowed)
            { store.DeleteDraft(); return null; }
            return draft;
        }
        finally { _gate.Release(); }
    }

    public Task<DraftSaveStatus> SaveAsync(EditorDraft draft, AppSettings settings, CancellationToken ct = default) =>
        SaveCurrentAsync(draft, () => settings, ct);

    public async Task<DraftSaveStatus> SaveCurrentAsync(EditorDraft draft, Func<AppSettings> currentSettings, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var settings = currentSettings();
            var status = !settings.RecoverEditorDrafts ? DraftSaveStatus.Disabled
                : string.IsNullOrWhiteSpace(draft.Text) ? DraftSaveStatus.Empty
                : draft.Text.Length > EditorDraft.MaxCharacters ? DraftSaveStatus.TooLarge
                : !PrivacyPolicy.Evaluate(new ClipPayload { Text = draft.Text }, "PasteDock", settings).Allowed ? DraftSaveStatus.Blocked
                : DraftSaveStatus.Saved;
            // Remove a previously recoverable revision when the current text cannot be persisted.
            if (status != DraftSaveStatus.Saved) { store.DeleteDraft(); return status; }
            draft = draft with { SavedAt = _time.GetUtcNow() };
            draft.Validate();
            await store.SaveDraftAsync(draft, ct).ConfigureAwait(false);
            return status;
        }
        finally { _gate.Release(); }
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { store.DeleteDraft(); }
        finally { _gate.Release(); }
    }
}
