using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PasteDock.Desktop.ViewModels;
using PasteDock.Desktop.Views;

namespace PasteDock.Desktop.Services;

internal static class UiSmokeDiagnostics
{
    /// <summary>Render the real WPF views against an isolated synthetic workspace. No clipboard access or input injection.</summary>
    public static async Task<int> RunAsync(string reportPath)
    {
        var fullReport = Path.GetFullPath(reportPath);
        var output = Path.Combine(Path.GetDirectoryName(fullReport)!, Path.GetFileNameWithoutExtension(fullReport));
        Directory.CreateDirectory(output);
        var root = Path.Combine(Path.GetTempPath(), "PasteDock-ui-smoke-" + Guid.NewGuid().ToString("N"));
        var checks = new List<object>();
        var errors = new BindingErrors();
        var trace = PresentationTraceSources.DataBindingSource;
        var oldLevel = trace.Switch.Level;
        trace.Switch.Level = SourceLevels.Error;
        trace.Listeners.Add(errors);
        WorkspaceService? workspace = null;
        ShellViewModel? shell = null;
        MainWindow? window = null;
        var failed = false;
        var previousRenderMode = RenderOptions.ProcessRenderMode;
        try
        {
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
            workspace = new WorkspaceService(root);
            workspace.Clipboard.SetPaused(true);
            // Deliberately do not call WorkspaceService.InitializeAsync: it starts clipboard monitoring.
            await workspace.History.InitializeAsync();
            var settings = new AppSettings { RetentionDays = 0, ReducedMotion = true };
            var json = await workspace.History.CaptureAsync(new ClipPayload { Text = "{\n  \"project\": \"PasteDock\",\n  \"mode\": \"local-first\"\n}" }, "Visual Studio", settings);
            await workspace.History.UpdateMetadataAsync([json.Id], e => e with { IsPinned = true, Tags = ["development", "project"] });
            await workspace.History.CaptureAsync(new ClipPayload { Text = "https://example.test/reference" }, "Browser", settings);
            await workspace.History.CaptureAsync(new ClipPayload { Text = "A place for the things you copy.\nSearch, inspect, edit, and use them again." }, "Notes", settings);
            await workspace.History.CaptureAsync(new ClipPayload { Text = "#315D4A" }, "Design", settings);
            shell = new ShellViewModel(workspace, Application.Current.Dispatcher);
            window = new MainWindow(shell)
            {
                ShowActivated = false, ShowInTaskbar = false, Topmost = false,
                Left = -32000, Top = -32000, Width = 1080, Height = 700
            };
            window.ApplySettings(settings);
            window.Topmost = false;
            window.Show();
            await SettleAsync(shell);
            if (window.FindName("HistoryList") is not ListBox { Items.Count: 4 })
                throw new InvalidOperationException("The real history view did not bind to all four fixture clips.");
            checks.Add(new { name = "History list data binding", status = "passed" });
            foreach (var theme in new[] { "Light", "Dark" })
            {
                ThemeService.Apply(theme);
                await SettleAsync(shell);
                await CaptureAsync(window, Path.Combine(output, theme.ToLowerInvariant() + "-workspace.png"));
                shell.OpenSettingsCommand.Execute(null);
                await SettleAsync(shell);
                await CaptureAsync(window, Path.Combine(output, theme.ToLowerInvariant() + "-settings.png"));
                shell.SettingsOpen = false;
                checks.Add(new { name = theme + " workspace and settings rendered", status = "passed" });
            }
            ThemeService.Apply("Light");
            if (window.FindName("HistoryList") is not ListBox history || history.Items.Count < 2)
                throw new InvalidOperationException("The history fixture cannot prepare a two-clip comparison.");
            history.SelectedItems.Clear();
            history.SelectedItems.Add(history.Items[0]);
            history.SelectedItems.Add(history.Items[1]);
            shell.SetSelection(history.SelectedItems.Cast<ClipItemViewModel>().ToArray());
            shell.CompareCommand.Execute(null);
            await WaitCommandAsync(shell.CompareCommand);
            await SettleAsync(shell);
            if (!shell.CompareOpen || shell.CompareLines.Count == 0 || window.FindName("ComparePopup") is not System.Windows.Controls.Primitives.Popup { IsOpen: true, Child: FrameworkElement compareVisual })
                throw new InvalidOperationException("The compare popover did not open with rendered diff lines.");
            await CaptureElementAsync(compareVisual, Path.Combine(output, "light-compare-popup.png"));
            shell.CompareOpen = false;
            checks.Add(new { name = "Two-clip diff popover rendered", status = "passed" });

            await workspace.History.DeleteWithUndoAsync([json.Id]);
            await SettleAsync(shell);
            if (!shell.HasDeletionUndo) throw new InvalidOperationException("The undo affordance did not become available.");
            await CaptureAsync(window, Path.Combine(output, "light-deletion-undo.png"));
            checks.Add(new { name = "Durable deletion undo affordance", status = "passed" });
            shell.NewSnippetCommand.Execute(null);
            shell.EditText = "A synthetic draft. No private clipboard content is used by this check.";
            await SettleAsync(shell);
            await CaptureAsync(window, Path.Combine(output, "light-inline-editor.png"));
            shell.CancelEditCommand.Execute(null);
            checks.Add(new { name = "Inline editor rendered", status = "passed" });

            // Exercise recovery paths using only this isolated synthetic workspace.
            shell.NewSnippetCommand.Execute(null);
            shell.EditText = "   ";
            shell.SaveEditCommand.Execute(null); await WaitCommandAsync(shell.SaveEditCommand);
            if (!shell.IsEditing || !shell.HasError) throw new InvalidOperationException("An empty snippet must keep its editor open with a useful error.");
            checks.Add(new { name = "Empty snippet rejected without losing the editor", status = "passed" });
            shell.EditText = "-----BEGIN PRIVATE KEY-----\nsynthetic test fixture";
            shell.SaveEditCommand.Execute(null); await WaitCommandAsync(shell.SaveEditCommand);
            if (!shell.IsEditing || !shell.HasError || workspace.History.Snapshot.Any(e => e.Preview.Contains("PRIVATE KEY")))
                throw new InvalidOperationException("Recognizable secrets must stay out of saved history.");
            checks.Add(new { name = "Secret snippet blocked with draft preserved", status = "passed" });
            shell.EditText = "A draft survives hiding the workspace.";
            window.HidePanel(); window.Show();
            if (!shell.IsEditing || shell.EditText != "A draft survives hiding the workspace.")
                throw new InvalidOperationException("Hiding the workspace lost the draft.");
            shell.CancelEditCommand.Execute(null);
            checks.Add(new { name = "Draft survives hide and reopen", status = "passed" });

            shell.OpenSettingsCommand.Execute(null);
            shell.Settings.Capacity = "not-a-number";
            shell.SaveSettingsCommand.Execute(null); await WaitCommandAsync(shell.SaveSettingsCommand);
            if (!shell.SettingsOpen || string.IsNullOrWhiteSpace(shell.Settings.Error) || workspace.Settings.HistoryCapacity != 2000)
                throw new InvalidOperationException("Invalid settings changed persisted configuration or closed their recovery UI.");
            await CaptureAsync(window, Path.Combine(output, "light-invalid-settings.png"));
            shell.SettingsOpen = false;
            checks.Add(new { name = "Invalid settings rejected with editable recovery", status = "passed" });

            shell.SearchText = "no-such-synthetic-clip"; await SettleAsync(shell);
            if (!shell.IsEmpty || !shell.IsViewFiltered || shell.HasPreview)
                throw new InvalidOperationException("No-results state did not clear the stale preview.");
            await CaptureAsync(window, Path.Combine(output, "light-no-results.png"));
            shell.ResetViewCommand.Execute(null); await SettleAsync(shell);
            if (shell.IsEmpty || shell.IsViewFiltered) throw new InvalidOperationException("Reset view did not restore history.");
            shell.SearchText = "nonexistent"; shell.SearchText = "reference"; await SettleAsync(shell);
            if (shell.Items.Count != 1 || shell.Items[0].Entry.SourceApp != "Browser")
                throw new InvalidOperationException("An older search replaced newer results.");
            shell.ResetViewCommand.Execute(null); await SettleAsync(shell);
            checks.Add(new { name = "Empty search recovery and latest-query cancellation", status = "passed" });

            var malformed = await workspace.History.CaptureAsync(new ClipPayload { Text = "{ invalid json }" }, "Synthetic editor", settings);
            await SettleAsync(shell);
            history.SelectedItem = shell.Items.Single(e => e.Id == malformed.Id); await SettleAsync(shell);
            shell.OpenToolsCommand.Execute(null); shell.SelectedTransform = shell.Transforms[0];
            shell.TransformCommand.Execute(null); await WaitCommandAsync(shell.TransformCommand);
            if (shell.TransformReady || !shell.HasError || (await workspace.History.LoadAsync(malformed.Id)).Text != "{ invalid json }")
                throw new InvalidOperationException("Invalid JSON transformed or replaced its source.");
            shell.ToolsOpen = false;
            checks.Add(new { name = "Invalid JSON leaves original content intact", status = "passed" });

            shell.QueueCommand.Execute(null);
            await workspace.History.DeleteAsync([malformed.Id]); await SettleAsync(shell);
            shell.QueueNextCommand.Execute(null); await WaitCommandAsync(shell.QueueNextCommand);
            if (shell.HasQueue || !shell.Status.Contains("deleted queue item", StringComparison.Ordinal))
                throw new InvalidOperationException("Deleted queue entries did not recover without writing the clipboard.");
            checks.Add(new { name = "Deleted queued clip skipped without clipboard access", status = "passed" });

            window.MinWidth = 620; window.Width = 720; window.Height = 560; await SettleAsync(shell);
            await CaptureAsync(window, Path.Combine(output, "light-compact-workspace.png"));
            if (window.FindName("NavigationPane") is not FrameworkElement { Visibility: Visibility.Collapsed })
                throw new InvalidOperationException("Compact workspace did not release navigation space.");
            window.Width = 1080; window.Height = 700; await SettleAsync(shell);
            checks.Add(new { name = "Compact workspace at 720 by 560", status = "passed" });

            shell.NewSnippetCommand.Execute(null); shell.EditText = "Unsaved draft survives Windows lock.";
            workspace.SetSessionLocked(true); shell.OnSessionLock();
            if (shell.CopyCommand.CanExecute(null) || shell.PasteCommand.CanExecute(null) || shell.SaveEditCommand.CanExecute(null)
                || shell.NewSnippetCommand.CanExecute(null) || shell.HasQueue || shell.PreviewText.Length > 0
                || shell.EditText != "Unsaved draft survives Windows lock.")
                throw new InvalidOperationException("Session lock did not disable actions and clear preview while preserving the draft.");
            workspace.SetSessionLocked(false); shell.CancelEditCommand.Execute(null); window.Show(); shell.OnShown(); await SettleAsync(shell);
            checks.Add(new { name = "Session lock clears preview and disables paste while preserving draft", status = "passed" });

            await workspace.History.DeleteAsync(workspace.History.Snapshot.Select(e => e.Id).ToArray()); await SettleAsync(shell);
            shell.Notify("Ready. Copy something to begin.");
            if (!shell.IsEmpty || shell.IsViewFiltered || shell.EmptyTitle != "Capture is paused")
                throw new InvalidOperationException("First-use paused state is not actionable.");
            await CaptureAsync(window, Path.Combine(output, "light-first-use-paused.png"));
            workspace.TogglePause(); await SettleAsync(shell);
            await CaptureAsync(window, Path.Combine(output, "light-first-use.png"));
            checks.Add(new { name = "First-use and paused empty states rendered", status = "passed" });
            if (errors.Messages.Count > 0) throw new InvalidOperationException("WPF reported binding errors. Inspect bindingErrors in this report.");
        }
        catch (Exception exception)
        {
            failed = true;
            checks.Add(new { name = "WPF UI smoke", status = "failed", error = exception.ToString() });
        }
        finally
        {
            shell?.Dispose(); window?.CloseForExit();
            if (workspace is not null) await workspace.DisposeAsync();
            trace.Listeners.Remove(errors); trace.Switch.Level = oldLevel;
            RenderOptions.ProcessRenderMode = previousRenderMode;
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch (IOException) { checks.Add(new { name = "Synthetic fixture cleanup", status = "incomplete" }); }
            catch (UnauthorizedAccessException) { checks.Add(new { name = "Synthetic fixture cleanup", status = "incomplete" }); }
        }
        await ExportService.WriteTextAsync(fullReport, JsonSerializer.Serialize(new
        {
            timestampUtc = DateTimeOffset.UtcNow,
            status = failed ? "failed" : "passed",
            validation = "Real WPF construction, data binding and off-screen rendering. Not a manual visual acceptance review.",
            clipboardAccessed = false, inputSynthesized = false, realWorkspaceOpened = false,
            screenshots = Directory.GetFiles(output, "*.png").Select(Path.GetFileName).ToArray(),
            bindingErrors = errors.Messages, checks
        }, new JsonSerializerOptions { WriteIndented = true }));
        return failed ? 1 : 0;
    }

