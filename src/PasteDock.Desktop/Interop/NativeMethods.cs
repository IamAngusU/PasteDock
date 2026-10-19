using System.Runtime.InteropServices;

namespace PasteDock.Desktop.Interop;

internal static class NativeMethods
{
    internal const int WmClipboardUpdate = 0x031D;
    internal const int WmHotkey = 0x0312;
    internal const uint ModNoRepeat = 0x4000;
    internal const uint EventSystemForeground = 0x0003;
    internal const uint WineventOutOfContext = 0;
    internal const uint WineventSkipOwnProcess = 2;
    internal const uint ProcessQueryLimitedInformation = 0x1000;
    internal const uint KeyeventfKeyup = 2;
    internal const uint InputKeyboard = 1;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] internal static extern IntPtr GetClipboardOwner();
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int virtualKey);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder className, int maxCount);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(uint count, [In] Input[] inputs, int size);
    [DllImport("user32.dll")]
    internal static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module,
        WinEventDelegate callback, uint processId, uint threadId, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWinEvent(IntPtr hook);

    internal delegate void WinEventDelegate(IntPtr hook, uint eventType, IntPtr hwnd,
        int objectId, int childId, uint eventThread, uint eventTime);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public HardwareInput Hardware;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardInput { public ushort VirtualKey; public ushort Scan; public uint Flags; public uint Time; public UIntPtr ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseInput { public int X; public int Y; public uint MouseData; public uint Flags; public uint Time; public UIntPtr ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct HardwareInput { public uint Message; public ushort ParamLow; public ushort ParamHigh; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint flags, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint desiredAccess);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll", EntryPoint = "GetUserObjectInformationW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDesktopName(IntPtr desktop, int index, System.Text.StringBuilder buffer, uint bytes, out uint required);
    [DllImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QuerySessionState(IntPtr server, int sessionId, int infoClass, out IntPtr buffer, out uint bytes);
    [DllImport("wtsapi32.dll")] private static extern void WTSFreeMemory(IntPtr memory);

    internal static bool IsInteractiveDesktop()
    {
        // WTS_CURRENT_SESSION, WTSConnectState: only WTSActive (0) is accepted.
        if (!QuerySessionState(IntPtr.Zero, -1, 8, out var state, out var bytes)) return false;
        try { if (bytes < 4 || Marshal.ReadInt32(state) != 0) return false; }
        finally { WTSFreeMemory(state); }
        var desktop = OpenInputDesktop(0, false, 0x0001); // DESKTOP_READOBJECTS
        if (desktop == IntPtr.Zero) return false;
        try
        {
            var name = new System.Text.StringBuilder(256);
            return GetDesktopName(desktop, 2, name, (uint)(name.Capacity * 2), out _)
                && name.ToString().Equals("Default", StringComparison.OrdinalIgnoreCase);
        }
        finally { CloseDesktop(desktop); }
    }

    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);

    internal static double MonitorScale(int x, int y)
    {
        // A tiny invisible top-level window asks Windows for the target monitor's effective DPI.
        // GetDpiForMonitor is deliberately avoided for a PerMonitorV2 caller.
        var parameters = new System.Windows.Interop.HwndSourceParameters("PasteDock.DpiProbe")
        { PositionX = x, PositionY = y, Width = 1, Height = 1, WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x08000080 };
        using var probe = new System.Windows.Interop.HwndSource(parameters);
        var dpi = GetDpiForWindow(probe.Handle);
        return dpi == 0 ? 1d : dpi / 96d;
    }

    internal static Input Key(ushort virtualKey, bool keyUp = false) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = virtualKey, Flags = keyUp ? KeyeventfKeyup : 0 } }
    };
}
