using System.Globalization;
using System.Text;

namespace AnatelEtl;

/// <summary>Regras da seção 1 (leitura e limpeza geral).</summary>
public static class Cleaning
{
    /// <summary>Trim; vazio/só espaços → null.</summary>
    public static string? Text(string? raw)
    {
        if (raw == null) return null;
        var t = raw.Trim();
        return t.Length == 0 ? null : t;
    }

    public static string? Upper(string? raw) => Text(raw)?.ToUpperInvariant();

    /// <summary>Número com '.' ou ',' como separador decimal; não finito/inválido → null.</summary>
    public static double? Number(string? raw)
    {
        var t = Text(raw);
        if (t == null) return null;
        t = t.Replace(',', '.');
        if (!IsNumberSyntax(t)) return null;
        if (!double.TryParse(t, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
                CultureInfo.InvariantCulture, out var v)) return null;
        return double.IsFinite(v) ? v : null;
    }

    /// <summary>[+-]? (d+ (. d*)? | . d+) ([eE][+-]?d+)?</summary>
    private static bool IsNumberSyntax(string s)
    {
        int i = 0, n = s.Length;
        if (i < n && (s[i] == '+' || s[i] == '-')) i++;
        int intDigits = 0, fracDigits = 0;
        while (i < n && char.IsAsciiDigit(s[i])) { i++; intDigits++; }
        if (i < n && s[i] == '.')
        {
            i++;
            while (i < n && char.IsAsciiDigit(s[i])) { i++; fracDigits++; }
        }
        if (intDigits + fracDigits == 0) return false;
        if (i < n && (s[i] == 'e' || s[i] == 'E'))
        {
            i++;
            if (i < n && (s[i] == '+' || s[i] == '-')) i++;
            int expDigits = 0;
            while (i < n && char.IsAsciiDigit(s[i])) { i++; expDigits++; }
            if (expDigits == 0) return false;
        }
        return i == n;
    }

    /// <summary>Data estrita yyyy-MM-dd; qualquer outra coisa → null.</summary>
    public static DateOnly? Date(string? raw)
    {
        var t = Text(raw);
        if (t == null || t.Length != 10) return null;
        for (int i = 0; i < 10; i++)
        {
            bool dash = i == 4 || i == 7;
            if (dash ? t[i] != '-' : !char.IsAsciiDigit(t[i])) return null;
        }
        return DateOnly.TryParseExact(t, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;
    }

    /// <summary>Regra 1.6: remove acentos, maiúsculas, não letra/dígito → espaço, colapsa espaços.</summary>
    public static string? Normalize(string? raw)
    {
        var t = Text(raw);
        if (t == null) return null;
        var decomposed = t.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        bool pendingSpace = false;
        foreach (var ch in decomposed)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
                continue;
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
