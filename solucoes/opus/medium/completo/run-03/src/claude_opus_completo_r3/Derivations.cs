using System.Globalization;

namespace Etl;

/// <summary>Colunas derivadas e validações (seções 4 e 5).</summary>
public static class Derivations
{
    public static readonly DateOnly Today = new(2026, 10, 15);

    // ---------- arredondamento ----------

    /// <summary>Converte double → decimal pela representação decimal mais curta (evita ruído binário).</summary>
    public static decimal? ToDecimal(double d)
    {
        var s = d.ToString("R", CultureInfo.InvariantCulture);
        return decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var m) ? m : null;
    }

    /// <summary>Arredonda com a metade para longe do zero.</summary>
    public static double Round(double value, int digits)
    {
        var m = ToDecimal(value);
        if (m is decimal dm)
            return (double)Math.Round(dm, digits, MidpointRounding.AwayFromZero);
        return Math.Round(value, digits, MidpointRounding.AwayFromZero);
    }

    // ---------- operadora ----------

    public static string Operadora(string? entidade)
    {
        if (entidade == null) return "OUTRAS";
        var w = entidade.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (w.Length == 0) return "OUTRAS";
        bool Has(string word) => Array.IndexOf(w, word) >= 0;

        if (Has("TELEFONICA") || Has("VIVO")) return "VIVO";
        if (Has("CLARO") || Has("EMBRATEL")) return "CLARO";
        if (w[0] == "TIM") return "TIM";
        if (w[0] == "OI" || Has("TELEMAR") || HasSequence(w, "BRASIL", "TELECOM")) return "OI";
        if (Has("ALGAR")) return "ALGAR";
        if (Has("SERCOMTEL")) return "SERCOMTEL";
        if (Has("BRISANET")) return "BRISANET";
        return "OUTRAS";
    }

    private static bool HasSequence(string[] w, string a, string b)
    {
        for (int i = 0; i + 1 < w.Length; i++)
            if (w[i] == a && w[i + 1] == b) return true;
        return false;
    }

    // ---------- geração ----------

    public static string Geracao(string? tecnologia)
    {
        if (tecnologia == null) return "N/D";
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

    // ---------- largura de banda ----------

    public static double? LarguraBandaKhz(string? designacao)
    {
        if (designacao == null) return null;
        var d = designacao.ToUpperInvariant();
        if (d.Length < 4) return null;
        int unitPos = -1;
        for (int i = 0; i < 4; i++)
        {
            char c = d[i];
            if (c is 'H' or 'K' or 'M' or 'G')
            {
                if (i == 0 || unitPos >= 0) return null;
                unitPos = i;
            }
            else if (c < '0' || c > '9') return null;
        }
        if (unitPos < 0) return null;

        string inteiro = d[..unitPos];
        string fracao = d.Substring(unitPos + 1, 3 - unitPos);
        var num = decimal.Parse(fracao.Length > 0 ? inteiro + "." + fracao : inteiro, CultureInfo.InvariantCulture);
        decimal fator = d[unitPos] switch
        {
            'H' => 0.001m,
            'K' => 1m,
            'M' => 1000m,
            _ => 1_000_000m,
        };
        return (double)(num * fator);
    }

    // ---------- demais derivadas ----------

    public static double? DuplexMhz(double? tx, double? rx)
    {
        if (tx is not double t || rx is not double r) return null;
        var mt = ToDecimal(t);
        var mr = ToDecimal(r);
        if (mt is decimal a && mr is decimal b)
            return (double)Math.Round(a - b, 3, MidpointRounding.AwayFromZero);
        return Round(t - r, 3);
    }

    public static double? PotenciaDbm(double? w)
    {
        if (w is not double p || p <= 0) return null;
        var v = 10.0 * Math.Log10(p * 1000.0);
        if (!double.IsFinite(v)) return null;
        return Round(v, 1);
    }

    public static bool Vencida(DateOnly? validade) => validade is DateOnly v && v < Today;

    public static double? AnosLicenciada(DateOnly? primeiro)
    {
        if (primeiro is not DateOnly p) return null;
        decimal dias = Today.DayNumber - p.DayNumber;
        return (double)Math.Round(dias / 365.25m, 1, MidpointRounding.AwayFromZero);
    }

    public static bool CoordenadaValida(double? lat, double? lon)
    {
        if (lat is not double la || lon is not double lo) return false;
        if (la == 0 && lo == 0) return false;
        return la >= -33.75 && la <= 5.28 && lo >= -73.99 && lo <= -28.83;
    }

    public static bool? AzimuteValido(double? az) => az is double a ? a >= 0 && a <= 360 : null;

    public static bool? ElevacaoValida(double? el) => el is double e ? e >= -90 && e <= 90 : null;
}
