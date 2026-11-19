using System.Globalization;
using System.Text;

namespace Etl;

/// <summary>Regras gerais de limpeza (seção 1 da especificação).</summary>
public static class Cleaner
{
    /// <summary>Trim; vazio ou só espaços vira null.</summary>
    public static string? Clean(string? value)
    {
        if (value == null) return null;
        var t = value.Trim();
        return t.Length == 0 ? null : t;
    }

    /// <summary>Texto limpo em maiúsculas.</summary>
    public static string? Upper(string? value) => Clean(value)?.ToUpperInvariant();

    /// <summary>Remove acentos, maiúsculas, não letra/dígito vira espaço, colapsa espaços; vazio → null.</summary>
    public static string? NormalizeText(string? value)
    {
        var t = Clean(value);
        if (t == null) return null;

        // Normalize(FormD) é no-op com InvariantGlobalization; AccentTable cobre esse caso.
        var decomposed = t.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        bool pendingSpace = false;
        foreach (var original in decomposed)
        {
            var piece = AccentTable.Map.TryGetValue(original, out var mapped) ? mapped : null;
            if (piece == null) Append(original); else foreach (var ch in piece) Append(ch);
        }

        void Append(char ch)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) return;
            var up = char.ToUpperInvariant(ch);
            if (char.IsLetterOrDigit(up))
            {
                if (pendingSpace && sb.Length > 0) sb.Append(' ');
                pendingSpace = false;
                sb.Append(up);
            }
            else
            {
                pendingSpace = true;
            }
        }
        return sb.Length == 0 ? null : sb.ToString();
    }

    /// <summary>Número com '.' ou ',' decimal; sem separador de milhar; não finito → null.</summary>
    public static double? ParseNumber(string? value)
    {
        var t = Clean(value);
        if (t == null) return null;
        t = t.Replace(',', '.');
        if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return null;
        if (!double.IsFinite(d)) return null;
        return d == 0 ? 0.0 : d;   // normaliza -0
    }

    /// <summary>Data estrita yyyy-MM-dd; qualquer outra coisa → null.</summary>
    public static DateOnly? ParseDate(string? value)
    {
        var t = Clean(value);
        if (t == null) return null;
        return DateOnly.TryParseExact(t, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;
    }
}
