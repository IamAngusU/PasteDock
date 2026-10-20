using System.Windows;
using System.Windows.Controls;
using PasteDock.Desktop.ViewModels;
using PasteDock.Desktop.Views;

namespace PasteDock.Desktop.Services;

internal static partial class UiSmokeDiagnostics
{
    private static async Task RunDraftScenariosAsync(string root, string output, List<object> checks)
    {
        await using var workspace = new WorkspaceService(root);
        workspace.Clipboard.SetPaused(true); await workspace.History.InitializeAsync();
        ShellViewModel? shell = null; MainWindow? window = null;
        async Task ReopenAsync()
        {
            shell?.Dispose(); window?.CloseForExit();
            await workspace.LoadDraftRecoveryAsync();
            shell = new ShellViewModel(workspace, Application.Current.Dispatcher);
            window = new MainWindow(shell) { ShowActivated = false, ShowInTaskbar = false, Topmost = false, Left = -32000, Top = -32000, Width = 1080, Height = 700 };
            window.Show(); await SettleAsync(shell);
        }
        try
        {
            await ReopenAsync();
            shell!.NewSnippetCommand.Execute(null); shell.EditText = "A persisted safe draft.";
            await shell.FlushEditorDraftAsync();
            shell.EditText = "-----BEGIN PRIVATE KEY-----\nsynthetic test fixture";
            await shell.FlushEditorDraftAsync();
            if (await workspace.Drafts.LoadAsync(workspace.Settings) is not null || !shell.DraftStatus.Contains("Privacy"))
                throw new InvalidOperationException("Secret editor text entered recoverable storage or left a stale safe revision.");
            checks.Add(new { name = "Draft autosave applies privacy rules and clears the stale recovery copy", status = "passed" });

            shell.EditText = "Discard must win over an in-flight autosave.";
            var pendingSave = shell.FlushEditorDraftAsync();
            shell.DiscardEditCommand.Execute(null); await WaitCommandAsync(shell.DiscardEditCommand); await pendingSave;
            await shell.FlushEditorDraftAsync();
            if (shell.IsEditing || await workspace.Drafts.LoadAsync(workspace.Settings) is not null)
                throw new InvalidOperationException("An old autosave recreated a discarded draft.");
            checks.Add(new { name = "Explicit discard wins over an in-flight autosave", status = "passed" });

            shell.NewSnippetCommand.Execute(null);
            shell.EditText = "Release notes in progress\n\nThe next copy stays local.\nThis text is a synthetic recovery fixture.";
            var expected = shell.EditText;
            // Exercise the real timer, then simulate process loss without a graceful editor save.
            await Task.Delay(1400); await Application.Current.Dispatcher.InvokeAsync(() => { });
            if (await workspace.Drafts.LoadAsync(workspace.Settings) is null)
                throw new InvalidOperationException("The actual editor timer did not persist its recovery copy.");
            await ReopenAsync();
            if (!shell!.DraftRecoveryPending || shell.NewSnippetCommand.CanExecute(null) || shell.EditCommand.CanExecute(null))
                throw new InvalidOperationException("An unresolved recovery draft could be overwritten by a new edit.");
            await CaptureAsync(window!, Path.Combine(output, "light-draft-recovery.png"));
            checks.Add(new { name = "Real autosave timer and restart recovery affordance", status = "passed" });
            shell.RestoreDraftCommand.Execute(null); await WaitCommandAsync(shell.RestoreDraftCommand);
            if (!shell.IsEditing || shell.EditText != expected || shell.DraftRecoveryPending)
                throw new InvalidOperationException("Restoring did not preserve exact draft text.");
            await shell.FlushEditorDraftAsync(); await CaptureAsync(window!, Path.Combine(output, "light-recovered-editor.png"));
            ThemeService.Apply("Dark"); await CaptureAsync(window!, Path.Combine(output, "dark-recovered-editor.png"));
            ThemeService.Apply("Light");
            window!.MinWidth = 620; window.Width = 720; window.Height = 560; await SettleAsync(shell);
            if (window.FindName("HistoryColumn") is System.Windows.Controls.ColumnDefinition column) column.Width = new GridLength(230);
            await CaptureAsync(window, Path.Combine(output, "compact-recovered-editor.png"));
            if (window.ActualWidth > 721 || window.FindName("NavigationPane") is not FrameworkElement { Visibility: Visibility.Collapsed }
                || window.FindName("Editor") is not TextBox { ActualWidth: > 300 })
                throw new InvalidOperationException("The recovered editor did not fit the actual compact window.");
            checks.Add(new { name = "Recovered editor renders in dark and actual compact layouts", status = "passed" });
            window.Width = 1080; window.Height = 700;
            if (window.FindName("HistoryColumn") is System.Windows.Controls.ColumnDefinition fullColumn) fullColumn.Width = new GridLength(350);
            shell.SaveEditCommand.Execute(null); await WaitCommandAsync(shell.SaveEditCommand); await SettleAsync(shell);
            if (shell.IsEditing || await workspace.Drafts.LoadAsync(workspace.Settings) is not null || workspace.History.Snapshot.Count != 1)
                throw new InvalidOperationException("A successful save did not commit the snippet and clear recovery.");
            checks.Add(new { name = "Restored editor saves exact text and removes recovery after commit", status = "passed" });

            var original = workspace.History.Snapshot.Single();
            await workspace.History.RenameAsync(original.Id, "Original handoff notes"); await SettleAsync(shell);
            shell.EditCommand.Execute(null);
            if (!shell.EditingExisting || !shell.SaveEditAsNewCommand.CanExecute(null)) throw new InvalidOperationException("Edit does not expose Save as new.");
            shell.SaveEditAsNewCommand.Execute(null); await WaitCommandAsync(shell.SaveEditAsNewCommand); await SettleAsync(shell);
            if (workspace.History.Snapshot.Count != 2 || workspace.History.Snapshot.Single(e => e.Id == original.Id).Title != "Original handoff notes")
                throw new InvalidOperationException("Saving an unchanged intentional copy merged or changed the original.");
            checks.Add(new { name = "Save as new preserves an unchanged original as a separate snippet", status = "passed" });

            shell.SelectedItem = shell.Items.First(e => e.Id == original.Id); await SettleAsync(shell);
            shell.EditCommand.Execute(null); shell.EditText = "My edited draft";
            await workspace.History.ReplacePayloadAsync(original.Id, new ClipPayload { Text = "A newer stored revision" }, workspace.Settings);
            shell.SaveEditCommand.Execute(null); await WaitCommandAsync(shell.SaveEditCommand);
            if (!shell.IsEditing || !shell.HasError || shell.EditText != "My edited draft"
                || (await workspace.History.LoadAsync(original.Id)).Text != "A newer stored revision")
                throw new InvalidOperationException("A stale editor overwrote newer content or lost the user's draft.");
            await CaptureAsync(window!, Path.Combine(output, "light-editor-conflict.png"));
            shell.SaveEditAsNewCommand.Execute(null); await WaitCommandAsync(shell.SaveEditAsNewCommand); await SettleAsync(shell);
            if (shell.IsEditing || !workspace.History.Snapshot.Any(e => e.Preview == "My edited draft"))
                throw new InvalidOperationException("Save as new failed to resolve an editor conflict.");
            checks.Add(new { name = "Stale edit conflict preserves both versions through Save as new", status = "passed" });

            shell.SelectedItem = shell.Items.First(e => e.Id == original.Id); await SettleAsync(shell);
            shell.EditCommand.Execute(null); shell.EditText = "Recovered after the original was removed.";
            await shell.FlushEditorDraftAsync(); await workspace.History.DeleteAsync([original.Id]);
            await ReopenAsync(); shell!.RestoreDraftCommand.Execute(null); await WaitCommandAsync(shell.RestoreDraftCommand);
            if (!shell.IsEditing || shell.EditingExisting || shell.EditText != "Recovered after the original was removed.")
                throw new InvalidOperationException("Recovery attached a deleted original to an unsafe overwrite path.");
            shell.DiscardEditCommand.Execute(null); await WaitCommandAsync(shell.DiscardEditCommand);
            checks.Add(new { name = "Missing-original recovery becomes a safe new snippet", status = "passed" });

            var recoveryPath = Path.Combine(root, "editor-draft.pdv");
            shell.NewSnippetCommand.Execute(null); shell.EditText = "An editor stays open if discard cannot remove recovery.";
            await shell.FlushEditorDraftAsync();
            using (var lockedFile = new FileStream(recoveryPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                shell.DiscardEditCommand.Execute(null); await WaitCommandAsync(shell.DiscardEditCommand);
                if (!shell.IsEditing || !shell.HasError || shell.EditText != "An editor stays open if discard cannot remove recovery.")
                    throw new InvalidOperationException("A failed discard lost the editor draft.");
            }
            shell.DiscardEditCommand.Execute(null); await WaitCommandAsync(shell.DiscardEditCommand);
            checks.Add(new { name = "Failed draft discard preserves editable text and permits retry", status = "passed" });

            shell.NewSnippetCommand.Execute(null); shell.EditText = "A committed snippet survives recovery cleanup failure.";
            await shell.FlushEditorDraftAsync();
            using (var lockedFile = new FileStream(recoveryPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                shell.SaveEditCommand.Execute(null); await WaitCommandAsync(shell.SaveEditCommand);
                if (shell.IsEditing || !shell.HasError || !shell.DraftRecoveryPending
                    || !workspace.History.Snapshot.Any(e => e.Preview == "A committed snippet survives recovery cleanup failure."))
                    throw new InvalidOperationException("Cleanup failure obscured a successful history commit.");
            }
            shell.DiscardRecoveryCommand.Execute(null); shell.ConfirmDiscardRecoveryCommand.Execute(null); await WaitCommandAsync(shell.ConfirmDiscardRecoveryCommand);
            if (shell.DraftRecoveryPending) throw new InvalidOperationException("Saved recovery cleanup could not be retried.");
            checks.Add(new { name = "Committed snippet remains safe when recovery cleanup fails", status = "passed" });

            await File.WriteAllBytesAsync(recoveryPath, [1, 2, 3]);
            await ReopenAsync();
            if (!shell!.DraftRecoveryPending || shell.RestoreDraftCommand.CanExecute(null) || shell.NewSnippetCommand.CanExecute(null))
                throw new InvalidOperationException("Unreadable recovery was silently overwritten.");
            shell.DiscardRecoveryCommand.Execute(null); shell.KeepRecoveryCommand.Execute(null);
            if (!File.Exists(recoveryPath)) throw new InvalidOperationException("Keep draft removed its recovery file.");
            shell.DiscardRecoveryCommand.Execute(null);
            await CaptureAsync(window!, Path.Combine(output, "light-draft-recovery-error.png"));
            shell.ConfirmDiscardRecoveryCommand.Execute(null); await WaitCommandAsync(shell.ConfirmDiscardRecoveryCommand);
            if (shell.DraftRecoveryPending || !shell.NewSnippetCommand.CanExecute(null) || File.Exists(recoveryPath))
                throw new InvalidOperationException("Explicit recovery discard did not unblock the editor.");
            checks.Add(new { name = "Unreadable recovery has retry and explicit discard without replacing history", status = "passed" });

            var rich = await workspace.History.CaptureAsync(new ClipPayload { Text = "Formatted original", Html = "<b>Formatted original</b>" }, "Synthetic Word", workspace.Settings);
            shell.FilterCommand.Execute("all"); await SettleAsync(shell);
            shell.SelectedItem = shell.Items.Single(item => item.Id == rich.Id); await SettleAsync(shell);
            shell.EditCommand.Execute(null);
            if (!shell.EditorFormatHint.Contains("other formats")) throw new InvalidOperationException("A rich clip's destructive plain-text save was not explained.");
            shell.EditText = "Plain variant"; shell.SaveEditAsNewCommand.Execute(null); await WaitCommandAsync(shell.SaveEditAsNewCommand); await SettleAsync(shell);
            if ((await workspace.History.LoadAsync(rich.Id)).Html != "<b>Formatted original</b>"
                || shell.SelectedItem is null || (await workspace.History.LoadAsync(shell.SelectedItem.Id)).Html is not null
                || shell.SelectedItem.Preview != "Plain variant")
                throw new InvalidOperationException("Save as new changed the rich original or failed to reveal the saved variant.");
            var variantId = shell.SelectedItem.Id;
            shell.SearchText = "Plain variant"; await SettleAsync(shell);
            shell.EditCommand.Execute(null); shell.EditText = "Changed beyond the previous search.";
            shell.SaveEditCommand.Execute(null); await WaitCommandAsync(shell.SaveEditCommand); await SettleAsync(shell);
            if (shell.SelectedItem?.Id != variantId || shell.HasSearch || shell.PreviewText != "Changed beyond the previous search.")
                throw new InvalidOperationException("A successful edit disappeared behind its previous search filter.");
            checks.Add(new { name = "Rich clip edit preserves original formats and successful saves reveal the result", status = "passed" });

            shell.NewSnippetCommand.Execute(null); shell.EditText = "Draft kept in RAM after disabling recovery.";
            await shell.FlushEditorDraftAsync(); shell.OpenSettingsCommand.Execute(null); shell.Settings.RecoverDrafts = false;
            shell.SaveSettingsCommand.Execute(null); await WaitCommandAsync(shell.SaveSettingsCommand);
            await shell.FlushEditorDraftAsync();
            if (workspace.Settings.RecoverEditorDrafts || File.Exists(recoveryPath) || !shell.IsEditing || shell.EditText != "Draft kept in RAM after disabling recovery.")
                throw new InvalidOperationException("Recovery opt-out lost the live editor or retained its disk copy.");
            shell.DiscardEditCommand.Execute(null); await WaitCommandAsync(shell.DiscardEditCommand);
            checks.Add(new { name = "Recovery setting removes disk copy while preserving the live editor", status = "passed" });
        }
        finally { shell?.Dispose(); window?.CloseForExit(); }
    }
}
