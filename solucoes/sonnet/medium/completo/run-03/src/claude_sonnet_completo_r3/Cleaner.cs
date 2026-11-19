using System.Globalization;
using System.Text;

static class Cleaner
{
    public static string? Text(string? raw)
    {
        if (raw == null) return null;
        var t = raw.Trim();
        return t.Length == 0 ? null : t;
    }

    public static string? Upper(string? v) => v?.ToUpperInvariant();

    public static double? Number(string? raw)
    {
        var t = Text(raw);
        if (t == null) return null;
        t = t.Replace(',', '.');
        if (!double.TryParse(t, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
                CultureInfo.InvariantCulture, out var d)) return null;
        if (!double.IsFinite(d)) return null;
        return d == 0 ? 0.0 : d;
    }

    public static string? Date(string? raw)
    {
        var t = Text(raw);
        if (t == null || t.Length != 10) return null;
        return DateTime.TryParseExact(t, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
    }

    public static string? Normalize(string? raw)
    {
        var t = Text(raw);
        if (t == null) return null;
        var d = t.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        bool pendingSpace = false;
        foreach (var ch in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            var u = char.ToUpperInvariant(ch);
            if (char.IsLetterOrDigit(u))
            {
                if (pendingSpace && sb.Length > 0) sb.Append(' ');
                pendingSpace = false;
                sb.Append(u);
            }
            else pendingSpace = true;
        }
        return sb.Length == 0 ? null : sb.ToString();
    }
}
