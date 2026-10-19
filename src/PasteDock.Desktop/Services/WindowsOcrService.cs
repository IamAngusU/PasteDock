using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace PasteDock.Desktop.Services;

internal sealed class WindowsOcrService
{
    public async Task<string> RecognizeAsync(byte[] png, CancellationToken ct = default)
    {
        var (width, height) = PngHeader.ReadDimensions(png);
        ct.ThrowIfCancellationRequested();
        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is null)
        {
            var language = OcrEngine.AvailableRecognizerLanguages.FirstOrDefault();
            if (language is not null) engine = OcrEngine.TryCreateFromLanguage(language);
        }
        if (engine is null) throw new InvalidOperationException("No local OCR language is installed. Add a language pack in Windows Settings.");
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(png);
            await writer.StoreAsync();
            writer.DetachStream();
        }
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var scale = Math.Min(1d, OcrEngine.MaxImageDimension / (double)Math.Max(width, height));
        var transform = new BitmapTransform
        {
            ScaledWidth = (uint)Math.Max(1, Math.Floor(width * scale)),
            ScaledHeight = (uint)Math.Max(1, Math.Floor(height * scale))
        };
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
            transform, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
        ct.ThrowIfCancellationRequested();
        var result = await engine.RecognizeAsync(bitmap);
        ct.ThrowIfCancellationRequested();
        return string.Join(Environment.NewLine, result.Lines.Select(line => line.Text));
    }
}
