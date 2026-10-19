using System.Security.Principal;

namespace PasteDock.Desktop.Services;

internal sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showEvent;
    private RegisteredWaitHandle? _registration;
    public bool IsPrimary { get; }

    public SingleInstanceGuard()
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("The Windows user could not be resolved.");
        _mutex = new Mutex(false, @"Local\PasteDock.Mutex." + sid);
        try { IsPrimary = _mutex.WaitOne(0); }
        catch (AbandonedMutexException) { IsPrimary = true; }
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\PasteDock.Show." + sid);
        if (!IsPrimary) _showEvent.Set();
    }

    public void RegisterShow(Action show)
    {
        _registration = ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) => show(), null, Timeout.Infinite, false);
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        _showEvent.Dispose();
        if (IsPrimary) _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
