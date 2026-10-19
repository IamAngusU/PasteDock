using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PasteDock.Core.Models;

namespace PasteDock.Core.Services;

public sealed record BackupItem(ClipEntry Entry, ClipPayload Payload);

public static class BackupService
{
    public const long MaxRawBytes = 128L * 1024 * 1024;
    private const int MaxArchiveBytes = 800 * 1024 * 1024;
    private const int Iterations = 600_000;
    private static ReadOnlySpan<byte> Magic => "PDKBAK01"u8;
    private const int HeaderSize = 8 + 16 + 12;

    public static byte[] Encrypt(IReadOnlyList<BackupItem> items, string passphrase)
    {
        if (passphrase.Length < 12) throw new ArgumentException("Use a passphrase of at least 12 characters.");
        if (items.Count > Limits.MaxItems || items.Sum(i => i.Payload.GetByteCount()) > MaxRawBytes)
            throw new InvalidOperationException("This backup exceeds the 128 MiB content limit.");
        var plain = JsonSerializer.SerializeToUtf8Bytes(items);
        if ((long)plain.Length + HeaderSize + 16 > MaxArchiveBytes)
        {
            CryptographicOperations.ZeroMemory(plain);
            throw new InvalidOperationException("Serialized backup metadata exceeds the archive safety limit.");
        }
        var password = Encoding.UTF8.GetBytes(passphrase);
        var result = new byte[HeaderSize + plain.Length + 16];
        Magic.CopyTo(result);
        RandomNumberGenerator.Fill(result.AsSpan(8, 16));
        RandomNumberGenerator.Fill(result.AsSpan(24, 12));
        var key = Rfc2898DeriveBytes.Pbkdf2(password, result.AsSpan(8, 16), Iterations, HashAlgorithmName.SHA256, 32);
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(result.AsSpan(24, 12), plain, result.AsSpan(HeaderSize, plain.Length),
                result.AsSpan(HeaderSize + plain.Length, 16), result.AsSpan(0, HeaderSize));
            return result;
        }
        finally { CryptographicOperations.ZeroMemory(plain); CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(password); }
    }

    public static IReadOnlyList<BackupItem> Decrypt(byte[] archive, string passphrase)
    {
        if (archive.Length < HeaderSize + 16 || archive.Length > MaxArchiveBytes || !archive.AsSpan(0, 8).SequenceEqual(Magic))
            throw new InvalidDataException("Invalid or unsupported backup.");
        var plain = new byte[archive.Length - HeaderSize - 16];
        var password = Encoding.UTF8.GetBytes(passphrase);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, archive.AsSpan(8, 16), Iterations, HashAlgorithmName.SHA256, 32);
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(archive.AsSpan(24, 12), archive.AsSpan(HeaderSize, plain.Length), archive.AsSpan(HeaderSize + plain.Length, 16),
                plain, archive.AsSpan(0, HeaderSize));
            var items = JsonSerializer.Deserialize<BackupItem[]>(plain) ?? throw new InvalidDataException("Empty archive.");
            if (items.Length > Limits.MaxItems || items.Any(i => i is null || i.Entry is null || i.Payload is null
                || i.Entry.Title is null || i.Entry.Tags is null || i.Entry.Tags.Any(t => t is null)
                || i.Entry.SourceApp is null || i.Entry.CopyCount < 1))
                throw new InvalidDataException("Invalid backup entries.");
            foreach (var item in items) item.Payload.Validate();
            if (items.Sum(i => i.Payload.GetByteCount()) > MaxRawBytes) throw new InvalidDataException("The backup is too large.");
            return items;
        }
        finally { CryptographicOperations.ZeroMemory(plain); CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(password); }
    }

    public static async Task<byte[]> ReadFileAsync(string path, CancellationToken ct = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous);
        if (stream.Length < HeaderSize + 16 || stream.Length > MaxArchiveBytes) throw new InvalidDataException("The backup file is too large or invalid.");
        var bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, ct).ConfigureAwait(false);
        return bytes;
    }
}
