namespace AnatelEtl;

/// <summary>Colunas derivadas e validações (seções 4 e 5). Funções puras.</summary>
public static class Derivations
{
    /// <summary>"Hoje" fixo da especificação.</summary>
    public static readonly DateOnly Today = new(2026, 10, 15);

    public static double Round(double v, int digits) => Math.Round(v, digits, MidpointRounding.AwayFromZero);

    /// <summary>Operadora a partir da entidade já normalizada (regras 1–7, primeira que casar).</summary>
    public static string Operadora(string? entidadeNormalizada)
    {
        if (entidadeNormalizada is null) return "OUTRAS";
        // Entidade normalizada só tem letras/dígitos e espaços simples: "palavra" == token entre espaços.
        string padded = " " + entidadeNormalizada + " ";
        bool Has(string word) => padded.Contains(" " + word + " ", StringComparison.Ordinal);
        bool Starts(string word) => entidadeNormalizada.Equals(word, StringComparison.Ordinal)
                                    || entidadeNormalizada.StartsWith(word + " ", StringComparison.Ordinal);

        if (Has("TELEFONICA") || Has("VIVO")) return "VIVO";
        if (Has("CLARO") || Has("EMBRATEL")) return "CLARO";
        if (Starts("TIM")) return "TIM";
        if (Starts("OI") || Has("TELEMAR") || Has("BRASIL TELECOM")) return "OI";
        if (Has("ALGAR")) return "ALGAR";
        if (Has("SERCOMTEL")) return "SERCOMTEL";
        if (Has("BRISANET")) return "BRISANET";
        return "OUTRAS";
    }

    /// <summary>Geração a partir da tecnologia (comparação sem caixa).</summary>
    public static string Geracao(string? tecnologia)
    {
        if (string.IsNullOrWhiteSpace(tecnologia)) return "N/D";
        return tecnologia.Trim().ToUpperInvariant() switch
        {
            "GSM" or "CDMA" => "2G",
            "WCDMA" or "WDCMA" => "3G",
            "LTE" => "4G",
            "NR" => "5G",
            _ => "OUTRA",
        };
    }

    /// <summary>
    /// Decodifica os 4 primeiros caracteres da designação (ex.: 5M00 → 5000 kHz). Uma letra de unidade H/K/M/G
    /// (nunca na 1ª posição) separa parte inteira e fracionária; as demais posições são dígitos.
    /// </summary>
    public static double? LarguraBandaKhz(string? designacaoMaiuscula)
    {
        if (designacaoMaiuscula is null || designacaoMaiuscula.Length < 4) return null;
        int unitPos = -1;
        for (int i = 0; i < 4; i++)
        {
            char c = designacaoMaiuscula[i];
            if (c >= '0' && c <= '9') continue;
            if ((c == 'H' || c == 'K' || c == 'M' || c == 'G') && i > 0 && unitPos < 0) { unitPos = i; continue; }
            return null;
        }
        if (unitPos < 0) return null;

        decimal intPart = 0;
        for (int i = 0; i < unitPos; i++) intPart = intPart * 10 + (designacaoMaiuscula[i] - '0');
        decimal frac = 0, scale = 0.1m;
        for (int i = unitPos + 1; i < 4; i++)
        {
            frac += (designacaoMaiuscula[i] - '0') * scale;
            scale /= 10;
        }
        decimal factor = designacaoMaiuscula[unitPos] switch
        {
            'H' => 0.001m,
            'K' => 1m,
            'M' => 1000m,
            _ => 1_000_000m,
        };
        return (double)((intPart + frac) * factor);
    }

    public static double? DuplexMhz(double? tx, double? rx) =>
        tx.HasValue && rx.HasValue ? Round(tx.Value - rx.Value, 3) : null;

    public static double? PotenciaDbm(double? watts) =>
        watts.HasValue && watts.Value > 0 ? Round(10.0 * Math.Log10(watts.Value * 1000.0), 1) : null;

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
