using System.Text;
using System.Security.Cryptography;
using PasteDock.Core.Storage;

namespace PasteDock.Core.Services;

/// <summary>Commit an export only after the complete file has been flushed in the destination directory.</summary>
public static class ExportService
{
    public static Task WriteBytesAsync(string path, byte[] bytes, CancellationToken ct = default) =>
        AtomicFile.WriteAsync(path, bytes, ct);

    public static async Task WriteTextAsync(string path, string text, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        var bytes = new UTF8Encoding(false, true).GetBytes(text);
        try { await AtomicFile.WriteAsync(path, bytes, ct).ConfigureAwait(false); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
