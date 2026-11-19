using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AnatelEtl;

internal static class Transform
{
    public static readonly DateOnly Today = new(2026, 10, 15);
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly (Regex Pattern, string Operator)[] Operators =
    [
        (new(@"\b(TELEFONICA|VIVO)\b", RegexOptions.CultureInvariant), "VIVO"),
        (new(@"\b(CLARO|EMBRATEL)\b", RegexOptions.CultureInvariant), "CLARO"),
        (new(@"^TIM\b", RegexOptions.CultureInvariant), "TIM"),
        (new(@"^OI\b|\bTELEMAR\b|\bBRASIL TELECOM\b", RegexOptions.CultureInvariant), "OI"),
        (new(@"\bALGAR\b", RegexOptions.CultureInvariant), "ALGAR"),
        (new(@"\bSERCOMTEL\b", RegexOptions.CultureInvariant), "SERCOMTEL"),
        (new(@"\bBRISANET\b", RegexOptions.CultureInvariant), "BRISANET")
    ];

    public static string? Upper(string? value) => value?.ToUpperInvariant();

    public static double? Number(string? value)
    {
        if (value is null) return null;
        return double.TryParse(value.Replace(',', '.'), NumberStyles.Float, Invariant, out double result)
               && double.IsFinite(result) ? result : null;
    }

    public static DateOnly? Date(string? value) => value?.Length == 10 &&
        DateOnly.TryParseExact(value, "yyyy-MM-dd", Invariant, DateTimeStyles.None, out var date) ? date : null;
    public static string? DateText(DateOnly? value) => value?.ToString("yyyy-MM-dd", Invariant);

    public static string? Normalize(string? value)
    {
        if (value is null) return null;
        var output = new StringBuilder(value.Length);
        bool space = false;
        foreach (Rune rune in value.Normalize(NormalizationForm.FormD).EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
                continue;
            if (Rune.IsLetterOrDigit(rune))
            {
                if (space && output.Length > 0) output.Append(' ');
                output.Append(Rune.ToUpperInvariant(rune));
                space = false;
            }
            else space = true;
        }
        return output.Length == 0 ? null : output.ToString();
    }

    public static string Operator(string? entity)
    {
        if (entity is not null)
            foreach (var rule in Operators)
                if (rule.Pattern.IsMatch(entity)) return rule.Operator;
        return "OUTRAS";
    }

    public static string Generation(string? technology) => Upper(technology) switch
    {
        "GSM" or "CDMA" => "2G", "WCDMA" or "WDCMA" => "3G", "LTE" => "4G", "NR" => "5G",
        null => "N/D", _ => "OUTRA"
    };

    public static double? Bandwidth(string? designation)
    {
        if (designation is null || designation.Length < 4) return null;
        ReadOnlySpan<char> prefix = designation.AsSpan(0, 4);
        int unit = -1;
        double factor = 0;
        for (int i = 0; i < 4; i++)
        {
            char c = char.ToUpperInvariant(prefix[i]);
            if (c is >= '0' and <= '9') continue;
            if (i == 0 || unit >= 0) return null;
            factor = c switch { 'H' => 0.001, 'K' => 1, 'M' => 1000, 'G' => 1000000, _ => 0 };
            if (factor == 0) return null;
            unit = i;
        }
        if (unit < 0) return null;
        int integer = 0, fraction = 0, divisor = 1;
        for (int i = 0; i < unit; i++) integer = integer * 10 + prefix[i] - '0';
        for (int i = unit + 1; i < 4; i++) { fraction = fraction * 10 + prefix[i] - '0'; divisor *= 10; }
        return (integer + (double)fraction / divisor) * factor;
    }

    public static double Round(double value, int places) => Math.Round(value, places, MidpointRounding.AwayFromZero);

    public static double? Duplex(double? tx, double? rx)
    {
        if (tx is null || rx is null) return null;
        // Decimal arithmetic preserves decimal midpoint cases such as 1.2345 - 1.234.
        if (decimal.TryParse(tx.Value.ToString("R", Invariant), NumberStyles.Float, Invariant, out decimal a) &&
            decimal.TryParse(rx.Value.ToString("R", Invariant), NumberStyles.Float, Invariant, out decimal b))
        {
            try { return (double)decimal.Round(a - b, 3, MidpointRounding.AwayFromZero); }
            catch (OverflowException) { /* Fall back to finite double arithmetic. */ }
        }
        double difference = tx.Value - rx.Value;
        return double.IsFinite(difference) ? Round(difference, 3) : null;
    }

    public static double? PowerDbm(double? watts) => watts is > 0
        ? Round(10 * (Math.Log10(watts.Value) + 3), 1) : null;
    public static double? Years(DateOnly? date) => date is { } d ? Round((Today.DayNumber - d.DayNumber) / 365.25, 1) : null;
    public static int Expired(DateOnly? date) => date is { } d && d < Today ? 1 : 0;
    public static int? InRange(double? value, double low, double high) => value is { } v ? (v >= low && v <= high ? 1 : 0) : null;
    public static int Coordinates(double? latitude, double? longitude) =>
        latitude is >= -33.75 and <= 5.28 && longitude is >= -73.99 and <= -28.83 &&
        !(latitude == 0 && longitude == 0) ? 1 : 0;
}
