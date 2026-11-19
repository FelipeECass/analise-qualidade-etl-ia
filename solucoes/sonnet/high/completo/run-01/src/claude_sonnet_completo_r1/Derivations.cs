namespace AnatelEtl;

/// <summary>Seções 4 e 5: colunas derivadas e validações.</summary>
public static class Derivations
{
    public static readonly DateOnly Hoje = new(2026, 10, 15);

    /// <summary>Arredondamento com a metade para longe do zero.</summary>
    public static double RoundAway(double v, int digits)
    {
        if (Math.Abs(v) < 1e15)
        {
            // (decimal)double arredonda a 15 dígitos significativos, eliminando ruído binário (ex.: 2.675000000000001)
            return (double)Math.Round((decimal)v, digits, MidpointRounding.AwayFromZero);
        }
        return Math.Round(v, digits, MidpointRounding.AwayFromZero);
    }

    // ---------- operadora ----------
    public static string Operadora(string? entidadeNormalizada)
    {
        if (entidadeNormalizada is null) return "OUTRAS";
        var tokens = entidadeNormalizada.Split(' ');
        bool Has(string w) => Array.IndexOf(tokens, w) >= 0;

        if (Has("TELEFONICA") || Has("VIVO")) return "VIVO";
        if (Has("CLARO") || Has("EMBRATEL")) return "CLARO";
        if (tokens[0] == "TIM") return "TIM";
        if (tokens[0] == "OI" || Has("TELEMAR") || HasPair(tokens, "BRASIL", "TELECOM")) return "OI";
        if (Has("ALGAR")) return "ALGAR";
        if (Has("SERCOMTEL")) return "SERCOMTEL";
        if (Has("BRISANET")) return "BRISANET";
        return "OUTRAS";
    }

    private static bool HasPair(string[] tokens, string a, string b)
    {
        for (int i = 0; i + 1 < tokens.Length; i++)
            if (tokens[i] == a && tokens[i + 1] == b) return true;
        return false;
    }

    // ---------- geracao ----------
    public static string Geracao(string? tecnologia)
    {
        if (string.IsNullOrWhiteSpace(tecnologia)) return "N/D";
        switch (tecnologia.Trim().ToUpperInvariant())
        {
            case "GSM":
            case "CDMA": return "2G";
            case "WCDMA":
            case "WDCMA": return "3G";
            case "LTE": return "4G";
            case "NR": return "5G";
            default: return "OUTRA";
        }
    }

    // ---------- largura de banda ----------
    public static double? LarguraBandaKhz(string? designacaoMaiuscula)
    {
        if (designacaoMaiuscula is null || designacaoMaiuscula.Length < 4) return null;

        int unitPos = -1;
        for (int i = 0; i < 4; i++)
        {
            char c = designacaoMaiuscula[i];
            if (c >= '0' && c <= '9') continue;
            if ((c == 'H' || c == 'K' || c == 'M' || c == 'G') && i > 0 && unitPos < 0) { unitPos = i; continue; }
            return null; // caractere inválido ou segunda letra de unidade
        }
        if (unitPos < 0) return null;

        decimal inteiro = 0;
        for (int i = 0; i < unitPos; i++) inteiro = inteiro * 10 + (designacaoMaiuscula[i] - '0');

        decimal fracao = 0, escala = 1;
        for (int i = unitPos + 1; i < 4; i++)
        {
            escala /= 10;
            fracao += (designacaoMaiuscula[i] - '0') * escala;
        }

        decimal fator = designacaoMaiuscula[unitPos] switch
        {
            'H' => 0.001m,
            'K' => 1m,
            'M' => 1000m,
            _ => 1_000_000m,
        };
        return (double)((inteiro + fracao) * fator);
    }

    public static double? DuplexMhz(double? tx, double? rx)
        => tx.HasValue && rx.HasValue ? NormZero(RoundAway(tx.Value - rx.Value, 3)) : null;

    public static double? PotenciaDbm(double? watts)
    {
        if (!watts.HasValue || watts.Value <= 0) return null;
        var dbm = 10.0 * Math.Log10(watts.Value * 1000.0);
        return double.IsFinite(dbm) ? NormZero(RoundAway(dbm, 1)) : null;
    }

    public static bool Vencida(DateOnly? validade) => validade.HasValue && validade.Value < Hoje;

    public static double? AnosLicenciada(DateOnly? primeiro)
    {
        if (!primeiro.HasValue) return null;
        int dias = Hoje.DayNumber - primeiro.Value.DayNumber;
        return NormZero(RoundAway(dias / 365.25, 1));
    }

    public static bool CoordenadaValida(double? lat, double? lon)
    {
        if (!lat.HasValue || !lon.HasValue) return false;
        if (lat.Value == 0 && lon.Value == 0) return false;
        return lat.Value >= -33.75 && lat.Value <= 5.28 && lon.Value >= -73.99 && lon.Value <= -28.83;
    }

    public static bool? Azimute(double? az) => az.HasValue ? az.Value >= 0 && az.Value <= 360 : null;

    public static bool? Elevacao(double? el) => el.HasValue ? el.Value >= -90 && el.Value <= 90 : null;

    private static double NormZero(double v) => v == 0 ? 0.0 : v;
}
