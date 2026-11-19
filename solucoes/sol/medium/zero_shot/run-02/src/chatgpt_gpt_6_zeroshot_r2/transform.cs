using System.Globalization;
using System.Text;

namespace AnatelEtl;

internal static class Transform
{
    public static readonly DateOnly Today = new(2026, 10, 15);
    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static double? Number(string? value)
    {
        if (value is null || !double.TryParse(value.Replace(',', '.'), NumberStyles.Float,
            CultureInfo.InvariantCulture, out var result) || !double.IsFinite(result)) return null;
        return result == 0 ? 0d : result; // Canonicalize negative zero for deduplication.
    }

    public static DateOnly? Date(string? value) => DateOnly.TryParseExact(value, "yyyy-MM-dd",
        CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
    public static string? DateText(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string? Normalize(string? value)
    {
        if (value is null) return null;
        var text = new StringBuilder();
        var pendingSpace = false;
        foreach (var rune in value.Normalize(NormalizationForm.FormD).EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark or
                UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark) continue;
            var upper = Rune.ToUpperInvariant(rune);
            if (Rune.IsLetterOrDigit(upper))
            {
                if (pendingSpace && text.Length > 0) text.Append(' ');
                text.Append(upper.ToString());
                pendingSpace = false;
            }
            else pendingSpace = true;
        }
        return text.Length == 0 ? null : text.ToString();
    }

    public static string Operator(string? entity)
    {
        if (entity is null) return "OUTRAS";
        var words = entity.Split(' ');
        if (words.Contains("TELEFONICA") || words.Contains("VIVO")) return "VIVO";
        if (words.Contains("CLARO") || words.Contains("EMBRATEL")) return "CLARO";
        if (words[0] == "TIM") return "TIM";
        if (words[0] == "OI" || words.Contains("TELEMAR") || (" " + entity + " ").Contains(" BRASIL TELECOM ")) return "OI";
        if (words.Contains("ALGAR")) return "ALGAR";
        if (words.Contains("SERCOMTEL")) return "SERCOMTEL";
        if (words.Contains("BRISANET")) return "BRISANET";
        return "OUTRAS";
    }

    public static string Generation(string? technology) => technology?.ToUpperInvariant() switch
    {
        null or "" => "N/D", "GSM" or "CDMA" => "2G", "WCDMA" or "WDCMA" => "3G",
        "LTE" => "4G", "NR" => "5G", _ => "OUTRA"
    };

    public static double? Bandwidth(string? designation)
    {
        if (designation is null || designation.Length < 4) return null;
        var unit = -1;
        var value = 0;
        var fractionalDigits = 0;
        var factor = 0d;
        for (var i = 0; i < 4; i++)
        {
            var c = char.ToUpperInvariant(designation[i]);
            if (c is >= '0' and <= '9')
            {
                value = value * 10 + (c - '0');
                if (unit >= 0) fractionalDigits++;
            }
            else
            {
                if (i == 0 || unit >= 0) return null;
                factor = c switch { 'H' => 0.001, 'K' => 1, 'M' => 1000, 'G' => 1000000, _ => 0 };
                if (factor == 0) return null;
                unit = i;
            }
        }
        return unit < 0 ? null : value * factor / Math.Pow(10, fractionalDigits);
    }

    public static double Round(double value, int digits)
    {
        // Decimal rounding preserves midpoint behavior for ordinary decimal source values.
        try { return (double)decimal.Round((decimal)value, digits, MidpointRounding.AwayFromZero); }
        catch (OverflowException) { return Math.Round(value, digits, MidpointRounding.AwayFromZero); }
    }

    public static double? Duplex(double? tx, double? rx)
    {
        if (!tx.HasValue || !rx.HasValue) return null;
        try { return (double)decimal.Round((decimal)tx.Value - (decimal)rx.Value, 3, MidpointRounding.AwayFromZero); }
        catch (OverflowException) { return Round(tx.Value - rx.Value, 3); }
    }

    public static bool? InRange(double? value, double min, double max) => value.HasValue ? value >= min && value <= max : null;

    public static string Key(params object?[] values)
    {
        // Length prefixes distinguish NULL and embedded separators without hash collisions.
        var builder = new StringBuilder();
        foreach (var value in values)
        {
            if (value is null) { builder.Append("-1:"); continue; }
            var text = value is double number ? number.ToString("R", CultureInfo.InvariantCulture) : (string)value;
            builder.Append(text.Length).Append(':').Append(text);
        }
        return builder.ToString();
    }
}
