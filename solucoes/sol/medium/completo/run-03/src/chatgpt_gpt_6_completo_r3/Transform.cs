using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AnatelEtl;

internal static class Transform
{
    internal static readonly DateOnly Today = new(2026, 10, 15);
    internal static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    internal static string? Upper(string? value) => Clean(value)?.ToUpperInvariant();
    internal static double? Number(string? value) =>
        double.TryParse(Clean(value)?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
        && double.IsFinite(n) ? n : null;
    internal static DateOnly? Date(string? value) =>
        DateOnly.TryParseExact(Clean(value), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    internal static string? DateText(DateOnly? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    internal static string? Normalize(string? value)
    {
        value = Clean(value);
        if (value is null) return null;
        var result = new StringBuilder(value.Length);
        var space = false;
        foreach (var rune in value.Normalize(NormalizationForm.FormD).EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
                continue;
            if (Rune.IsLetterOrDigit(rune))
            {
                if (space && result.Length > 0) result.Append(' ');
                result.Append(Rune.ToUpperInvariant(rune));
                space = false;
            }
            else space = true;
        }
        return result.Length == 0 ? null : result.ToString();
    }

    private static readonly (Regex Pattern, string Name)[] Operators =
    [
        (new(@"\b(?:TELEFONICA|VIVO)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant), "VIVO"),
        (new(@"\b(?:CLARO|EMBRATEL)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant), "CLARO"),
        (new(@"^TIM\b", RegexOptions.Compiled | RegexOptions.CultureInvariant), "TIM"),
        (new(@"^OI\b|\bTELEMAR\b|\bBRASIL TELECOM\b", RegexOptions.Compiled | RegexOptions.CultureInvariant), "OI"),
        (new(@"\bALGAR\b", RegexOptions.Compiled | RegexOptions.CultureInvariant), "ALGAR"),
        (new(@"\bSERCOMTEL\b", RegexOptions.Compiled | RegexOptions.CultureInvariant), "SERCOMTEL"),
        (new(@"\bBRISANET\b", RegexOptions.Compiled | RegexOptions.CultureInvariant), "BRISANET")
    ];
    internal static string Operator(string? entity)
    {
        if (entity is not null)
            foreach (var (pattern, name) in Operators)
                if (pattern.IsMatch(entity)) return name;
        return "OUTRAS";
    }

    internal static string Generation(string? technology) => technology?.ToUpperInvariant() switch
    {
        null or "" => "N/D", "GSM" or "CDMA" => "2G", "WCDMA" or "WDCMA" => "3G",
        "LTE" => "4G", "NR" => "5G", _ => "OUTRA"
    };

    internal static double? Bandwidth(string? designation)
    {
        if (designation is null || designation.Length < 4) return null;
        int unit = -1;
        double factor = 0;
        for (int i = 0; i < 4; i++)
        {
            var c = char.ToUpperInvariant(designation[i]);
            if (c is >= '0' and <= '9') continue;
            if (i == 0 || unit >= 0) return null;
            factor = c switch { 'H' => .001, 'K' => 1, 'M' => 1000, 'G' => 1000000, _ => 0 };
            if (factor == 0) return null;
            unit = i;
        }
        if (unit < 0) return null;
        var digits = designation[..4].ToCharArray();
        digits[unit] = '.';
        return double.Parse(digits, CultureInfo.InvariantCulture) * factor;
    }

    internal static double? Round(double value, int digits) => double.IsFinite(value)
        ? Math.Round(value, digits, MidpointRounding.AwayFromZero) : null;
    internal static double? Duplex(double? tx, double? rx)
    {
        if (!tx.HasValue || !rx.HasValue) return null;
        // Decimal arithmetic preserves decimal midpoints from the source (e.g. 1.0005).
        try { return (double)Math.Round((decimal)tx.Value - (decimal)rx.Value, 3, MidpointRounding.AwayFromZero); }
        catch (OverflowException) { return Round(tx.Value - rx.Value, 3); }
    }
    internal static double? PowerDbm(double? watts) => watts is > 0
        ? Round(10 * (Math.Log10(watts.Value) + 3), 1) : null;
    internal static double? Years(DateOnly? first) => first.HasValue ? Round((Today.DayNumber - first.Value.DayNumber) / 365.25, 1) : null;
    internal static int Expired(DateOnly? validity) => validity.HasValue && validity.Value < Today ? 1 : 0;
    internal static int? InRange(double? value, double min, double max) => value.HasValue ? (value >= min && value <= max ? 1 : 0) : null;
    internal static int Coordinates(double? lat, double? lon) =>
        lat.HasValue && lon.HasValue && (lat != 0 || lon != 0)
        && lat >= -33.75 && lat <= 5.28 && lon >= -73.99 && lon <= -28.83 ? 1 : 0;
}
