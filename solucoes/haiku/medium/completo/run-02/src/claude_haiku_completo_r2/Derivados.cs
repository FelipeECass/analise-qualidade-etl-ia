using System.Globalization;
using System.Text.RegularExpressions;

namespace ClaudeHaikuCompletoR2;

/// <summary>Colunas derivadas e validações técnicas (seções 4 e 5 da especificação).</summary>
internal static class Derivados
{
    private static readonly (Regex Padrao, string Operadora)[] RegrasOperadora =
    [
        (new Regex(@"\b(?:TELEFONICA|VIVO)\b", RegexOptions.CultureInvariant), "VIVO"),
        (new Regex(@"\b(?:CLARO|EMBRATEL)\b", RegexOptions.CultureInvariant), "CLARO"),
        (new Regex(@"^TIM\b", RegexOptions.CultureInvariant), "TIM"),
        (new Regex(@"^OI\b|\bTELEMAR\b|\bBRASIL TELECOM\b", RegexOptions.CultureInvariant), "OI"),
        (new Regex(@"\bALGAR\b", RegexOptions.CultureInvariant), "ALGAR"),
        (new Regex(@"\bSERCOMTEL\b", RegexOptions.CultureInvariant), "SERCOMTEL"),
        (new Regex(@"\bBRISANET\b", RegexOptions.CultureInvariant), "BRISANET"),
    ];

    // Primeiros quatro caracteres da designação: parte inteira, unidade (H, K, M, G) e fração.
    private static readonly Regex FormatoDesignacao = new(@"^([0-9]+)([HKMG])([0-9]*)\z", RegexOptions.CultureInvariant);

    /// <summary>Operadora pela entidade já normalizada (regra 4).</summary>
    public static string Operadora(string? entidadeNormalizada)
    {
        if (entidadeNormalizada is null) return "OUTRAS";

        foreach (var (padrao, operadora) in RegrasOperadora)
        {
            if (padrao.IsMatch(entidadeNormalizada)) return operadora;
        }

        return "OUTRAS";
    }

    /// <summary>Geração a partir da tecnologia (regra 4).</summary>
    public static string Geracao(string? tecnologia)
    {
        if (string.IsNullOrEmpty(tecnologia)) return "N/D";

        return tecnologia.ToUpperInvariant() switch
        {
            "GSM" or "CDMA" => "2G",
            "WCDMA" or "WDCMA" => "3G",
            "LTE" => "4G",
            "NR" => "5G",
            _ => "OUTRA",
        };
    }

    /// <summary>Largura de banda em kHz a partir dos quatro primeiros caracteres da designação.</summary>
    public static double? LarguraBandaKhz(string? designacao)
    {
        var d = Limpeza.Maiusculas(designacao);
        if (d is null) return null;
        if (d.Length > 4) d = d[..4];

        var m = FormatoDesignacao.Match(d);
        if (!m.Success) return null;

        var inteiro = m.Groups[1].Value;
        var fracao = m.Groups[3].Value;
        var valor = decimal.Parse(
            inteiro + "." + (fracao.Length == 0 ? "0" : fracao),
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture);

        var fator = m.Groups[2].Value switch
        {
            "H" => 0.001m,
            "K" => 1m,
            "M" => 1000m,
            _ => 1000000m, // G
        };

        return (double)(valor * fator);
    }

    /// <summary>Espaçamento entre transmissão e recepção, com sinal, arredondado a 3 casas.</summary>
    public static double? Duplex(double? freqTx, double? freqRx)
    {
        if (freqTx is null || freqRx is null) return null;

        var diferenca = Convert.ToDecimal(freqTx.Value) - Convert.ToDecimal(freqRx.Value);
        return (double)Math.Round(diferenca, 3, MidpointRounding.AwayFromZero);
    }

    /// <summary>Potência em dBm, arredondada a 1 casa. Nula se a potência não existe ou é ≤ 0.</summary>
    public static double? PotenciaDbm(double? potenciaW)
    {
        if (potenciaW is null || potenciaW <= 0) return null;

        var dbm = 10.0 * Math.Log10(potenciaW.Value * 1000.0);
        return Math.Round(dbm, 1, MidpointRounding.AwayFromZero);
    }

    /// <summary>Anos desde o primeiro licenciamento, dias ÷ 365,25, arredondado a 1 casa.</summary>
    public static double? AnosLicenciada(DateOnly? primeiroLicenciamento)
    {
        if (primeiroLicenciamento is null) return null;

        var dias = Configuracao.DataReferencia.DayNumber - primeiroLicenciamento.Value.DayNumber;
        return Math.Round(dias / 365.25, 1, MidpointRounding.AwayFromZero);
    }

    public static bool Vencida(DateOnly? validade) =>
        validade is not null && validade.Value < Configuracao.DataReferencia;

    public static bool CoordenadaValida(double? latitude, double? longitude)
    {
        if (latitude is null || longitude is null) return false;
        if (latitude == 0 && longitude == 0) return false;

        return latitude >= Configuracao.LatitudeMin && latitude <= Configuracao.LatitudeMax
            && longitude >= Configuracao.LongitudeMin && longitude <= Configuracao.LongitudeMax;
    }

    /// <summary>1 se 0 ≤ azimute ≤ 360; 0 se fora; null se o azimute não existe.</summary>
    public static int? AzimuteValido(double? azimute)
    {
        if (azimute is null) return null;
        return azimute >= 0 && azimute <= 360 ? 1 : 0;
    }

    /// <summary>1 se −90 ≤ ângulo ≤ 90; 0 se fora; null se o ângulo não existe.</summary>
    public static int? ElevacaoValida(double? anguloElevacao)
    {
        if (anguloElevacao is null) return null;
        return anguloElevacao >= -90 && anguloElevacao <= 90 ? 1 : 0;
    }
}
