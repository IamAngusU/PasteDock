using System.Diagnostics;
using System.Text;

namespace PasteDock.Desktop.Interop;

internal sealed record PasteTarget(IntPtr Handle, uint ProcessId, long ProcessStartedAt, string AppName);

internal sealed class ForegroundTracker : IDisposable
{
    private readonly NativeMethods.WinEventDelegate _callback;
    private readonly IntPtr _hook;
    private PasteTarget? _last;
    public PasteTarget? Last => Volatile.Read(ref _last);

    public ForegroundTracker()
    {
        _callback = (_, _, hwnd, _, _, _, _) => Remember(hwnd);
        _hook = NativeMethods.SetWinEventHook(NativeMethods.EventSystemForeground, NativeMethods.EventSystemForeground,
            IntPtr.Zero, _callback, 0, 0, NativeMethods.WineventOutOfContext | NativeMethods.WineventSkipOwnProcess);
        Remember(NativeMethods.GetForegroundWindow());
    }

    public void RememberCurrent() => Remember(NativeMethods.GetForegroundWindow());

    public static string AppNameForWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return "Unknown";
        NativeMethods.GetWindowThreadProcessId(hwnd, out var id);
        try { using var process = Process.GetProcessById(checked((int)id)); return process.ProcessName; }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or OverflowException)
        { return "Unknown"; }
    }

    public static bool IsSameTarget(PasteTarget target)
    {
        if (!NativeMethods.IsWindow(target.Handle)) return false;
        NativeMethods.GetWindowThreadProcessId(target.Handle, out var pid);
        if (pid != target.ProcessId || pid == Environment.ProcessId) return false;
        try
        {
            using var process = Process.GetProcessById(checked((int)pid));
            return target.ProcessStartedAt == process.StartTime.ToUniversalTime().Ticks;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or OverflowException)
        { return false; }
    }

    private void Remember(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindowVisible(hwnd)) return;
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == Environment.ProcessId || pid == 0) return;
        var className = new StringBuilder(256);
        NativeMethods.GetClassName(hwnd, className, className.Capacity);
        if (className.ToString() is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "NotifyIconOverflowWindow" or "Progman" or "WorkerW") return;
        try
        {
            using var process = Process.GetProcessById(checked((int)pid));
            Volatile.Write(ref _last, new PasteTarget(hwnd, pid, process.StartTime.ToUniversalTime().Ticks, process.ProcessName));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or OverflowException)
        { }
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) NativeMethods.UnhookWinEvent(_hook);
        GC.KeepAlive(_callback);
    }
}
