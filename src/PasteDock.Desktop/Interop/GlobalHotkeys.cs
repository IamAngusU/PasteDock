using System.Windows.Interop;

namespace PasteDock.Desktop.Interop;

internal sealed class GlobalHotkeys : IDisposable
{
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = [];
    private int _mainId = -1;
    private int _nextId = 100;
    private (uint Modifiers, uint Key)? _mainChord;
    private const int QueueId = 90;
    public bool QueueRegistered { get; private set; }

    public GlobalHotkeys(HwndSource source, Action nextInQueue)
    {
        _source = source;
        _source.AddHook(Hook);
        QueueRegistered = NativeMethods.RegisterHotKey(_source.Handle, QueueId, 3 | NativeMethods.ModNoRepeat, 0x4E);
        if (QueueRegistered) _actions[QueueId] = nextInQueue;
    }

    public bool TrySetMain(uint modifiers, uint key, Action show)
    {
        if (_mainChord == (modifiers, key)) return true;
        if ((modifiers & 0xF) == 0 || (modifiers & ~0xF) != 0 || key is < 0x20 or > 0xFE) return false;
        var candidate = ++_nextId;
        if (!NativeMethods.RegisterHotKey(_source.Handle, candidate, modifiers | NativeMethods.ModNoRepeat, key)) return false;
        if (_mainId >= 0)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, _mainId);
            _actions.Remove(_mainId);
        }
        _mainId = candidate;
        _mainChord = (modifiers, key);
        _actions[candidate] = show;
        return true;
    }

    private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == NativeMethods.WmHotkey && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            action();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _actions.Keys) NativeMethods.UnregisterHotKey(_source.Handle, id);
        _actions.Clear();
        _source.RemoveHook(Hook);
    }
}
