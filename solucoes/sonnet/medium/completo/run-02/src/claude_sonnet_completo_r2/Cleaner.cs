using System.Globalization;
using System.Text;

namespace Etl;

public static class Cleaner
{
    public static string? Text(string? s)
    {
        if (s == null) return null;
        var t = s.Trim();
        return t.Length == 0 ? null : t;
    }

    public static string? Upper(string? s)
    {
        var t = Text(s);
        return t?.ToUpperInvariant();
    }

    public static double? Number(string? s)
    {
        var t = Text(s);
        if (t == null) return null;
        t = t.Replace(',', '.');
        if (!double.TryParse(t, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
                CultureInfo.InvariantCulture, out var v)) return null;
        return double.IsFinite(v) ? v : null;
    }

    public static DateTime? Date(string? s)
    {
        var t = Text(s);
        if (t == null) return null;
        return DateTime.TryParseExact(t, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    // Em modo globalization-invariant o Normalize não decompõe; nesse caso usa-se tabela de apoio (Latin-1/Latin Extended-A).
    private static readonly bool NormalizeWorks = "\u00C1".Normalize(NormalizationForm.FormD).Length == 2;
    private const string FromAcc = "ÀÁÂÃÄÅàáâãäåÇçÈÉÊËèéêëÌÍÎÏìíîïÑñÒÓÔÕÖòóôõöÙÚÛÜùúûüÝýÿ";
    private const string ToAcc   = "AAAAAAaaaaaaCcEEEEeeeeIIIIiiiiNnOOOOOoooooUUUUuuuuYyy";

    private static string Decompose(string t)
    {
        if (NormalizeWorks) return t.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(t.Length);
        foreach (var ch in t) { int i = FromAcc.IndexOf(ch); sb.Append(i >= 0 ? ToAcc[i] : ch); }
        return sb.ToString();
    }

    public static string? Normalize(string? s)
    {
        var t = Text(s);
        if (t == null) return null;
        var d = Decompose(t);
        var sb = new StringBuilder(d.Length);
        bool pendingSpace = false;
        foreach (var ch in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch))
            {
                if (pendingSpace && sb.Length > 0) sb.Append(' ');
                pendingSpace = false;
                sb.Append(char.ToUpperInvariant(ch));
            }
            else pendingSpace = true;
        }
        return sb.Length == 0 ? null : sb.ToString();
    }
}
