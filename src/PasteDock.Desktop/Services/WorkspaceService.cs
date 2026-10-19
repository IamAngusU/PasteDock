using System.Threading.Channels;
using PasteDock.Core.Storage;
using PasteDock.Desktop.Clipboard;
using PasteDock.Desktop.Interop;

namespace PasteDock.Desktop.Services;

internal sealed class WorkspaceService : IAsyncDisposable
{
    private readonly Channel<CapturedClip> _captures = Channel.CreateBounded<CapturedClip>(new BoundedChannelOptions(4)
    { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly CancellationTokenSource _stop = new();
    private Task? _consumer;
    private Task? _maintenance;
    private AppSettings _settings = new();
    public AppSettings Settings => Volatile.Read(ref _settings);
    public EncryptedFileStore Store { get; }
    public HistoryRepository History { get; }
    public WindowsClipboardService Clipboard { get; } = new();
    public PasteService Paste { get; }
    public TemporaryPasteFileService PasteFiles { get; }
    public WindowsOcrService Ocr { get; } = new();
    public SafeLog Log { get; }
    public event Action<string>? Notice;
    public event Action? CaptureStateChanged;

    public WorkspaceService(string root)
    {
        Store = new EncryptedFileStore(root, new DpapiProtector());
        History = new HistoryRepository(Store);
        Paste = new PasteService(Clipboard);
        PasteFiles = new TemporaryPasteFileService(root);
        Log = new SafeLog(root);
        Clipboard.Captured += capture =>
        {
            if (!_captures.Writer.TryWrite(capture)) Notice?.Invoke("Capture queue is full. A copy was skipped rather than blocking your desktop.");
        };
        Clipboard.Notice += message => Notice?.Invoke(message);
    }

    public async Task InitializeAsync()
    {
        _settings = await Task.Run(() => Store.LoadSettingsAsync()).ConfigureAwait(false);
        await Task.Run(PasteFiles.CleanupBestEffort).ConfigureAwait(false);
        await Task.Run(() => History.InitializeAsync()).ConfigureAwait(false);
        await History.PruneAsync(Settings).ConfigureAwait(false);
        _consumer = Task.Run(ConsumeAsync);
        _maintenance = Task.Run(MaintainAsync);
        await Clipboard.StartAsync(Settings).ConfigureAwait(false);
    }

    public void TogglePause()
    {
        Clipboard.SetPaused(!Clipboard.IsPaused);
        CaptureStateChanged?.Invoke();
    }

    public void SetSessionLocked(bool locked)
    {
        Clipboard.SetLocked(locked);
        CaptureStateChanged?.Invoke();
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        var normalized = settings.Normalize();
        await Task.Run(() => Store.SaveSettingsAsync(normalized)).ConfigureAwait(false);
        Volatile.Write(ref _settings, normalized);
        Clipboard.UpdateSettings(normalized);
        try { await History.PruneAsync(normalized).ConfigureAwait(false); }
        catch (Exception exception) { Log.Write("RETENTION_FAILED", exception); Notice?.Invoke("Settings were saved, but history cleanup could not finish."); }
    }

    private async Task ConsumeAsync()
    {
        try
        {
            await foreach (var capture in _captures.Reader.ReadAllAsync(_stop.Token).ConfigureAwait(false))
            {
                if (capture.Epoch != Clipboard.Epoch || !PasteDock.Desktop.Interop.NativeMethods.IsInteractiveDesktop()) continue;
                var decision = PrivacyPolicy.Evaluate(capture.Payload, capture.SourceApp, Settings, Clipboard.IsPaused, Clipboard.IsLocked);
                if (!decision.Allowed) continue;
                try { await History.CaptureAsync(capture.Payload, capture.SourceApp, Settings, _stop.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
                catch (InvalidOperationException exception) { Notice?.Invoke(exception.Message); }
                catch (Exception exception) { Log.Write("CAPTURE_STORE_FAILED", exception); Notice?.Invoke("An item could not be saved. Check local storage permissions and free space."); }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    private async Task MaintainAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
            {
                try { await History.PruneAsync(Settings, _stop.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
                catch (Exception exception) { Log.Write("MAINTENANCE_FAILED", exception); }
                PasteFiles.CleanupBestEffort();
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync()
    {
        Clipboard.SetPaused(true);
        _captures.Writer.TryComplete();
        _stop.Cancel();
        try { await Clipboard.DisposeAsync().ConfigureAwait(false); }
        catch (TimeoutException) { Log.Write("CLIPBOARD_SHUTDOWN_TIMEOUT"); }
        var tasks = new[] { _consumer, _maintenance }.Where(t => t is not null).Cast<Task>().ToArray();
        if (tasks.Length > 0) await Task.WhenAll(tasks).ConfigureAwait(false);
        _stop.Dispose();
    }
}
