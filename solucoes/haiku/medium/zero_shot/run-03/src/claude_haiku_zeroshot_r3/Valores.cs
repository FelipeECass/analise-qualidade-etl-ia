using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ClaudeHaikuZeroshotR3;

/// <summary>Limpeza de valores brutos (seção 1 da especificação).</summary>
internal static class Limpeza
{
    // Número finito em notação decimal, com '.' ou ',' como separador, opcionalmente com expoente.
    private static readonly Regex NumeroRx =
        new(@"^[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?$", RegexOptions.CultureInvariant);

    private static readonly Regex DataRx =
        new(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.CultureInvariant);

    /// <summary>Remove espaços das extremidades; vazio vira NULL.</summary>
    public static string? Texto(string? valor)
    {
        if (valor is null) return null;
        var t = valor.Trim();
        return t.Length == 0 ? null : t;
    }

    /// <summary>Texto limpo em maiúsculas.</summary>
    public static string? Maiusculas(string? valor)
        => Texto(valor)?.ToUpperInvariant();

    /// <summary>
    /// Número decimal. Aceita '.' ou ',' como separador decimal (nunca de milhar).
    /// Valores com os dois separadores, texto, NaN ou infinito viram NULL.
    /// </summary>
    public static decimal? Numero(string? valor)
    {
        var t = Texto(valor);
        if (t is null) return null;

        bool temPonto = t.Contains('.');
        bool temVirgula = t.Contains(',');
        if (temPonto && temVirgula) return null;
        if (temVirgula) t = t.Replace(',', '.');

        if (!NumeroRx.IsMatch(t)) return null;

        if (decimal.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
        {
            return d;
        }
        return null;
    }

    /// <summary>Data estrita yyyy-MM-dd; qualquer outra coisa (inclusive data inexistente) vira NULL.</summary>
    public static DateOnly? Data(string? valor)
    {
        var t = Texto(valor);
        if (t is null || !DataRx.IsMatch(t)) return null;

        if (DateOnly.TryParseExact(t, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            return d;
        }
        return null;
    }

    /// <summary>
    /// Normalização de texto (regra 1.6): remove acentos, maiúsculas, troca tudo que não
    /// é letra ou dígito por espaço, colapsa espaços e remove as pontas.
    /// </summary>
    public static string? NormalizaTexto(string? valor)
    {
        var t = Texto(valor);
        if (t is null) return null;

        var decomposto = t.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposto.Length);

        foreach (var rune in decomposto.EnumerateRunes())
        {
            var categoria = Rune.GetUnicodeCategory(rune);
            if (categoria is UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark
                or UnicodeCategory.EnclosingMark)
            {
                continue;
            }

            var maiuscula = Rune.ToUpperInvariant(rune);
            if (Rune.IsLetterOrDigit(maiuscula))
            {
                sb.Append(maiuscula.ToString());
            }
            else
            {
                sb.Append(' ');
            }
        }

        var resultado = string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return resultado.Length == 0 ? null : resultado;
    }
}

/// <summary>Colunas derivadas e validações técnicas (seções 4 e 5).</summary>
internal static class Derivados
{
    public static readonly DateOnly Hoje = new(2026, 10, 15);

    private static readonly (Regex Rx, string Operadora)[] RegrasOperadora =
    {
        (new Regex(@"\b(TELEFONICA|VIVO)\b", RegexOptions.CultureInvariant), "VIVO"),
        (new Regex(@"\b(CLARO|EMBRATEL)\b", RegexOptions.CultureInvariant), "CLARO"),
        (new Regex(@"^TIM\b", RegexOptions.CultureInvariant), "TIM"),
        (new Regex(@"^OI\b|\bTELEMAR\b|\bBRASIL TELECOM\b", RegexOptions.CultureInvariant), "OI"),
        (new Regex(@"\bALGAR\b", RegexOptions.CultureInvariant), "ALGAR"),
        (new Regex(@"\bSERCOMTEL\b", RegexOptions.CultureInvariant), "SERCOMTEL"),
        (new Regex(@"\bBRISANET\b", RegexOptions.CultureInvariant), "BRISANET"),
    };

