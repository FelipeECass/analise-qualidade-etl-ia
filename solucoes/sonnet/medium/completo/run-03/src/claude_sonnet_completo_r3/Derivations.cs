using System.Globalization;

static class Derivations
{
    public static readonly DateTime Today = new(2026, 10, 15);
    const string TodayText = "2026-10-15";

    public static double Round(double v, int digits)
    {
        var r = Math.Round(v, digits, MidpointRounding.AwayFromZero);
        return r == 0 ? 0.0 : r;
    }

    public static string Operadora(string? entidade)
    {
        if (entidade == null) return "OUTRAS";
        var tokens = entidade.Split(' ');
        bool Has(string w) => Array.IndexOf(tokens, w) >= 0;
        if (Has("TELEFONICA") || Has("VIVO")) return "VIVO";
        if (Has("CLARO") || Has("EMBRATEL")) return "CLARO";
        if (tokens[0] == "TIM") return "TIM";
        if (tokens[0] == "OI" || Has("TELEMAR") || (" " + entidade + " ").Contains(" BRASIL TELECOM ")) return "OI";
        if (Has("ALGAR")) return "ALGAR";
        if (Has("SERCOMTEL")) return "SERCOMTEL";
        if (Has("BRISANET")) return "BRISANET";
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
        var s = designacao.Substring(0, 4).ToUpperInvariant();
        int unit = -1;
        for (int i = 0; i < 4; i++)
        {
            char c = s[i];
            if (c is 'H' or 'K' or 'M' or 'G')
            {
                if (i == 0 || unit >= 0) return null;
                unit = i;
            }
            else if (c < '0' || c > '9') return null;
        }
        if (unit < 0) return null;
        var text = s.Substring(0, unit) + "." + s.Substring(unit + 1);
        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v)) return null;
        decimal factor = s[unit] switch { 'H' => 0.001m, 'K' => 1m, 'M' => 1000m, _ => 1000000m };
        return (double)(v * factor);
    }

    public static double? Duplex(double? tx, double? rx) => tx.HasValue && rx.HasValue ? Round(tx.Value - rx.Value, 3) : null;

    public static double? PotenciaDbm(double? w) => w.HasValue && w.Value > 0 ? Round(10 * Math.Log10(w.Value * 1000), 1) : null;

    public static bool Vencida(string? validade) => validade != null && string.CompareOrdinal(validade, TodayText) < 0;

    public static double? AnosLicenciada(string? primeira)
    {
        if (primeira == null) return null;
        var d = DateTime.ParseExact(primeira, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        return Round((Today - d).Days / 365.25, 1);
    }

    public static bool CoordenadaValida(double? lat, double? lon)
    {
        if (!lat.HasValue || !lon.HasValue) return false;
        if (lat.Value == 0 && lon.Value == 0) return false;
        return lat.Value >= -33.75 && lat.Value <= 5.28 && lon.Value >= -73.99 && lon.Value <= -28.83;
    }

    public static bool? Between(double? v, double lo, double hi) => v.HasValue ? v.Value >= lo && v.Value <= hi : null;
}
