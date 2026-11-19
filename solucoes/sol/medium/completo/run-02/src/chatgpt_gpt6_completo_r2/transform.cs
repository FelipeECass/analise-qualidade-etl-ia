using System.Globalization;
using System.Text;

namespace AnatelEtl;

internal readonly record struct StationKey(string Uf, string Fistel, string Number);
internal readonly record struct EmissionKey(
    StationKey Station, string? Act, double? Azimuth, double? Tx, double? Rx,
    string? Designation, string? Technology);

internal static class Transform
{
    public static readonly DateOnly Today = new(2026, 10, 15);
    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    public static string? Upper(string? value) => Clean(value)?.ToUpperInvariant();

    public static double? Number(string? value)
    {
        value = Clean(value);
        if (value is null) return null;
        if (double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture,
            out double result) && double.IsFinite(result)) return result == 0 ? 0 : result;
        return null;
    }

    public static DateOnly? Date(string? value)
    {
        value = Clean(value);
        return value is { Length: 10 } && DateOnly.TryParseExact(value, "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
    }

    public static string? DateText(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string? Normalize(string? value)
    {
        value = Clean(value);
        if (value is null) return null;
        var result = new StringBuilder(value.Length);
        bool space = false;
        foreach (Rune rune in value.Normalize(NormalizationForm.FormD).EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark
                or UnicodeCategory.EnclosingMark) continue;
            Rune upper = Rune.ToUpperInvariant(rune);
            if (Rune.IsLetterOrDigit(upper))
            {
                if (space && result.Length > 0) result.Append(' ');
                result.Append(upper.ToString());
                space = false;
            }
            else space = true;
        }
        return result.Length == 0 ? null : result.ToString();
    }

    public static string Operator(string? entity)
    {
        if (entity is null) return "OUTRAS";
        string padded = $" {entity} ";
        bool Word(string word) => padded.Contains($" {word} ", StringComparison.Ordinal);
        bool Starts(string word) => entity == word || entity.StartsWith(word + " ", StringComparison.Ordinal);
        if (Word("TELEFONICA") || Word("VIVO")) return "VIVO";
        if (Word("CLARO") || Word("EMBRATEL")) return "CLARO";
        if (Starts("TIM")) return "TIM";
        if (Starts("OI") || Word("TELEMAR") || Word("BRASIL TELECOM")) return "OI";
        if (Word("ALGAR")) return "ALGAR";
        if (Word("SERCOMTEL")) return "SERCOMTEL";
        if (Word("BRISANET")) return "BRISANET";
        return "OUTRAS";
    }

    public static string Generation(string? technology) => Upper(technology) switch
    {
        null => "N/D", "GSM" or "CDMA" => "2G", "WCDMA" or "WDCMA" => "3G",
        "LTE" => "4G", "NR" => "5G", _ => "OUTRA"
    };

    public static double? Bandwidth(string? designation)
    {
        designation = Upper(designation);
        if (designation is null || designation.Length < 4) return null;
        int unit = -1;
        double factor = 0;
        for (int i = 0; i < 4; i++)
        {
            char c = designation[i];
            if (c is >= '0' and <= '9') continue;
            if (unit != -1 || i == 0) return null;
            factor = c switch { 'H' => 0.001, 'K' => 1, 'M' => 1000, 'G' => 1000000, _ => 0 };
            if (factor == 0) return null;
            unit = i;
        }
        if (unit == -1) return null;
        string number = designation[..unit] + "." + designation[(unit + 1)..4];
        return double.Parse(number, CultureInfo.InvariantCulture) * factor;
    }

    public static double? Round(double? value, int digits) => value is double v && double.IsFinite(v)
        ? Math.Round(v, digits, MidpointRounding.AwayFromZero) : null;
    public static double? Duplex(double? tx, double? rx) => Round(tx - rx, 3);
    public static double? Dbm(double? watts) => watts is > 0
        ? Round(10 * (Math.Log10(watts.Value) + 3), 1) : null;
    public static double? Years(DateOnly? first) => first is DateOnly date
        ? Round((Today.DayNumber - date.DayNumber) / 365.25, 1) : null;
    public static int Expired(DateOnly? valid) => valid is DateOnly date && date < Today ? 1 : 0;
    public static int? InRange(double? value, double min, double max) =>
        value is null ? null : value >= min && value <= max ? 1 : 0;
    public static int Coordinates(double? lat, double? lon) =>
        lat is >= -33.75 and <= 5.28 && lon is >= -73.99 and <= -28.83 &&
        (lat != 0 || lon != 0) ? 1 : 0;

    // Length-prefixed strings and presence-tagged IEEE doubles are unambiguous and lossless.
    // No hashes: different keys can never collide. Negative and positive zero are identical.
    public static byte[] EncodeKey(EmissionKey key)
    {
        using var stream = new MemoryStream(160);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        void Text(string? text)
        {
            writer.Write(text is not null);
            if (text is not null) writer.Write(text);
        }
        void Numeric(double? number)
        {
            writer.Write(number.HasValue);
            if (number.HasValue) writer.Write(number.Value == 0 ? 0d : number.Value);
        }
        Text(key.Station.Uf); Text(key.Station.Fistel); Text(key.Station.Number); Text(key.Act);
        Numeric(key.Azimuth); Numeric(key.Tx); Numeric(key.Rx); Text(key.Designation); Text(key.Technology);
        writer.Flush();
        return stream.ToArray();
    }
}