    /// <summary>Operadora a partir da entidade já normalizada.</summary>
    public static string Operadora(string? entidadeNormalizada)
    {
        if (entidadeNormalizada is null) return "OUTRAS";

        foreach (var (rx, operadora) in RegrasOperadora)
        {
            if (rx.IsMatch(entidadeNormalizada)) return operadora;
        }
        return "OUTRAS";
    }

    /// <summary>Geração a partir da tecnologia já em maiúsculas.</summary>
    public static string Geracao(string? tecnologiaMaiuscula) => tecnologiaMaiuscula switch
    {
        null => "N/D",
        "GSM" or "CDMA" => "2G",
        "WCDMA" or "WDCMA" => "3G",
        "LTE" => "4G",
        "NR" => "5G",
        _ => "OUTRA",
    };

    /// <summary>Largura de banda em kHz a partir dos quatro primeiros caracteres da designação.</summary>
    public static double? LarguraBandaKhz(string? designacaoMaiuscula)
    {
        if (designacaoMaiuscula is null) return null;

        var janela = designacaoMaiuscula.Length > 4 ? designacaoMaiuscula[..4] : designacaoMaiuscula;

        int posUnidade = -1;
        char unidade = '\0';
        for (int i = 0; i < janela.Length; i++)
        {
            char c = janela[i];
            if (char.IsAsciiDigit(c)) continue;

            // Exatamente uma letra de unidade, fora da primeira posição, e nenhuma outra letra.
            if (posUnidade >= 0 || i == 0 || "HKMG".IndexOf(c) < 0) return null;
            posUnidade = i;
            unidade = c;
        }

        if (posUnidade < 0) return null;

        var parteInteira = janela[..posUnidade];
        var fracao = janela[(posUnidade + 1)..];
        if (fracao.Length == 0) fracao = "0";

        var valor = decimal.Parse(parteInteira + "." + fracao, CultureInfo.InvariantCulture);
        decimal fator = unidade switch
        {
            'H' => 0.001m,
            'K' => 1m,
            'M' => 1000m,
            'G' => 1000000m,
            _ => throw new InvalidOperationException("unidade inesperada"),
        };

        return (double)(valor * fator);
    }

    public static double? DuplexMhz(decimal? tx, decimal? rx)
    {
        if (tx is null || rx is null) return null;
        return (double)Math.Round(tx.Value - rx.Value, 3, MidpointRounding.AwayFromZero);
    }

    public static double? PotenciaDbm(decimal? potenciaW)
    {
        if (potenciaW is null || potenciaW.Value <= 0) return null;

        double db = 10.0 * Math.Log10((double)potenciaW.Value * 1000.0);
        return (double)Math.Round((decimal)db, 1, MidpointRounding.AwayFromZero);
    }

    public static int Vencida(DateOnly? validade)
        => validade is not null && validade.Value < Hoje ? 1 : 0;

    public static double? AnosLicenciada(DateOnly? primeiroLicenciamento)
    {
        if (primeiroLicenciamento is null) return null;

        decimal dias = Hoje.DayNumber - primeiroLicenciamento.Value.DayNumber;
        return (double)Math.Round(dias / 365.25m, 1, MidpointRounding.AwayFromZero);
    }

    public static int CoordenadaValida(decimal? latitude, decimal? longitude)
    {
        if (latitude is null || longitude is null) return 0;

        decimal lat = latitude.Value;
        decimal lon = longitude.Value;
        if (lat == 0 && lon == 0) return 0;

        bool dentro = lat >= -33.75m && lat <= 5.28m && lon >= -73.99m && lon <= -28.83m;
        return dentro ? 1 : 0;
    }

    public static int? AzimuteValido(decimal? azimute)
    {
        if (azimute is null) return null;
        return azimute.Value >= 0 && azimute.Value <= 360 ? 1 : 0;
    }

    public static int? ElevacaoValida(decimal? elevacao)
    {
        if (elevacao is null) return null;
        return elevacao.Value >= -90 && elevacao.Value <= 90 ? 1 : 0;
    }
}
