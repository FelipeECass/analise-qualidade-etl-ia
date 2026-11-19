using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Etl.Transform;

/// <summary>Limpeza e tipagem de valores brutos (seção 1 da especificação).</summary>
public static class Clean
{
    private static readonly Regex NumberPattern =
        new(@"^[+-]?([0-9]+(\.[0-9]*)?|\.[0-9]+)([eE][+-]?[0-9]+)?$", RegexOptions.CultureInvariant);

    private static readonly Regex DatePattern =
        new(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}$", RegexOptions.CultureInvariant);

    /// <summary>Trim; vazio ou só espaços vira null.</summary>
    public static string? Text(string? raw)
    {
        if (raw is null) return null;
        var t = raw.Trim();
        return t.Length == 0 ? null : t;
    }

    /// <summary>Texto limpo em maiúsculas.</summary>
    public static string? Upper(string? raw) => Text(raw)?.ToUpperInvariant();

    /// <summary>
    /// Normalização de texto (regra 1.6): remove acentos, maiúsculas, troca o que não é
    /// letra/dígito por espaço, colapsa espaços e remove as pontas.
    /// </summary>
    public static string? Normalize(string? raw)
    {
        var t = Text(raw);
        if (t is null) return null;

        var sb = new StringBuilder(t.Length);
        foreach (var ch in t.Normalize(NormalizationForm.FormD))
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat is UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark
                or UnicodeCategory.EnclosingMark)
            {
                continue;
            }

            var up = char.ToUpperInvariant(ch);
            sb.Append(char.IsLetterOrDigit(up) ? up : ' ');
        }

        var collapsed = string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length == 0 ? null : collapsed;
    }

    /// <summary>
    /// Número (regra 1.3). Aceita '.' ou ',' como decimal (nunca milhar). Texto que não seja
    /// número finito (NA, NaN, #N/A, unidades, datas, etc.) vira null.
    /// </summary>
    public static double? Number(string? raw)
    {
        var t = NumericText(raw);
        if (t is null) return null;
        if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) || !double.IsFinite(d))
        {
            return null;
        }
        return d + 0.0; // evita -0.0
    }

    /// <summary>Mesmo critério de <see cref="Number"/>, em decimal exato (para arredondamentos).</summary>
    public static decimal? Decimal(string? raw)
    {
        var t = NumericText(raw);
        if (t is null) return null;
        return decimal.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    /// <summary>Data estrita yyyy-MM-dd; qualquer outra coisa (inclusive data com hora) vira null.</summary>
    public static DateOnly? Date(string? raw)
    {
        var t = Text(raw);
        if (t is null || !DatePattern.IsMatch(t)) return null;
        return DateOnly.TryParseExact(t, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;
    }

    public static double ToDouble(decimal d) =>
        double.Parse(d.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    private static string? NumericText(string? raw)
    {
        var t = Text(raw);
        if (t is null) return null;
        t = t.Replace(',', '.');
        return NumberPattern.IsMatch(t) ? t : null;
    }
}
