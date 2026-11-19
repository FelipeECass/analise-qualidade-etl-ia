using System.Globalization;

namespace AnatelEtl.Transform;

/// <summary>Conversões tolerantes de texto para número/data (retornam <c>null</c> se inválido).</summary>
public static class ValueParser
{
    private const string DateFormat = "yyyy-MM-dd";

    public static double? ParseDouble(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var normalized = text.Trim().Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            && double.IsFinite(value)
            ? value
            : null;
    }

    public static DateOnly? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return DateOnly.TryParseExact(
            text.Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }
}
