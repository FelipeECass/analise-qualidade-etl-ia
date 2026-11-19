using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ClaudeHaikuCompletoR2;

/// <summary>Regras de limpeza geral (seção 1 da especificação).</summary>
internal static class Limpeza
{
    private static readonly Regex Espacos = new(@"\s+", RegexOptions.CultureInvariant);

    // [0-9] em vez de \d: \d aceitaria dígitos de outros alfabetos.
    private static readonly Regex FormatoData = new(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}\z", RegexOptions.CultureInvariant);

    /// <summary>Remove espaços das pontas; vazio vira null.</summary>
    public static string? Texto(string? bruto)
    {
        if (bruto is null) return null;
        var t = bruto.Trim();
        return t.Length == 0 ? null : t;
    }

    /// <summary>Texto limpo em maiúsculas (sem remover acentos).</summary>
    public static string? Maiusculas(string? bruto) => Texto(bruto)?.ToUpperInvariant();

    /// <summary>
    /// Normalização de texto (regra 1.6): remove acentos, põe em maiúsculas, troca tudo que não
    /// é letra ou dígito por espaço, colapsa espaços e remove os das pontas.
    /// </summary>
    public static string? TextoNormalizado(string? bruto)
    {
        var t = Texto(bruto);
        if (t is null) return null;

        var decomposto = t.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposto.Length);
        foreach (var c in decomposto)
        {
            if (EhMarca(c)) continue;
            sb.Append(char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : ' ');
        }

        var resultado = Espacos.Replace(sb.ToString(), " ").Trim();
        return resultado.Length == 0 ? null : resultado;
    }

    /// <summary>
    /// Número (regra 1.3). Aceita '.' ou ',' como separador decimal, nunca de milhar.
    /// Mais de um separador, texto ou valor não finito vira null.
    /// </summary>
    public static double? Numero(string? bruto)
    {
        var t = Texto(bruto);
        if (t is null) return null;

        var separadores = 0;
        foreach (var c in t)
        {
            if (c is '.' or ',') separadores++;
        }
        if (separadores > 1) return null;

        var normalizado = t.Replace(',', '.');
        if (!double.TryParse(normalizado, NumberStyles.Float, CultureInfo.InvariantCulture, out var valor))
            return null;

        return double.IsFinite(valor) ? valor : null;
    }

    /// <summary>Data estrita yyyy-MM-dd (regra 1.4). Qualquer outra coisa, ou data inexistente, vira null.</summary>
    public static DateOnly? Data(string? bruto)
    {
        var t = Texto(bruto);
        if (t is null || !FormatoData.IsMatch(t)) return null;

        return DateOnly.TryParseExact(t, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var data)
            ? data
            : null;
    }

    private static bool EhMarca(char c) =>
        CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.EnclosingMark;
}
