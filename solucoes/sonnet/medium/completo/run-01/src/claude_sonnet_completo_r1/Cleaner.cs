using System.Globalization;
using System.Text;

namespace Etl;

public static class Cleaner
{
    /// <summary>Trim; vazio vira null.</summary>
    public static string? Text(string? s)
    {
        if (s == null) return null;
        s = s.Trim();
        return s.Length == 0 ? null : s;
    }

    public static string? Upper(string? s)
    {
        s = Text(s);
        return s?.ToUpperInvariant();
    }

    public static double? Number(string? s)
    {
        s = Text(s);
        if (s == null) return null;
        if (s.Contains(',')) s = s.Replace(',', '.');
        if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return null;
        return double.IsFinite(d) ? d : null;
    }

    public static DateTime? Date(string? s)
    {
        s = Text(s);
        if (s == null || s.Length != 10) return null;
        return DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    /// <summary>Remove acentos, maiúsculas, não alfanumérico → espaço, colapsa espaços.</summary>
    public static string? Normalize(string? s)
    {
        s = Text(s);
        if (s == null) return null;
        string d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        bool pendingSpace = false;
        foreach (var rune in d.EnumerateRunes())
        {
            var cat = Rune.GetUnicodeCategory(rune);
            if (cat is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
                continue;
            if (Rune.IsLetterOrDigit(rune))
            {
                if (pendingSpace && sb.Length > 0) sb.Append(' ');
                pendingSpace = false;
                sb.Append(Rune.ToUpperInvariant(rune).ToString());
            }
            else pendingSpace = true;
        }
        return sb.Length == 0 ? null : sb.ToString();
    }
}
