using System.Globalization;

namespace PasteDock.Core.Services;

public readonly record struct HexColor(byte Red, byte Green, byte Blue, byte Alpha)
{
    public static bool TryParse(string? text, out HexColor color)
    {
        color = default;
        var hex = (text ?? "").Trim();
        if (!hex.StartsWith('#')) return false;
        hex = hex[1..];
        if (hex.Length is not (3 or 4 or 6 or 8) || !hex.All(Uri.IsHexDigit)) return false;
        if (hex.Length is 3 or 4) hex = string.Concat(hex.Select(character => new string(character, 2)));
        static byte Byte(string pair) => byte.Parse(pair, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        color = new(Byte(hex[..2]), Byte(hex[2..4]), Byte(hex[4..6]), hex.Length == 8 ? Byte(hex[6..8]) : (byte)255);
        return true;
    }
}
