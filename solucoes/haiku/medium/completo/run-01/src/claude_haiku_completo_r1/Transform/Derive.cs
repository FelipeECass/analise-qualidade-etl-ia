using System.Text.RegularExpressions;

namespace Etl.Transform;

/// <summary>Colunas derivadas e validações (seções 4 e 5 da especificação).</summary>
public static class Derive
{
    /// <summary>Data de referência fixa ("Hoje").</summary>
    public static readonly DateOnly Hoje = new(2026, 10, 15);

    private static readonly Regex VivoRx = new(@"\b(TELEFONICA|VIVO)\b", RegexOptions.CultureInvariant);
    private static readonly Regex ClaroRx = new(@"\b(CLARO|EMBRATEL)\b", RegexOptions.CultureInvariant);
    private static readonly Regex TimRx = new(@"^TIM\b", RegexOptions.CultureInvariant);
    private static readonly Regex OiRx = new(@"^OI\b|\bTELEMAR\b", RegexOptions.CultureInvariant);
    private static readonly Regex AlgarRx = new(@"\bALGAR\b", RegexOptions.CultureInvariant);
    private static readonly Regex SercomtelRx = new(@"\bSERCOMTEL\b", RegexOptions.CultureInvariant);
    private static readonly Regex BrisanetRx = new(@"\bBRISANET\b", RegexOptions.CultureInvariant);

    /// <summary>Operadora a partir da entidade já normalizada (seção 4).</summary>
    public static string Operadora(string? entidadeNormalizada)
    {
        if (entidadeNormalizada is null) return "OUTRAS";
        var e = entidadeNormalizada;

        if (VivoRx.IsMatch(e)) return "VIVO";
        if (ClaroRx.IsMatch(e)) return "CLARO";
        if (TimRx.IsMatch(e)) return "TIM";
        if (OiRx.IsMatch(e) || e.Contains("BRASIL TELECOM", StringComparison.Ordinal)) return "OI";
        if (AlgarRx.IsMatch(e)) return "ALGAR";
        if (SercomtelRx.IsMatch(e)) return "SERCOMTEL";
        if (BrisanetRx.IsMatch(e)) return "BRISANET";
        return "OUTRAS";
    }

    /// <summary>Geração a partir da tecnologia (já em maiúsculas).</summary>
    public static string Geracao(string? tecnologia) => tecnologia switch
    {
        null => "N/D",
        "GSM" or "CDMA" => "2G",
        "WCDMA" or "WDCMA" => "3G",
        "LTE" => "4G",
        "NR" => "5G",
        _ => "OUTRA",
    };

    /// <summary>
    /// Largura de banda em kHz a partir dos 4 primeiros caracteres da designação.
    /// Exatamente uma unidade (H, K, M, G) fora da primeira posição; dígitos antes e depois.
    /// </summary>
    public static double? LarguraBandaKhz(string? designacao)
    {
        var d = Clean.Text(designacao);
        if (d is null) return null;

        var w = d.ToUpperInvariant();
        if (w.Length > 4) w = w[..4];

        var unidadeIdx = -1;
        for (var i = 0; i < w.Length; i++)
        {
            if (w[i] is not ('H' or 'K' or 'M' or 'G')) continue;
            if (unidadeIdx >= 0) return null;
            unidadeIdx = i;
        }
        if (unidadeIdx < 1) return null;

        var inteiro = w[..unidadeIdx];
        var fracao = w[(unidadeIdx + 1)..];
        if (!IsDigits(inteiro) || !IsDigits(fracao)) return null;

        var numero = decimal.Parse(fracao.Length > 0 ? $"{inteiro}.{fracao}" : inteiro,
            System.Globalization.CultureInfo.InvariantCulture);

        var fator = w[unidadeIdx] switch
        {
            'H' => 0.001m,
            'K' => 1m,
            'M' => 1000m,
            _ => 1000000m,
        };

        return Clean.ToDouble(numero * fator);
    }

    /// <summary>tx − rx com sinal, arredondado a 3 casas (metade para longe de zero).</summary>
    public static double? DuplexMhz(decimal? tx, decimal? rx)
    {
        if (tx is null || rx is null) return null;
        var v = Math.Round(tx.Value - rx.Value, 3, MidpointRounding.AwayFromZero);
        return Clean.ToDouble(v);
    }

    /// <summary>10·log10(W·1000), 1 casa; null se potência ausente ou ≤ 0.</summary>
    public static double? PotenciaDbm(double? potenciaW)
    {
        if (potenciaW is null || potenciaW <= 0) return null;
        var dbm = 10.0 * Math.Log10(potenciaW.Value * 1000.0);
        return Clean.ToDouble(Math.Round(Convert.ToDecimal(dbm), 1, MidpointRounding.AwayFromZero));
    }

    public static int Vencida(DateOnly? validade) => validade is { } v && v < Hoje ? 1 : 0;

    /// <summary>(Hoje − primeiro licenciamento) em dias ÷ 365,25, 1 casa.</summary>
    public static double? AnosLicenciada(DateOnly? primeiroLicenciamento)
    {
        if (primeiroLicenciamento is not { } p) return null;
        var dias = Hoje.DayNumber - p.DayNumber;
        var anos = dias / 365.25m;
        return Clean.ToDouble(Math.Round(anos, 1, MidpointRounding.AwayFromZero));
    }

    /// <summary>1 se lat/lon existem, não são ambas 0 e estão no retângulo do Brasil; senão 0.</summary>
    public static int CoordenadaValida(double? latitude, double? longitude)
    {
        if (latitude is not { } lat || longitude is not { } lon) return 0;
        if (lat == 0 && lon == 0) return 0;
        var ok = lat >= -33.75 && lat <= 5.28 && lon >= -73.99 && lon <= -28.83;
        return ok ? 1 : 0;
    }

    /// <summary>NULL se azimute nulo; 1 se 0 ≤ az ≤ 360; senão 0.</summary>
    public static int? AzimuteValido(double? azimute)
    {
        if (azimute is not { } v) return null;
        return v >= 0 && v <= 360 ? 1 : 0;
    }

    /// <summary>NULL se ângulo nulo; 1 se −90 ≤ v ≤ 90; senão 0.</summary>
    public static int? ElevacaoValida(double? elevacao)
    {
        if (elevacao is not { } v) return null;
        return v >= -90 && v <= 90 ? 1 : 0;
    }

    private static bool IsDigits(string s)
    {
        foreach (var ch in s)
        {
            if (ch < '0' || ch > '9') return false;
        }
        return true;
    }
}