    private static async Task SettleAsync(ShellViewModel shell)
    {
        var watch = Stopwatch.StartNew();
        do
        {
            await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await Task.Delay(120);
            if (watch.Elapsed > TimeSpan.FromSeconds(10))
                throw new TimeoutException("The synthetic workspace did not settle within the UI smoke deadline.");
        } while (shell.IsSearching || shell.PreviewBusy);
    }

    private static async Task WaitCommandAsync(AsyncRelayCommand command)
    {
        var watch = Stopwatch.StartNew();
        do
        {
            await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await Task.Delay(50);
            if (watch.Elapsed > TimeSpan.FromSeconds(10))
                throw new TimeoutException("A UI smoke command did not finish within ten seconds.");
        } while (command.IsRunning);
    }

    private static Task CaptureAsync(MainWindow window, string path)
    {
        window.UpdateLayout();
        var visual = window.Content as FrameworkElement ?? throw new InvalidOperationException("Missing WPF root visual.");
        return CaptureElementAsync(visual, path, minimumWidth: 600, minimumHeight: 400);
    }

    private static async Task CaptureElementAsync(FrameworkElement visual, string path, int minimumWidth = 200, int minimumHeight = 100)
    {
        visual.UpdateLayout();
        var width = (int)Math.Ceiling(visual.ActualWidth);
        var height = (int)Math.Ceiling(visual.ActualHeight);
        if (width < minimumWidth || height < minimumHeight)
            throw new InvalidOperationException("Unexpectedly small or unarranged WPF visual.");
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var buffer = new MemoryStream(); encoder.Save(buffer);
        await ExportService.WriteBytesAsync(path, buffer.ToArray());
    }

    private sealed class BindingErrors : TraceListener
    {
        public List<string> Messages { get; } = [];
        public override void Write(string? message) { if (!string.IsNullOrWhiteSpace(message)) Messages.Add(message); }
        public override void WriteLine(string? message) => Write(message);
    }
}
