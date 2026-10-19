using System.Text;

namespace PasteDock.Core.Models;

/// <summary>Only explicitly supported formats are persisted. No OLE objects are deserialized.</summary>
public sealed record ClipPayload
{
    public string? Text { get; init; }
    public string? Html { get; init; }
    public string? Rtf { get; init; }
    public byte[]? Png { get; init; }
    public string[] Files { get; init; } = [];
    public string? OcrText { get; init; }
    public int ImageWidth { get; init; }
    public int ImageHeight { get; init; }

    public long GetByteCount()
    {
        static long Size(string? text) => text is null ? 0 : Encoding.UTF8.GetByteCount(text);
        return checked(Size(Text) + Size(Html) + Size(Rtf) + (Png?.LongLength ?? 0)
            + Files.Sum(Size) + Size(OcrText));
    }

    public string GetSearchableText() => string.Join("\n", new[] { Text, OcrText }
        .Where(s => !string.IsNullOrEmpty(s)).Concat(Files));

    public string GetPlainText() => Text ?? OcrText ??
        (Files.Length > 0 ? string.Join(Environment.NewLine, Files)
        : throw new InvalidOperationException("This item has no text. Extract text from the image first."));

    public void Validate(long maxBytes = Limits.MaxPayloadBytes)
    {
        if (Files is null || Files.Length > 4096 || Files.Any(p => p is null || p.Length > 32768))
            throw new InvalidDataException("Invalid file references.");
        if (Text is null && Html is null && Rtf is null && Png is null && Files.Length == 0)
            throw new InvalidDataException("The clipboard item is empty.");
        if (GetByteCount() > maxBytes)
            throw new InvalidDataException("The item exceeds the configured size limit. It was not truncated.");
        if (Png is { Length: > 0 })
        {
            var (width, height) = PngHeader.ReadDimensions(Png);
            if ((long)width * height > Limits.MaxImagePixels)
                throw new InvalidDataException("The image exceeds the pixel safety limit.");
            if (ImageWidth != width || ImageHeight != height)
                throw new InvalidDataException("The image dimensions do not match the PNG header.");
        }
        else if (Png is not null)
            throw new InvalidDataException("The PNG is empty.");
    }
}

public static class Limits
{
    public const long MaxPayloadBytes = 64L * 1024 * 1024;
    public const long MaxImagePixels = 40_000_000;
    public const int PreviewCharacters = 1600;
    public const int SearchCharacters = 4096;
    public const int MaxItems = 10_000;
}
