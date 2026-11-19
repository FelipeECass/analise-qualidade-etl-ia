using System.Globalization;
using System.Text.RegularExpressions;

namespace AnatelEtl;

/// <summary>Regras das seções 4 e 5 (colunas derivadas e validações).</summary>
public static partial class Derivations
{
    public static readonly DateOnly Today = new(2026, 10, 15);

    /// <summary>Arredondamento com metade para longe do zero, sobre a representação decimal do double.</summary>
    public static double Round(double value, int digits)
    {
        if (!double.IsFinite(value) || Math.Abs(value) > 1e15) return value;
        var dec = decimal.Parse(value.ToString("R", CultureInfo.InvariantCulture),
            NumberStyles.Float, CultureInfo.InvariantCulture);
        return (double)Math.Round(dec, digits, MidpointRounding.AwayFromZero);
    }

    [GeneratedRegex(@"\b(TELEFONICA|VIVO)\b")] private static partial Regex RxVivo();
    [GeneratedRegex(@"\b(CLARO|EMBRATEL)\b")] private static partial Regex RxClaro();
    [GeneratedRegex(@"^TIM\b")] private static partial Regex RxTim();
    [GeneratedRegex(@"^OI\b|\bTELEMAR\b|\bBRASIL TELECOM\b")] private static partial Regex RxOi();
    [GeneratedRegex(@"\bALGAR\b")] private static partial Regex RxAlgar();
    [GeneratedRegex(@"\bSERCOMTEL\b")] private static partial Regex RxSercomtel();
    [GeneratedRegex(@"\bBRISANET\b")] private static partial Regex RxBrisanet();

    public static string Operadora(string? entidadeNormalizada)
    {
        var e = entidadeNormalizada;
        if (e == null) return "OUTRAS";
        if (RxVivo().IsMatch(e)) return "VIVO";
        if (RxClaro().IsMatch(e)) return "CLARO";
        if (RxTim().IsMatch(e)) return "TIM";
        if (RxOi().IsMatch(e)) return "OI";
        if (RxAlgar().IsMatch(e)) return "ALGAR";
        if (RxSercomtel().IsMatch(e)) return "SERCOMTEL";
        if (RxBrisanet().IsMatch(e)) return "BRISANET";
        return "OUTRAS";
    }

    public static string Geracao(string? tecnologia)
    {
        if (tecnologia == null) return "N/D";
        return tecnologia.ToUpperInvariant() switch
        {
            "GSM" or "CDMA" => "2G",
            "WCDMA" or "WDCMA" => "3G",
            "LTE" => "4G",
            "NR" => "5G",
            _ => "OUTRA",
        };
    }

    public static double? LarguraBandaKhz(string? designacao)
    {
        if (designacao == null) return null;
        var s = designacao.ToUpperInvariant();
        if (s.Length > 4) s = s[..4];
        int unitPos = -1;
        decimal factor = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            decimal f = c switch { 'H' => 0.001m, 'K' => 1m, 'M' => 1000m, 'G' => 1_000_000m, _ => -1m };
            if (f > 0)
            {
                if (unitPos >= 0 || i == 0) return null;
                unitPos = i;
                factor = f;
            }
            else if (!char.IsAsciiDigit(c)) return null;
        }
        if (unitPos < 0) return null;
        var integer = s[..unitPos];
        var fraction = s[(unitPos + 1)..];
        var number = decimal.Parse(fraction.Length > 0 ? integer + "." + fraction : integer, CultureInfo.InvariantCulture);
        return (double)(number * factor);
    }

    public static double? Duplex(double? tx, double? rx) =>
        tx.HasValue && rx.HasValue ? Round(tx.Value - rx.Value, 3) : null;

    public static double? PotenciaDbm(double? w) =>
        w.HasValue && w.Value > 0 ? Round(10 * Math.Log10(w.Value * 1000), 1) : null;

    public static bool Vencida(DateOnly? validade) => validade.HasValue && validade.Value < Today;

    public static double? AnosLicenciada(DateOnly? primeiro) =>
        primeiro.HasValue ? Round((Today.DayNumber - primeiro.Value.DayNumber) / 365.25, 1) : null;

    public static bool CoordenadaValida(double? lat, double? lon)
    {
        if (!lat.HasValue || !lon.HasValue) return false;
        double la = lat.Value, lo = lon.Value;
        if (la == 0 && lo == 0) return false;
        return la >= -33.75 && la <= 5.28 && lo >= -73.99 && lo <= -28.83;
    }

    public static bool? AzimuteValido(double? az) => az.HasValue ? az.Value >= 0 && az.Value <= 360 : null;

    public static bool? ElevacaoValida(double? el) => el.HasValue ? el.Value >= -90 && el.Value <= 90 : null;
}
