using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using PasteDock.Desktop.Interop;
using PasteDock.Desktop.Services;
using PasteDock.Desktop.ViewModels;
using PasteDock.Desktop.Views;

namespace PasteDock.Desktop;

public partial class App : Application
{
    private SingleInstanceGuard? _instance;
    private WorkspaceService? _workspace;
    private ForegroundTracker? _foreground;
    private GlobalHotkeys? _hotkeys;
    private TrayService? _tray;
    private ShellViewModel? _shell;
    private Views.MainWindow? _window;
    private bool _quitting;
    private bool _eventsSubscribed;
    private bool _savingSettings;
    private FileStream? _workspaceLock;
    private bool _handlingFailure;
    private string? _diagnosticReport;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--ui-smoke", StringComparer.OrdinalIgnoreCase) || e.Args.Contains("--diagnostics", StringComparer.OrdinalIgnoreCase))
        {
            var reportIndex = Array.FindIndex(e.Args, a => a.Equals("--report", StringComparison.OrdinalIgnoreCase));
            _diagnosticReport = reportIndex >= 0 && reportIndex + 1 < e.Args.Length ? e.Args[reportIndex + 1] : "PasteDock-diagnostics.json";
        }
        DispatcherUnhandledException += OnUnhandled;
        try
        {
            if (e.Args.Contains("--ui-smoke", StringComparer.OrdinalIgnoreCase))
            {
                var index = Array.FindIndex(e.Args, a => a.Equals("--report", StringComparison.OrdinalIgnoreCase));
                var report = index >= 0 && index + 1 < e.Args.Length ? e.Args[index + 1] : "PasteDock-ui-smoke.json";
                Shutdown(await UiSmokeDiagnostics.RunAsync(report)); return;
            }
            if (e.Args.Contains("--diagnostics", StringComparer.OrdinalIgnoreCase))
            {
                var index = Array.FindIndex(e.Args, a => a.Equals("--report", StringComparison.OrdinalIgnoreCase));
                var report = index >= 0 && index + 1 < e.Args.Length ? e.Args[index + 1] : "PasteDock-diagnostics.json";
                Shutdown(await NativeDiagnostics.RunAsync(report)); return;
            }
            _instance = new SingleInstanceGuard();
            if (!_instance.IsPrimary) { _instance.Dispose(); _instance = null; Shutdown(); return; }
            _foreground = new ForegroundTracker();
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PasteDock");
            Directory.CreateDirectory(root);
            // A Local\ mutex signals this session; the exclusive file lease also covers other sessions.
            _workspaceLock = new FileStream(Path.Combine(root, "workspace.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None, 1, FileOptions.None);
            _workspace = new WorkspaceService(root);
            await _workspace.InitializeAsync();
            ThemeService.Apply(_workspace.Settings.Theme);
            _shell = new ShellViewModel(_workspace, Dispatcher) { ApplySettings = ApplySettingsAsync };
            _window = new Views.MainWindow(_shell);
            MainWindow = _window;
            _window.ApplySettings(_workspace.Settings, restoreSize: true);
            var hwnd = new WindowInteropHelper(_window).EnsureHandle();
            var source = HwndSource.FromHwnd(hwnd) ?? throw new InvalidOperationException("The desktop window could not be created.");
            _hotkeys = new GlobalHotkeys(source, PasteNext);
            var registered = _hotkeys.TrySetMain(_workspace.Settings.HotkeyModifiers, _workspace.Settings.HotkeyKey, TogglePanel);
            _tray = new TrayService(ShowPanel, _workspace.TogglePause, PasteNext, RequestQuit);
            _workspace.CaptureStateChanged += UpdateTray;
            _shell.ShowRequested += ShowPanel;
            _shell.QuitRequested += RequestQuit;
            _instance.RegisterShow(() => Dispatcher.BeginInvoke(new Action(ShowPanel)));
            SystemEvents.SessionSwitch += OnSessionSwitch;
            _eventsSubscribed = true;
            UpdateTray();
            if (!e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase) || !registered) ShowPanel();
            if (!registered) _shell.Notify("The open shortcut is already in use. Open Settings to choose another; the tray remains available.");
            else if (!_hotkeys.QueueRegistered) _shell.Notify("Ctrl+Alt+N is in use by another app. The queue button is still available in PasteDock.");
        }
        catch (Exception exception)
        {
            if (_diagnosticReport is not null) { WriteDiagnosticFailure(exception); Shutdown(1); return; }
            _workspace?.Log.Write("STARTUP_FAILED", exception);
            MessageBox.Show("PasteDock could not open its local workspace. Existing data has not been replaced. " +
                "Check free disk space and permissions. Encrypted data requires the original Windows user profile. " +
                "See %LOCALAPPDATA%\\PasteDock for the content-free diagnostic log.",
                "PasteDock could not start", MessageBoxButton.OK, MessageBoxImage.Error);
            await ExitAsync(1, force: true);
        }
    }

