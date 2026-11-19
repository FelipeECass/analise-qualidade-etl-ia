using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace claude_haiku_completo_r3.Limpeza;

/// <summary>Regras de limpeza e tipagem da seção 1 da especificação.</summary>
public static class Limpeza
{
    private static readonly Regex FormatoData = new("^[0-9]{4}-[0-9]{2}-[0-9]{2}$", RegexOptions.CultureInvariant);

    /// <summary>Remove espaços das pontas; vazio ou só espaços → null.</summary>
    public static string? Texto(string? bruto)
    {
        if (bruto is null) return null;
        var t = bruto.Trim();
        return t.Length == 0 ? null : t;
    }

    /// <summary>Texto limpo em maiúsculas.</summary>
    public static string? Maiusculas(string? bruto) => Texto(bruto)?.ToUpperInvariant();

    /// <summary>
    /// Número com '.' ou ',' como separador decimal. Qualquer valor que não seja
    /// número finito (inclui texto, NA, NaN, infinito, dois separadores) → null.
    /// </summary>
    public static double? Numero(string? bruto)
    {
        var t = Texto(bruto);
        if (t is null) return null;

        // ',' é sempre decimal; um valor com dois separadores (ex.: "14,70.0") falha no parse.
        t = t.Replace(',', '.');

        if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var valor))
            return null;

        return double.IsFinite(valor) ? valor : null;
    }

    /// <summary>
    /// Mesma regra de <see cref="Numero"/>, mas em decimal (exato para o texto de origem).
    /// Usado onde a aritmética precisa ser decimal exata, como o duplex e os empates de arredondamento.
    /// </summary>
    public static decimal? NumeroDecimal(string? bruto)
    {
        var t = Texto(bruto);
        if (t is null) return null;

        t = t.Replace(',', '.');

        return decimal.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var valor)
            ? valor
            : null;
    }

    /// <summary>Data estrita yyyy-MM-dd. Qualquer outra forma, ou data inexistente, → null.</summary>
    public static DateOnly? Data(string? bruto)
    {
        var t = Texto(bruto);
        if (t is null || !FormatoData.IsMatch(t)) return null;

        return DateOnly.TryParseExact(t, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var data)
            ? data
            : null;
    }

    /// <summary>
    /// Normalização de texto: remove acentos (decomposição Unicode e descarte das marcas),
    /// maiúsculas, troca o que não é letra ou dígito por espaço, colapsa espaços. Vazio → null.
    /// </summary>
    public static string? TextoNormalizado(string? bruto)
    {
        var t = Texto(bruto);
        if (t is null) return null;

        var decomposto = t.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposto.Length);

        foreach (var ch in decomposto)
        {
            var categoria = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (categoria is UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark
                or UnicodeCategory.EnclosingMark)
            {
                continue;
            }

            var maiusculo = char.ToUpperInvariant(ch);
            sb.Append(char.IsLetterOrDigit(maiusculo) ? maiusculo : ' ');
        }

        var colapsado = string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return colapsado.Length == 0 ? null : colapsado;
    }
}
