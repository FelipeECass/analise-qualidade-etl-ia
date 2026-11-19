namespace Etl;

public static class Derivations
{
    public static readonly DateTime Today = new(2026, 10, 15);

    public static double Round(double v, int digits) => Math.Round(v, digits, MidpointRounding.AwayFromZero);

    public static string Operadora(string? entidadeNorm)
    {
        if (entidadeNorm == null) return "OUTRAS";
        var tk = entidadeNorm.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool Has(string w) => Array.IndexOf(tk, w) >= 0;
        if (Has("TELEFONICA") || Has("VIVO")) return "VIVO";
        if (Has("CLARO") || Has("EMBRATEL")) return "CLARO";
        if (tk.Length > 0 && tk[0] == "TIM") return "TIM";
        bool brasilTelecom = false;
        for (int i = 0; i + 1 < tk.Length; i++)
            if (tk[i] == "BRASIL" && tk[i + 1] == "TELECOM") { brasilTelecom = true; break; }
        if ((tk.Length > 0 && tk[0] == "OI") || Has("TELEMAR") || brasilTelecom) return "OI";
        if (Has("ALGAR")) return "ALGAR";
        if (Has("SERCOMTEL")) return "SERCOMTEL";
        if (Has("BRISANET")) return "BRISANET";
        return "OUTRAS";
    }

    public static string Geracao(string? tecnologia)
    {
        if (tecnologia == null) return "N/D";
        switch (tecnologia.ToUpperInvariant())
        {
            case "GSM": case "CDMA": return "2G";
            case "WCDMA": case "WDCMA": return "3G";
            case "LTE": return "4G";
            case "NR": return "5G";
            default: return "OUTRA";
        }
    }

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
                if (unit >= 0) return null;
                unit = i;
            }
            else if (c < '0' || c > '9') return null;
        }
        if (unit <= 0) return null;
        var ip = s.Substring(0, unit);
        var fp = s.Substring(unit + 1);
        decimal v = decimal.Parse(fp.Length == 0 ? ip : ip + "." + fp, System.Globalization.CultureInfo.InvariantCulture);
        decimal f = s[unit] switch { 'H' => 0.001m, 'K' => 1m, 'M' => 1000m, _ => 1000000m };
        return (double)(v * f);
    }

    public static double? Duplex(double? tx, double? rx) => tx.HasValue && rx.HasValue ? Round(tx.Value - rx.Value, 3) : null;

    public static double? PotenciaDbm(double? w) => w.HasValue && w.Value > 0 ? Round(10 * Math.Log10(w.Value * 1000), 1) : null;

    public static bool Vencida(DateTime? validade) => validade.HasValue && validade.Value < Today;

    public static double? AnosLicenciada(DateTime? primeira) =>
        primeira.HasValue ? Round((Today - primeira.Value).Days / 365.25, 1) : null;

    public static bool CoordenadaValida(double? lat, double? lon) =>
        lat.HasValue && lon.HasValue && !(lat.Value == 0 && lon.Value == 0)
        && lat.Value >= -33.75 && lat.Value <= 5.28 && lon.Value >= -73.99 && lon.Value <= -28.83;

    public static int? Range(double? v, double min, double max) => v.HasValue ? (v.Value >= min && v.Value <= max ? 1 : 0) : null;
}