    private void ShowPanel()
    {
        if (_quitting || _window is null || _shell is null || _workspace?.Clipboard.IsLocked == true || !NativeMethods.IsInteractiveDesktop()) return;
        _foreground?.RememberCurrent();
        _shell.SetTarget(_foreground?.Last);
        _window.Reveal();
    }
    private void TogglePanel()
    {
        if (_window is { IsVisible: true, IsActive: true }) _window.HidePanel();
        else ShowPanel();
    }
    private void PasteNext()
    {
        if (_quitting || _shell is null || _workspace?.Clipboard.IsLocked == true || !NativeMethods.IsInteractiveDesktop()) return;
        _foreground?.RememberCurrent();
        _shell.SetTarget(_foreground?.Last);
        if (_shell.QueueNextCommand.CanExecute(null)) _shell.QueueNextCommand.Execute(null);
        else { ShowPanel(); _shell.Notify(_shell.HasQueue ? "Finish or cancel the active edit before pasting from the queue." : "The queue is empty. Select clips and press Ctrl+Q."); }
    }
    private void UpdateTray()
    {
        if (_quitting) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_workspace is not null && !_quitting) _tray?.SetState(_workspace.Clipboard.IsPaused, _workspace.Clipboard.IsLocked);
        }));
    }

    private async Task ApplySettingsAsync(AppSettings next)
    {
        if (_workspace is null || _hotkeys is null || _window is null || _savingSettings) return;
        _savingSettings = true;
        var previous = _workspace.Settings;
        var normalized = _window.WithCurrentGeometry(next.Normalize());
        var startupChanged = normalized.StartWithWindows != previous.StartWithWindows;
        var startupWritten = false;
        try
        {
            if (!_hotkeys.TrySetMain(normalized.HotkeyModifiers, normalized.HotkeyKey, TogglePanel))
                throw new InvalidOperationException("This shortcut is already registered by another application. The previous shortcut is unchanged.");
            if (startupChanged) { StartupService.SetEnabled(normalized.StartWithWindows); startupWritten = true; }
            try { await _workspace.SaveSettingsAsync(normalized); }
            catch
            {
                if (startupWritten) StartupService.SetEnabled(previous.StartWithWindows);
                throw;
            }
            ThemeService.Apply(normalized.Theme);
            _window.ApplySettings(normalized);
        }
        catch
        {
            if (!_hotkeys.TrySetMain(previous.HotkeyModifiers, previous.HotkeyKey, TogglePanel))
                _shell?.Notify("The previous shortcut could not be restored. Open PasteDock from the tray and choose another shortcut.");
            throw;
        }
        finally { _savingSettings = false; }
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        var locked = e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff or SessionSwitchReason.RemoteDisconnect;
        var unlocked = e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon or SessionSwitchReason.RemoteConnect;
        if (!locked && !unlocked) return;
        // Block clipboard writes immediately, before the UI dispatcher gets a turn.
        _workspace?.SetSessionLocked(locked);
        if (locked) Dispatcher.BeginInvoke(new Action(() => _shell?.OnSessionLock()));
    }
    private async void RequestQuit() => await ExitAsync(0, force: false);
    private async Task ExitAsync(int exitCode, bool force)
    {
        if (_quitting) return;
        if (!force && _shell is { IsEditing: true })
        { ShowPanel(); _shell.Notify("Save or cancel the inline edit before quitting. Hiding the window keeps the draft."); return; }
        if (!force && (_shell?.HasRunningActions == true || _savingSettings))
        { ShowPanel(); _shell?.Notify("An operation is still active. Let it finish before quitting."); return; }
        _quitting = true;
        if (_eventsSubscribed)
        { SystemEvents.SessionSwitch -= OnSessionSwitch; _eventsSubscribed = false; }
        _hotkeys?.Dispose(); _foreground?.Dispose(); _tray?.Dispose();
        _hotkeys = null; _foreground = null; _tray = null;
        if (_workspace is not null)
        {
            _workspace.CaptureStateChanged -= UpdateTray;
            if (!force && _window is not null)
            {
                try { await _workspace.SaveSettingsAsync(_window.WithCurrentGeometry(_workspace.Settings)); }
                catch (Exception exception) { _workspace.Log.Write("GEOMETRY_SAVE_FAILED", exception); }
            }
            _shell?.Dispose();
            try { await _workspace.DisposeAsync(); }
            catch (Exception exception) { _workspace.Log.Write("SHUTDOWN_FAILED", exception); }
        }
        _window?.CloseForExit();
        _workspaceLock?.Dispose(); _workspaceLock = null;
        // The named mutex is owned by this dispatcher thread and must be released here.
        _instance?.Dispose(); _instance = null;
        Shutdown(exitCode);
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        if (_handlingFailure) { Shutdown(1); return; }
        _handlingFailure = true;
        if (_diagnosticReport is not null)
        {
            WriteDiagnosticFailure(e.Exception); Shutdown(1);
            return;
        }
        _workspace?.Log.Write("UNHANDLED_UI_ERROR", e.Exception);
        _workspace?.Clipboard.SetPaused(true);
        MessageBox.Show("PasteDock encountered an unexpected error and has paused capture. Restart the application before continuing. " +
            "Clipboard contents were not written to the diagnostic log.", "PasteDock", MessageBoxButton.OK, MessageBoxImage.Error);
        _ = ExitAsync(1, force: true);
    }

    private void WriteDiagnosticFailure(Exception exception)
    {
        try
        {
            var path = Path.GetFullPath(_diagnosticReport!);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new { status = "failed", error = exception.ToString() }));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
