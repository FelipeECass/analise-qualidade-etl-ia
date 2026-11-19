namespace Etl;

/// <summary>Colunas derivadas (seção 4) e validações (seção 5).</summary>
public static class Derivations
{
    public static readonly DateOnly Today = new(2026, 10, 15);

    private static bool HasWord(string[] tokens, string word) => Array.IndexOf(tokens, word) >= 0;

    /// <param name="entidade">Entidade já normalizada (ou null).</param>
    public static string Operadora(string? entidade)
    {
        if (entidade is null) return "OUTRAS";
        var t = entidade.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (t.Length == 0) return "OUTRAS";
        if (HasWord(t, "TELEFONICA") || HasWord(t, "VIVO")) return "VIVO";
        if (HasWord(t, "CLARO") || HasWord(t, "EMBRATEL")) return "CLARO";
        if (t[0] == "TIM") return "TIM";
        if (t[0] == "OI" || HasWord(t, "TELEMAR") || (" " + entidade + " ").Contains(" BRASIL TELECOM ")) return "OI";
        if (HasWord(t, "ALGAR")) return "ALGAR";
        if (HasWord(t, "SERCOMTEL")) return "SERCOMTEL";
        if (HasWord(t, "BRISANET")) return "BRISANET";
        return "OUTRAS";
    }

    public static string Geracao(string? tecnologiaUpper) => tecnologiaUpper switch
    {
        null => "N/D",
        "GSM" or "CDMA" => "2G",
        "WCDMA" or "WDCMA" => "3G",
        "LTE" => "4G",
        "NR" => "5G",
        _ => "OUTRA",
    };

    /// <summary>Largura de banda em kHz a partir dos 4 primeiros caracteres da designação.</summary>
    public static double? LarguraBandaKhz(string? designacaoUpper)
    {
        if (designacaoUpper is null || designacaoUpper.Length < 4) return null;
        var head = designacaoUpper.Substring(0, 4);
        var unitPos = -1;
        for (var i = 0; i < 4; i++)
        {
            var ch = head[i];
            if (ch is 'H' or 'K' or 'M' or 'G')
            {
                if (i == 0 || unitPos >= 0) return null;
                unitPos = i;
            }
            else if (ch < '0' || ch > '9') return null;
        }
        if (unitPos < 0) return null;

        var intPart = head.Substring(0, unitPos);
        var fracPart = head.Substring(unitPos + 1);
        var value = decimal.Parse(intPart + (fracPart.Length > 0 ? "." + fracPart : ""),
            System.Globalization.CultureInfo.InvariantCulture);
        var factor = head[unitPos] switch
        {
            'H' => 0.001m,
            'K' => 1m,
            'M' => 1000m,
            _ => 1_000_000m,
        };
        return (double)(value * factor);
    }

    public static double? DuplexMhz(double? tx, double? rx)
        => tx is null || rx is null ? null : Cleaning.RoundValue(tx.Value - rx.Value, 3);

    public static double? PotenciaDbm(double? watts)
    {
        if (watts is null || watts.Value <= 0) return null;
        var dbm = 10 * Math.Log10(watts.Value * 1000);
        return double.IsFinite(dbm) ? Cleaning.RoundValue(dbm, 1) : null;
    }

    public static bool Vencida(DateOnly? validade) => validade is not null && validade.Value < Today;

    public static double? AnosLicenciada(DateOnly? primeiro)
    {
        if (primeiro is null) return null;
        var dias = Today.DayNumber - primeiro.Value.DayNumber;
        return Cleaning.RoundValue(dias / 365.25, 1);
    }

    public static bool CoordenadaValida(double? lat, double? lon)
    {
        if (lat is null || lon is null) return false;
        if (lat.Value == 0 && lon.Value == 0) return false;
        return lat.Value >= -33.75 && lat.Value <= 5.28 && lon.Value >= -73.99 && lon.Value <= -28.83;
    }

    public static bool? AzimuteValido(double? az) => az is null ? null : az.Value >= 0 && az.Value <= 360;

    public static bool? ElevacaoValida(double? el) => el is null ? null : el.Value >= -90 && el.Value <= 90;
}
