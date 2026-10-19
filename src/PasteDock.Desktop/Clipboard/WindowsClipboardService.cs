using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PasteDock.Desktop.Interop;
using WpfClipboard = System.Windows.Clipboard;

namespace PasteDock.Desktop.Clipboard;

internal sealed record CapturedClip(ClipPayload Payload, string SourceApp, int Epoch);

/// <summary>All clipboard/OLE access lives on its own STA dispatcher, never on the UI thread.</summary>
internal sealed class WindowsClipboardService : IAsyncDisposable, IPasteClipboard
{
    private const string SessionFormat = "PasteDock.Session.v1";
    private const string PngFormat = "PNG";
    private const string ExcludeFormat = "ExcludeClipboardContentFromMonitorProcessing";
    private const string HistoryFormat = "CanIncludeInClipboardHistory";
    private const string CloudFormat = "CanUploadToCloudClipboard";
    private readonly byte[] _nonce = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim _access = new(1, 1);
    private Dispatcher? _dispatcher;
    private HwndSource? _window;
    private Thread? _thread;
    private volatile AppSettings _settings = new();
    private volatile bool _paused;
    private volatile bool _locked;
    private volatile bool _disposing;
    private int _epoch;
    private bool _reading;
    private bool _pending;
    private uint _lastSequence;
    public event Action<CapturedClip>? Captured;
    public event Action<string>? Notice;
    public bool IsPaused => _paused;
    public bool IsLocked => _locked;
    public int Epoch => Volatile.Read(ref _epoch);

