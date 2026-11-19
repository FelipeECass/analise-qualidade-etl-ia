using System.Globalization;
using System.Text;

namespace Etl;

/// <summary>Regras da seção 1 (limpeza geral) e arredondamento da seção 4.</summary>
public static class Cleaning
{
    /// <summary>Remove espaços nas extremidades; vazio vira null.</summary>
    public static string? Clean(string? raw)
    {
        if (raw is null) return null;
        var s = raw.Trim();
        return s.Length == 0 ? null : s;
    }

    public static string? Upper(string? cleaned) => cleaned?.ToUpperInvariant();

    /// <summary>Número finito; '.' e ',' são sempre separador decimal.</summary>
    public static double? ParseNumber(string? cleaned)
    {
        if (cleaned is null) return null;
        var s = cleaned.Replace(',', '.');
        if (!double.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
                CultureInfo.InvariantCulture, out var v))
            return null;
        return double.IsFinite(v) ? v : null;
    }

    /// <summary>Data estrita yyyy-MM-dd.</summary>
    public static DateOnly? ParseDate(string? cleaned)
    {
        if (cleaned is null) return null;
        return DateOnly.TryParseExact(cleaned, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;
    }

    /// <summary>Remove acentos, maiúsculas, não letra/dígito vira espaço, colapsa espaços.</summary>
    public static string? NormalizeText(string? cleaned)
    {
        if (cleaned is null) return null;
        var decomposed = cleaned.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        var pendingSpace = false;
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
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

    /// <summary>
    /// Arredondamento aritmético em double (valor × 10^n, arredonda, ÷ 10^n), como o resultado esperado:
    /// os exemplos verificados (0,1345 → 0,134; 3,7624999… → 3,762; −4,6374999… → −4,637)
    /// só são reproduzidos com Math.Round padrão (metade para o par), não com "metade para longe do zero".
    /// </summary>
    public static double RoundValue(double value, int digits)
        => Math.Round(value, digits, MidpointRounding.ToEven);
}
