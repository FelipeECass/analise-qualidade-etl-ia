namespace Etl;

public static class Derivations
{
    public static readonly DateTime Today = new(2026, 10, 15);

    private static double R(double v, int n) => Math.Round(v, n, MidpointRounding.AwayFromZero);

    private static bool HasWord(string[] t, string w) => Array.IndexOf(t, w) >= 0;

    public static string Operadora(string? entidade)
    {
        if (entidade == null) return "OUTRAS";
        var t = entidade.Split(' ');
        if (HasWord(t, "TELEFONICA") || HasWord(t, "VIVO")) return "VIVO";
        if (HasWord(t, "CLARO") || HasWord(t, "EMBRATEL")) return "CLARO";
        if (t[0] == "TIM") return "TIM";
        if (t[0] == "OI" || HasWord(t, "TELEMAR")) return "OI";
        for (int i = 0; i + 1 < t.Length; i++)
            if (t[i] == "BRASIL" && t[i + 1] == "TELECOM") return "OI";
        if (HasWord(t, "ALGAR")) return "ALGAR";
        if (HasWord(t, "SERCOMTEL")) return "SERCOMTEL";
        if (HasWord(t, "BRISANET")) return "BRISANET";
        return "OUTRAS";
    }

    public static string Geracao(string? tecnologia) => tecnologia switch
    {
        null => "N/D",
        "GSM" or "CDMA" => "2G",
        "WCDMA" or "WDCMA" => "3G",
        "LTE" => "4G",
        "NR" => "5G",
        _ => "OUTRA",
    };

    public static double? LarguraBandaKhz(string? designacao)
    {
        if (designacao == null || designacao.Length < 4) return null;
        int unitPos = -1;
        for (int i = 0; i < 4; i++)
        {
            char c = designacao[i];
            if (c is 'H' or 'K' or 'M' or 'G')
            {
                if (i == 0 || unitPos >= 0) return null;
                unitPos = i;
            }
            else if (c < '0' || c > '9') return null;
        }
        if (unitPos < 0) return null;
        decimal intPart = decimal.Parse(designacao.AsSpan(0, unitPos));
        decimal frac = 0;
        int fl = 3 - unitPos;
        if (fl > 0)
        {
            frac = decimal.Parse(designacao.AsSpan(unitPos + 1, fl));
            for (int i = 0; i < fl; i++) frac /= 10m;
        }
        decimal factor = designacao[unitPos] switch { 'H' => 0.001m, 'K' => 1m, 'M' => 1000m, _ => 1000000m };
        return (double)((intPart + frac) * factor);
    }

    public static double? Duplex(double? tx, double? rx)
    {
        if (tx == null || rx == null) return null;
        try { return (double)Math.Round((decimal)tx.Value - (decimal)rx.Value, 3, MidpointRounding.AwayFromZero); }
        catch (OverflowException) { return R(tx.Value - rx.Value, 3); }
    }

    public static double? PotenciaDbm(double? w)
    {
        if (w == null || w <= 0) return null;
        double v = 10 * Math.Log10(w.Value * 1000);
        return double.IsFinite(v) ? R(v, 1) : null;
    }

    public static bool Vencida(DateTime? validade) => validade != null && validade.Value < Today;

    public static double? AnosLicenciada(DateTime? primeiro)
        => primeiro == null ? null : R((Today - primeiro.Value).Days / 365.25, 1);

    public static bool CoordenadaValida(double? lat, double? lon)
    {
        if (lat == null || lon == null) return false;
        if (lat == 0 && lon == 0) return false;
        return lat >= -33.75 && lat <= 5.28 && lon >= -73.99 && lon <= -28.83;
    }

    public static bool? Range(double? v, double lo, double hi) => v == null ? null : v >= lo && v <= hi;
}