    public Task StartAsync(AppSettings settings)
    {
        _settings = settings;
        _thread = new Thread(Run) { IsBackground = true, Name = "PasteDock.Clipboard.STA" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        return _started.Task;
    }

    public void UpdateSettings(AppSettings settings) { _settings = settings; Interlocked.Increment(ref _epoch); }
    public void SetPaused(bool paused) { _paused = paused; Interlocked.Increment(ref _epoch); }
    public void SetLocked(bool locked) { _locked = locked; Interlocked.Increment(ref _epoch); }

    private void Run()
    {
        try
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(_dispatcher));
            var parameters = new HwndSourceParameters("PasteDock.Clipboard")
            { ParentWindow = new IntPtr(-3), Width = 0, Height = 0, WindowStyle = 0 };
            _window = new HwndSource(parameters);
            _window.AddHook(Hook);
            if (!NativeMethods.AddClipboardFormatListener(_window.Handle))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            _lastSequence = NativeMethods.GetClipboardSequenceNumber();
            _started.TrySetResult();
            Dispatcher.Run();
        }
        catch (Exception exception) { _started.TrySetException(exception); Notice?.Invoke("Clipboard monitoring stopped."); }
        finally
        {
            if (_window is not null)
            {
                NativeMethods.RemoveClipboardFormatListener(_window.Handle);
                _window.RemoveHook(Hook);
                _window.Dispose();
            }
            _stopped.TrySetResult();
        }
    }

    private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == NativeMethods.WmClipboardUpdate)
        {
            handled = true;
            _pending = true;
            if (!_reading && !_disposing) _ = ReadPendingAsync();
        }
        return IntPtr.Zero;
    }

    private async Task ReadPendingAsync()
    {
        _reading = true;
        try
        {
            while (_pending && !_disposing)
            {
                _pending = false;
                if (_paused || _locked || !NativeMethods.IsInteractiveDesktop()) continue;
                var epoch = Epoch;
                for (var attempt = 0; attempt < 6 && !_disposing; attempt++)
                {
                    var shouldRetry = false;
                    await _access.WaitAsync();
                    try
                    {
                        var sequence = NativeMethods.GetClipboardSequenceNumber();
                        if (sequence == _lastSequence) break;
                        var data = WpfClipboard.GetDataObject();
                        if (data is null) break;
                        if (IsSensitive(data) || IsOwnWrite(data)) { _lastSequence = sequence; break; }
                        var ownerApp = ForegroundTracker.AppNameForWindow(NativeMethods.GetClipboardOwner());
                        var foregroundApp = ForegroundTracker.AppNameForWindow(NativeMethods.GetForegroundWindow());
                        var settings = _settings;
                        // Exclude either source identity; some clipboard brokers own the window for another app.
                        if (settings.ExcludedApps.Contains(ownerApp, StringComparer.OrdinalIgnoreCase)
                            || settings.ExcludedApps.Contains(foregroundApp, StringComparer.OrdinalIgnoreCase))
                        { _lastSequence = sequence; break; }
                        var payload = ReadPayload(data, settings);
                        if (sequence != NativeMethods.GetClipboardSequenceNumber()) { _pending = true; break; }
                        _lastSequence = sequence;
                        if (payload is null || epoch != Epoch || _paused || _locked || !NativeMethods.IsInteractiveDesktop()) break;
                        var source = ownerApp == "Unknown" ? foregroundApp : ownerApp;
                        var decision = PrivacyPolicy.Evaluate(payload, source, settings);
                        if (decision.Allowed) Captured?.Invoke(new CapturedClip(payload, source, epoch));
                        else Notice?.Invoke(decision.Reason);
                        break;
                    }
                    catch (ExternalException) { shouldRetry = true; }
                    catch (Exception exception) when (exception is InvalidDataException or NotSupportedException or ArgumentException or IOException)
                    { Notice?.Invoke("A clipboard item could not be read safely. It was not saved."); break; }
                    finally { _access.Release(); }
                    if (shouldRetry)
                    {
                        if (attempt == 5) Notice?.Invoke("The clipboard stayed busy. This copy could not be captured.");
                        else await Task.Delay(20 << attempt);
                    }
                }
            }
        }
        catch (Exception) { Notice?.Invoke("A clipboard provider failed. Monitoring continues with the next copy."); }
        finally { _reading = false; }
    }

    private bool IsOwnWrite(IDataObject data)
    {
        if (!data.GetDataPresent(SessionFormat, false)) return false;
        var marker = data.GetData(SessionFormat, false) switch
        {
            byte[] bytes when bytes.Length == 16 => bytes,
            Stream stream => ReadMarker(stream),
            _ => null
        };
        return marker is not null && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(marker, _nonce);
    }

    private static byte[]? ReadMarker(Stream stream)
    {
        if (stream.CanSeek) { if (stream.Length != 16) return null; stream.Position = 0; }
        var marker = new byte[16];
        var offset = 0;
        while (offset < marker.Length)
        {
            var count = stream.Read(marker, offset, marker.Length - offset);
            if (count == 0) return null;
            offset += count;
        }
        return marker;
    }

    private static bool IsSensitive(IDataObject data)
    {
        if (data.GetDataPresent(ExcludeFormat, false) || data.GetDataPresent("Clipboard Viewer Ignore", false)) return true;
        if (!data.GetDataPresent(HistoryFormat, false)) return false;
        var value = data.GetData(HistoryFormat, false);
        return value switch
        {
            int number => number == 0,
            uint number => number == 0,
            byte[] bytes => bytes.Length < 4 || BitConverter.ToUInt32(bytes, 0) == 0,
            Stream stream => ReadDwordZero(stream),
            _ => true // Unknown privacy-flag representation: fail closed.
        };
    }

    private static bool ReadDwordZero(Stream stream)
    {
        Span<byte> bytes = stackalloc byte[4];
        if (stream.CanSeek) stream.Position = 0;
        var read = 0;
        while (read < 4) { var count = stream.Read(bytes[read..]); if (count == 0) return true; read += count; }
        return System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes) == 0;
    }

    private static ClipPayload? ReadPayload(IDataObject data, AppSettings settings)
    {
        var limit = settings.MaxItemMb * 1024L * 1024;
        string? Text(string format)
        {
            if (!data.GetDataPresent(format, false)) return null;
            if (data.GetData(format, false) is not string text) return null;
            if (text.Length > limit || Encoding.UTF8.GetByteCount(text) > limit)
                throw new InvalidDataException("Oversized text.");
            return text;
        }
        var payload = new ClipPayload
        {
            Text = Text(DataFormats.UnicodeText) ?? Text(DataFormats.Text),
            Html = Text(DataFormats.Html), Rtf = Text(DataFormats.Rtf)
        };
        if (settings.CaptureFiles && data.GetDataPresent(DataFormats.FileDrop, false))
        {
            if (data.GetData(DataFormats.FileDrop, false) is string[] files)
                payload = payload with { Files = files.ToArray() };
        }
        if (settings.CaptureImages)
        {
            byte[]? png = null;
            if (data.GetDataPresent(PngFormat, false))
            {
                png = data.GetData(PngFormat, false) switch
                {
                    byte[] bytes when bytes.LongLength <= limit => bytes.ToArray(),
                    Stream stream => ReadStream(stream, limit),
                    _ => null
                };
            }
            if (png is not null)
            {
                var (width, height) = PngHeader.ReadDimensions(png);
                payload = payload with { Png = png, ImageWidth = width, ImageHeight = height };
            }
            else if (data.GetDataPresent(DataFormats.Bitmap, true)
                && data.GetData(DataFormats.Bitmap, true) is BitmapSource bitmap)
            {
                if ((long)bitmap.PixelWidth * bitmap.PixelHeight > Limits.MaxImagePixels)
                    throw new InvalidDataException("Oversized image.");
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = new MemoryStream();
                encoder.Save(output);
                if (output.Length > limit) throw new InvalidDataException("Oversized image.");
                payload = payload with { Png = output.ToArray(), ImageWidth = bitmap.PixelWidth, ImageHeight = bitmap.PixelHeight };
            }
        }
        if (payload.Text is null && payload.Html is null && payload.Rtf is null && payload.Png is null && payload.Files.Length == 0) return null;
        payload.Validate(limit);
        return payload;
    }

    private static byte[] ReadStream(Stream stream, long limit)
    {
        if (stream.CanSeek)
        {
            if (stream.Length > limit) throw new InvalidDataException("Oversized clipboard stream.");
            stream.Position = 0;
        }
        using var output = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int count;
        while ((count = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (output.Length + count > limit) throw new InvalidDataException("Oversized clipboard stream.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    public async Task<uint> WriteAsync(ClipPayload payload, bool preventCloud, CancellationToken ct = default)
    {
        if (_dispatcher is null || _disposing) throw new InvalidOperationException("The clipboard service is not available.");
        if (_locked || !NativeMethods.IsInteractiveDesktop()) throw new InvalidOperationException("Clipboard writes require an active, unlocked Windows desktop.");
        payload.Validate();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        if (payload.Files.Length > 0)
            await Task.Run(() => FileReferencePolicy.EnsureAvailable(payload.Files, cancellationToken: timeout.Token), timeout.Token)
                .WaitAsync(timeout.Token).ConfigureAwait(false);
        var operation = _dispatcher.InvokeAsync(() => WriteOnStaAsync(payload, preventCloud, timeout.Token),
            DispatcherPriority.Normal, timeout.Token);
        return await (await operation.Task.ConfigureAwait(false)).ConfigureAwait(false);
    }

    private async Task<uint> WriteOnStaAsync(ClipPayload payload, bool preventCloud, CancellationToken ct)
    {
        await _access.WaitAsync(ct);
        try
        {
            var data = new DataObject();
            if (payload.Text is not null) data.SetData(DataFormats.UnicodeText, payload.Text, false);
            if (payload.Html is not null) data.SetData(DataFormats.Html, payload.Html, false);
            if (payload.Rtf is not null) data.SetData(DataFormats.Rtf, payload.Rtf, false);
            if (payload.Files.Length > 0) data.SetData(DataFormats.FileDrop, payload.Files.ToArray(), false);
            using var pngStream = payload.Png is null ? null : new MemoryStream(payload.Png, writable: false);
            if (pngStream is not null)
            {
                data.SetData(PngFormat, pngStream, false);
                using var bitmapStream = new MemoryStream(payload.Png!, writable: false);
                var bitmap = BitmapFrame.Create(bitmapStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                bitmap.Freeze();
                data.SetImage(bitmap);
            }
            using var marker = new MemoryStream(_nonce, writable: false);
            data.SetData(SessionFormat, marker, false);
            using var history = new MemoryStream(new byte[4], writable: false);
            using var cloud = new MemoryStream(new byte[4], writable: false);
            using var exclude = new MemoryStream(new byte[1], writable: false);
            if (preventCloud)
            {
                data.SetData(HistoryFormat, history, false);
                data.SetData(CloudFormat, cloud, false);
                data.SetData(ExcludeFormat, exclude, false);
            }
            for (var attempt = 0; ; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                if (_locked || _disposing || !NativeMethods.IsInteractiveDesktop()) throw new InvalidOperationException("Clipboard writes are blocked while the session is locked or PasteDock is closing.");
                try
                {
                    // copy:true flushes formats so the clipboard survives process exit and stream disposal.
                    WpfClipboard.SetDataObject(data, true);
                    break;
                }
                catch (ExternalException) when (attempt < 5) { await Task.Delay(20 << attempt, ct); }
            }
            ct.ThrowIfCancellationRequested();
            var sequence = NativeMethods.GetClipboardSequenceNumber();
            try
            {
                var current = WpfClipboard.GetDataObject();
                if (current is null || !IsOwnWrite(current) || NativeMethods.GetClipboardSequenceNumber() != sequence)
                    throw new InvalidOperationException("Another application replaced the clipboard. Automatic paste was cancelled.");
            }
            catch (ExternalException)
            {
                // Do not retry a completed write: a later copy from another app must win.
                throw new InvalidOperationException("The copy was written, but the clipboard could not be verified. Check it before pasting manually.");
            }
            // The watcher checks the marker itself. Never mark a potentially foreign sequence as our write.
            return sequence;
        }
        finally { _access.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        _disposing = true;
        if (_dispatcher is { HasShutdownStarted: false })
            _dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
        if (_thread is not null) await _stopped.Task.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
    }
}
