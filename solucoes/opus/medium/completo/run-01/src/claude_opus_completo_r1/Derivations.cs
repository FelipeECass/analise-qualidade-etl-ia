using System.Text.RegularExpressions;

namespace AnatelEtl;

/// <summary>Regras das seções 4 e 5 (colunas derivadas e validações).</summary>
public static partial class Derivations
{
    public static readonly DateOnly Today = new(2026, 10, 15);

    /// <summary>Arredondamento com metade para longe do zero (via decimal, sem ruído binário).</summary>
    public static double Round(double value, int digits)
    {
        if (Math.Abs(value) >= 7.9e27) return Math.Round(value, digits, MidpointRounding.AwayFromZero);
        decimal d = (decimal)value; // 15 dígitos significativos
        return (double)Math.Round(d, digits, MidpointRounding.AwayFromZero);
    }

    [GeneratedRegex(@"\b(?:TELEFONICA|VIVO)\b", RegexOptions.CultureInvariant)] private static partial Regex RxVivo();
    [GeneratedRegex(@"\b(?:CLARO|EMBRATEL)\b", RegexOptions.CultureInvariant)] private static partial Regex RxClaro();
    [GeneratedRegex(@"^TIM\b", RegexOptions.CultureInvariant)] private static partial Regex RxTim();
    [GeneratedRegex(@"^OI\b|\bTELEMAR\b|\bBRASIL TELECOM\b", RegexOptions.CultureInvariant)] private static partial Regex RxOi();
    [GeneratedRegex(@"\bALGAR\b", RegexOptions.CultureInvariant)] private static partial Regex RxAlgar();
    [GeneratedRegex(@"\bSERCOMTEL\b", RegexOptions.CultureInvariant)] private static partial Regex RxSercomtel();
    [GeneratedRegex(@"\bBRISANET\b", RegexOptions.CultureInvariant)] private static partial Regex RxBrisanet();

    public static string Operadora(string? entidade)
    {
        if (entidade is null) return "OUTRAS";
        if (RxVivo().IsMatch(entidade)) return "VIVO";
        if (RxClaro().IsMatch(entidade)) return "CLARO";
        if (RxTim().IsMatch(entidade)) return "TIM";
        if (RxOi().IsMatch(entidade)) return "OI";
        if (RxAlgar().IsMatch(entidade)) return "ALGAR";
        if (RxSercomtel().IsMatch(entidade)) return "SERCOMTEL";
        if (RxBrisanet().IsMatch(entidade)) return "BRISANET";
        return "OUTRAS";
    }

    public static string Geracao(string? tecnologia)
    {
        if (tecnologia is null) return "N/D";
        return tecnologia.Trim().ToUpperInvariant() switch
        {
            "" => "N/D",
            "GSM" or "CDMA" => "2G",
            "WCDMA" or "WDCMA" => "3G",
            "LTE" => "4G",
            "NR" => "5G",
            _ => "OUTRA",
        };
    }

    /// <summary>Largura de banda em kHz a partir dos 4 primeiros caracteres da designação.</summary>
    public static double? LarguraBandaKhz(string? designacao)
    {
        if (designacao is null) return null;
        var d = designacao.ToUpperInvariant();
        var p = d.Length > 4 ? d[..4] : d;
        int unitPos = -1;
        decimal factor = 0;
        for (int i = 0; i < p.Length; i++)
        {
            char c = p[i];
            decimal f = c switch { 'H' => 0.001m, 'K' => 1m, 'M' => 1000m, 'G' => 1000000m, _ => -1m };
            if (f > 0)
            {
                if (unitPos >= 0 || i == 0) return null;
                unitPos = i;
                factor = f;
            }
            else if (c < '0' || c > '9') return null;
        }
        if (unitPos < 0) return null;
        var intPart = p[..unitPos];
        var fracPart = p[(unitPos + 1)..];
        decimal value = decimal.Parse(intPart, System.Globalization.CultureInfo.InvariantCulture);
        if (fracPart.Length > 0)
            value += decimal.Parse(fracPart, System.Globalization.CultureInfo.InvariantCulture) / Pow10(fracPart.Length);
        return (double)(value * factor);
    }

    private static decimal Pow10(int n) { decimal r = 1; for (int i = 0; i < n; i++) r *= 10; return r; }

    public static double? Duplex(double? tx, double? rx)
    {
        if (tx is null || rx is null) return null;
        try { return (double)Math.Round((decimal)tx.Value - (decimal)rx.Value, 3, MidpointRounding.AwayFromZero); }
        catch (OverflowException) { return Round(tx.Value - rx.Value, 3); }
    }

    public static double? PotenciaDbm(double? w)
    {
        if (w is null || w.Value <= 0) return null;
        var v = 10.0 * Math.Log10(w.Value * 1000.0);
        return double.IsFinite(v) ? Round(v, 1) : null;
    }

    public static bool Vencida(DateOnly? validade) => validade is not null && validade.Value < Today;

    public static double? AnosLicenciada(DateOnly? primeiro)
    {
        if (primeiro is null) return null;
        int days = Today.DayNumber - primeiro.Value.DayNumber;
        return (double)Math.Round(days / 365.25m, 1, MidpointRounding.AwayFromZero);
    }

    public static bool CoordenadaValida(double? lat, double? lon)
    {
        if (lat is null || lon is null) return false;
        double a = lat.Value, o = lon.Value;
        if (a == 0 && o == 0) return false;
        return a >= -33.75 && a <= 5.28 && o >= -73.99 && o <= -28.83;
    }

    public static bool? AzimuteValido(double? az) => az is null ? null : az.Value >= 0 && az.Value <= 360;

    public static bool? ElevacaoValida(double? el) => el is null ? null : el.Value >= -90 && el.Value <= 90;
}
