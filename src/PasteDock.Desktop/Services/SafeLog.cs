namespace PasteDock.Desktop.Services;

internal sealed class SafeLog
{
    private readonly string _path;
    private readonly object _gate = new();
    public SafeLog(string root) => _path = Path.Combine(root, "diagnostics.log");

    public void Write(string code, Exception? exception = null)
    {
        // Never record exception.Message, clipboard data, URLs, file paths or window titles.
        lock (_gate)
        {
            try
            {
                if (File.Exists(_path) && new FileInfo(_path).Length > 256 * 1024) File.Delete(_path);
                File.AppendAllText(_path, $"{DateTimeOffset.UtcNow:O} {code} {exception?.GetType().Name ?? ""} {exception?.HResult ?? 0:X8}{Environment.NewLine}");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
