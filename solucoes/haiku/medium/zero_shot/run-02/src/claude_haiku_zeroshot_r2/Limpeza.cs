using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ClaudeHaikuZeroshotR2;

/// <summary>Regras de limpeza, normalização e derivação (seções 1, 3 e 4 da especificação).</summary>
internal static class Limpeza
{
    /// <summary>Data de referência fixa ("hoje" da especificação).</summary>
    public static readonly DateOnly Hoje = new(2026, 10, 15);

    /// <summary>Texto limpo: remove espaços das extremidades; vazio vira null.</summary>
    public static string? Texto(string? valor)
    {
        if (valor is null) return null;
        var t = valor.Trim();
        return t.Length == 0 ? null : t;
    }

    /// <summary>Texto limpo em maiúsculas.</summary>
    public static string? Maiusculas(string? valor) => Texto(valor)?.ToUpperInvariant();

    /// <summary>
    /// Número: '.' e ',' são separador decimal. Qualquer texto que não seja um número decimal
    /// finito (NA, NaN, infinito, formato ambíguo como 1.234,5) vira null.
    /// </summary>
    public static decimal? Numero(string? valor)
    {
        var t = Texto(valor);
        if (t is null) return null;
        t = t.Replace(',', '.');
        return decimal.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    /// <summary>Data estrita yyyy-MM-dd; qualquer outra coisa vira null.</summary>
    public static DateOnly? Data(string? valor)
    {
        var t = Texto(valor);
        if (t is null) return null;
        if (!Regex.IsMatch(t, "^[0-9]{4}-[0-9]{2}-[0-9]{2}$")) return null;
        return DateOnly.TryParseExact(t, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;
    }

    /// <summary>
    /// Normalização de texto: remove acentos, maiúsculas, troca caractere que não é letra/dígito
    /// por espaço, colapsa espaços e remove as pontas. Resultado vazio vira null.
    /// </summary>
    public static string? TextoNormalizado(string? valor)
    {
        var t = Texto(valor);
        if (t is null) return null;

        var sb = new StringBuilder(t.Length);
        foreach (var r in t.Normalize(NormalizationForm.FormD).EnumerateRunes())
        {
            var cat = Rune.GetUnicodeCategory(r);
            if (cat is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
                continue;
            if (Rune.IsLetterOrDigit(r))
                sb.Append(r.ToString().ToUpperInvariant());
            else
                sb.Append(' ');
        }

        var resultado = string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return resultado.Length == 0 ? null : resultado;
    }

    /// <summary>Operadora a partir da entidade já normalizada (seção 4).</summary>
    public static string Operadora(string? entidadeNormalizada)
    {
        if (entidadeNormalizada is null) return "OUTRAS";
        var e = entidadeNormalizada;

        if (Regex.IsMatch(e, @"\b(TELEFONICA|VIVO)\b")) return "VIVO";
        if (Regex.IsMatch(e, @"\b(CLARO|EMBRATEL)\b")) return "CLARO";
        if (Regex.IsMatch(e, @"^TIM\b")) return "TIM";
        if (Regex.IsMatch(e, @"^OI\b") || Regex.IsMatch(e, @"\bTELEMAR\b") || Regex.IsMatch(e, @"\bBRASIL TELECOM\b")) return "OI";
        if (Regex.IsMatch(e, @"\bALGAR\b")) return "ALGAR";
        if (Regex.IsMatch(e, @"\bSERCOMTEL\b")) return "SERCOMTEL";
        if (Regex.IsMatch(e, @"\bBRISANET\b")) return "BRISANET";
        return "OUTRAS";
    }

    /// <summary>Geração a partir da tecnologia já em maiúsculas (seção 4).</summary>
    public static string Geracao(string? tecnologia)
    {
        if (string.IsNullOrEmpty(tecnologia)) return "N/D";
        return tecnologia switch
        {
            "GSM" or "CDMA" => "2G",
            "WCDMA" or "WDCMA" => "3G",
            "LTE" => "4G",
            "NR" => "5G",
            _ => "OUTRA",
        };
    }

    /// <summary>Largura de banda em kHz a partir dos quatro primeiros caracteres da designação.</summary>
    public static decimal? LarguraBandaKhz(string? designacao)
    {
        var t = Texto(designacao)?.ToUpperInvariant();
        if (t is null) return null;

        var s = t.Length > 4 ? t[..4] : t;

        var posicaoUnidade = -1;
        for (var i = 1; i < s.Length; i++)
        {
            if ("HKMG".IndexOf(s[i]) >= 0)
            {
                posicaoUnidade = i;
                break;
            }
        }
        if (posicaoUnidade < 0) return null;

        var parteInteira = s[..posicaoUnidade];
        var parteFracionaria = s[(posicaoUnidade + 1)..];
        if (!SoDigitos(parteInteira, minimo: 1) || !SoDigitos(parteFracionaria, minimo: 0)) return null;

        var valor = decimal.Parse(
            parteInteira + "." + (parteFracionaria.Length > 0 ? parteFracionaria : "0"),
            CultureInfo.InvariantCulture);

        var fator = s[posicaoUnidade] switch
        {
            'H' => 0.001m,
            'K' => 1m,
            'M' => 1000m,
            'G' => 1000000m,
            _ => throw new InvalidOperationException("unidade inesperada"),
        };
        return valor * fator;
    }

    private static bool SoDigitos(string s, int minimo)
    {
        if (s.Length < minimo) return false;
        foreach (var c in s)
        {
            if (c < '0' || c > '9') return false;
        }
        return true;
    }

    /// <summary>Potência em dBm: 10·log10(W·1000), arredondada a 1 casa; null se W ausente ou ≤ 0.</summary>
    public static double? PotenciaDbm(decimal? potenciaW)
    {
        if (potenciaW is null || potenciaW <= 0) return null;
        var dbm = 10.0 * Math.Log10((double)(potenciaW.Value * 1000m));
        return Math.Round(dbm, 1, MidpointRounding.AwayFromZero);
    }

    /// <summary>Coordenada válida: existe, não é (0,0) e está no retângulo do Brasil (inclusive).</summary>
    public static bool CoordenadaValida(decimal? latitude, decimal? longitude)
    {
        if (latitude is null || longitude is null) return false;
        if (latitude == 0 && longitude == 0) return false;
        return latitude >= -33.75m && latitude <= 5.28m
            && longitude >= -73.99m && longitude <= -28.83m;
    }

    /// <summary>Converte decimal para double preservando a representação decimal exata (parse correto).</summary>
    public static double? ParaReal(decimal? valor) =>
        valor is null ? null : double.Parse(valor.Value.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
}
