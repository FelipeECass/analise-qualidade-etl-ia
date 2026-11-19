using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Etl;

/// <summary>Funções puras de limpeza (seção 1 da especificação).</summary>
public static partial class Cleaning
{
    /// <summary>Trim; vazio/só espaços → null.</summary>
    public static string? Text(string? raw)
    {
        if (raw == null) return null;
        var t = raw.Trim();
        return t.Length == 0 ? null : t;
    }

    /// <summary>Texto limpo em maiúsculas.</summary>
    public static string? Upper(string? raw) => Text(raw)?.ToUpperInvariant();

    [GeneratedRegex(@"^[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex NumberRegex();

    /// <summary>Número com '.' ou ',' como separador decimal; não numérico ou não finito → null.</summary>
    public static double? Number(string? raw)
    {
        var t = Text(raw);
        if (t == null) return null;
        t = t.Replace(',', '.');
        if (!NumberRegex().IsMatch(t)) return null;
        if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return null;
        if (!double.IsFinite(d)) return null;
        return d;
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex DateRegex();

    /// <summary>Data estrita yyyy-MM-dd; outra coisa → null.</summary>
    public static DateOnly? Date(string? raw)
    {
        var t = Text(raw);
        if (t == null || !DateRegex().IsMatch(t)) return null;
        return DateOnly.TryParseExact(t, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;
    }

    /// <summary>
    /// Normalização de texto (regra 1.6): remove acentos (NFD, descarta marcas), maiúsculas,
    /// não-letra/não-dígito → espaço, colapsa espaços, trim; vazio → null.
    /// </summary>
    public static string? Normalize(string? raw)
    {
        if (raw == null) return null;
        var decomposed = raw.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        bool pendingSpace = false;
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            var c = char.ToUpperInvariant(ch);
            if (char.IsLetterOrDigit(c))
            {
                if (pendingSpace && sb.Length > 0) sb.Append(' ');
                pendingSpace = false;
                sb.Append(c);
            }
            else
            {
                pendingSpace = true;
            }
        }
        return sb.Length == 0 ? null : sb.ToString();
    }
}
