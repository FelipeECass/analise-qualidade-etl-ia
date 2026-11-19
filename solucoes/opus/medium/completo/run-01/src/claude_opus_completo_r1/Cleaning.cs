using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AnatelEtl;

/// <summary>Regras da seção 1 (leitura e limpeza geral).</summary>
public static partial class Cleaning
{
    /// <summary>Texto limpo: trim nas pontas; vazio/só espaços → null.</summary>
    public static string? Text(string? raw)
    {
        if (raw is null) return null;
        var t = raw.Trim();
        return t.Length == 0 ? null : t;
    }

    /// <summary>Texto limpo em maiúsculas.</summary>
    public static string? Upper(string? raw) => Text(raw)?.ToUpperInvariant();

    [GeneratedRegex(@"^[+-]?(?:[0-9]+\.?[0-9]*|\.[0-9]+)(?:[eE][+-]?[0-9]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();

    /// <summary>Número: '.' ou ',' como separador decimal (nunca milhar); não finito → null.</summary>
    public static double? Number(string? raw)
    {
        var t = Text(raw);
        if (t is null) return null;
        t = t.Replace(',', '.');
        if (!NumberPattern().IsMatch(t)) return null;
        if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return null;
        return double.IsFinite(v) ? v : null;
    }

    /// <summary>Data estrita yyyy-MM-dd; qualquer outra coisa → null.</summary>
    public static DateOnly? Date(string? raw)
    {
        var t = Text(raw);
        if (t is null || t.Length != 10) return null;
        return DateOnly.TryParseExact(t, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;
    }

    /// <summary>
    /// Normalização de texto (regra 1.6): remove acentos (NFD e descarte das marcas),
    /// maiúsculas, não letra/dígito → espaço, colapsa espaços, trim; vazio → null.
    /// </summary>
    public static string? Normalize(string? raw)
    {
        if (raw is null) return null;
        var decomposed = raw.Normalize(NormalizationForm.FormD);
        var noMarks = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
                continue;
            noMarks.Append(ch);
        }
        var upper = noMarks.ToString().ToUpperInvariant();

        var sb = new StringBuilder(upper.Length);
        bool pendingSpace = false;
        foreach (var rune in upper.EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune))
            {
                if (pendingSpace && sb.Length > 0) sb.Append(' ');
                pendingSpace = false;
                sb.Append(rune.ToString());
            }
            else pendingSpace = true;
        }
        return sb.Length == 0 ? null : sb.ToString();
    }
}
