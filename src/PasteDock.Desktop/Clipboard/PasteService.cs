using System.Runtime.InteropServices;
using PasteDock.Desktop.Interop;

namespace PasteDock.Desktop.Clipboard;

internal sealed record PasteOutcome(bool InputSent, bool ClipboardWritten, string Message);

internal interface IPasteClipboard
{
    bool IsLocked { get; }
    Task<uint> WriteAsync(ClipPayload payload, bool preventCloud, CancellationToken ct = default);
}

internal interface IPasteDesktop
{
    bool IsSameTarget(PasteTarget target);
    bool SetForegroundWindow(IntPtr handle);
    IntPtr GetForegroundWindow();
    bool KeysHeld();
    bool IsInteractiveDesktop();
    uint GetClipboardSequenceNumber();
    uint SendPaste();
    void ReleaseKeys();
}

internal sealed class PasteService
{
    private readonly IPasteClipboard _clipboard;
    private readonly IPasteDesktop _desktop;
    private readonly SemaphoreSlim _gate = new(1, 1);
    public PasteService(WindowsClipboardService clipboard) : this(clipboard, new NativePasteDesktop()) { }
    internal PasteService(IPasteClipboard clipboard, IPasteDesktop desktop) { _clipboard = clipboard; _desktop = desktop; }

    public async Task<PasteOutcome> PasteAsync(ClipPayload payload, PasteTarget? target, bool preventCloud,
        Action hideWindow, CancellationToken ct = default)
    {
        if (!await _gate.WaitAsync(0, ct)) return new(false, false, "A paste is already in progress.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var callerToken = ct;
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        ct = deadline.Token;
        var clipboardWritten = false;
        try
        {
            var began = System.Diagnostics.Stopwatch.GetTimestamp();
            var sequence = await _clipboard.WriteAsync(payload, preventCloud, ct);
            clipboardWritten = true;
            if (target is null || !_desktop.IsSameTarget(target))
                return new(false, true, "Copied. Focus a target application and use Ctrl+V.");
            hideWindow();
            await Task.Delay(75, ct);
            if (!_desktop.SetForegroundWindow(target.Handle) && _desktop.GetForegroundWindow() != target.Handle)
                return new(false, true, "Copied. Windows did not allow switching to the target app; paste manually.");
            // Never synthesize a paste while the user's shortcut modifiers are still held.
            for (var attempt = 0; attempt < 80; attempt++)
            {
                if (!_desktop.KeysHeld()) break;
                await Task.Delay(15, ct);
            }
            if (_desktop.KeysHeld()) return new(false, true, "Copied. Release the shortcut keys and paste manually.");
            if (!_desktop.IsSameTarget(target) || _desktop.GetForegroundWindow() != target.Handle)
                return new(false, true, "Copied. Focus changed before paste; no keystrokes were sent.");
            ct.ThrowIfCancellationRequested();
            if (_clipboard.IsLocked || !_desktop.IsInteractiveDesktop()) return new(false, true, "The desktop is not available. No paste keystrokes were sent.");
            if (System.Diagnostics.Stopwatch.GetElapsedTime(began) > TimeSpan.FromSeconds(3))
                return new(false, true, "Copied. The paste request expired; paste manually.");
            if (sequence != _desktop.GetClipboardSequenceNumber())
                return new(false, true, "Another app changed the clipboard. Paste was cancelled.");
            var sent = _desktop.SendPaste();
            if (sent != 4)
            {
                // A partial injection must not leave our synthetic modifier held down.
                if (sent > 0)
                {
                    _desktop.ReleaseKeys();
                }
                return new(false, true, "Copied. Windows blocked automatic paste, possibly because the target runs as administrator.");
            }
            return new(true, true, "Paste sent.");
        }
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested)
        { return new(false, clipboardWritten, "The paste request expired. Check the clipboard and paste manually."); }
        finally { _gate.Release(); }
    }

}

internal sealed class NativePasteDesktop : IPasteDesktop
{
    public bool IsSameTarget(PasteTarget target) => ForegroundTracker.IsSameTarget(target);
    public bool SetForegroundWindow(IntPtr handle) => NativeMethods.SetForegroundWindow(handle);
    public IntPtr GetForegroundWindow() => NativeMethods.GetForegroundWindow();
    public bool IsInteractiveDesktop() => NativeMethods.IsInteractiveDesktop();
    public uint GetClipboardSequenceNumber() => NativeMethods.GetClipboardSequenceNumber();
    public bool KeysHeld() => new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C, 0x0D, 0x56, 0x4E, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39 }
        .Any(key => (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0);
    public uint SendPaste()
    {
        var inputs = new[] { NativeMethods.Key(0x11), NativeMethods.Key(0x56), NativeMethods.Key(0x56, true), NativeMethods.Key(0x11, true) };
        return NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>());
    }
    public void ReleaseKeys()
    {
        var release = new[] { NativeMethods.Key(0x56, true), NativeMethods.Key(0x11, true) };
        NativeMethods.SendInput((uint)release.Length, release, Marshal.SizeOf<NativeMethods.Input>());
    }
}
