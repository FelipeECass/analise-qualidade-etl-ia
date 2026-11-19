namespace Etl;

/// <summary>Colunas derivadas e validações (seções 4 e 5).</summary>
public static class Derivations
{
    public static readonly DateOnly Today = new(2026, 10, 15);

    public static double Round(double value, int digits)
    {
        var r = Math.Round(value, digits, MidpointRounding.AwayFromZero);
        return r == 0 ? 0.0 : r;   // evita -0
    }

    private static bool HasWord(string[] tokens, string word) => Array.IndexOf(tokens, word) >= 0;

    /// <param name="entidade">entidade já normalizada</param>
    public static string Operadora(string? entidade)
    {
        if (entidade == null) return "OUTRAS";
        var tk = entidade.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tk.Length == 0) return "OUTRAS";

        if (HasWord(tk, "TELEFONICA") || HasWord(tk, "VIVO")) return "VIVO";
        if (HasWord(tk, "CLARO") || HasWord(tk, "EMBRATEL")) return "CLARO";
        if (tk[0] == "TIM") return "TIM";
        if (tk[0] == "OI" || HasWord(tk, "TELEMAR") || HasPhrase(tk, "BRASIL", "TELECOM")) return "OI";
        if (HasWord(tk, "ALGAR")) return "ALGAR";
        if (HasWord(tk, "SERCOMTEL")) return "SERCOMTEL";
        if (HasWord(tk, "BRISANET")) return "BRISANET";
        return "OUTRAS";
    }

    private static bool HasPhrase(string[] tokens, string a, string b)
    {
        for (int i = 0; i + 1 < tokens.Length; i++)
            if (tokens[i] == a && tokens[i + 1] == b) return true;
        return false;
    }

    /// <param name="tecnologia">tecnologia limpa (maiúsculas) ou null</param>
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

    /// <param name="designacao">designação limpa em maiúsculas ou null</param>
    public static double? LarguraBandaKhz(string? designacao)
    {
        if (designacao == null || designacao.Length < 4) return null;

        int unitPos = -1;
        for (int i = 0; i < 4; i++)
        {
            char c = designacao[i];
            if (c >= '0' && c <= '9') continue;
            if ((c == 'H' || c == 'K' || c == 'M' || c == 'G') && i > 0 && unitPos < 0) { unitPos = i; continue; }
            return null;
        }
        if (unitPos < 0) return null;

        decimal intPart = 0;
        for (int i = 0; i < unitPos; i++) intPart = intPart * 10 + (designacao[i] - '0');
        decimal frac = 0, scale = 1;
        for (int i = unitPos + 1; i < 4; i++)
        {
            scale /= 10;
            frac += (designacao[i] - '0') * scale;
        }
        decimal factor = designacao[unitPos] switch
        {
            'H' => 0.001m,
            'K' => 1m,
            'M' => 1000m,
            _ => 1_000_000m,
        };
        return (double)((intPart + frac) * factor);
    }

    public static double? Duplex(double? tx, double? rx)
        => tx.HasValue && rx.HasValue ? Round(tx.Value - rx.Value, 3) : null;

    public static double? PotenciaDbm(double? watts)
        => watts.HasValue && watts.Value > 0 ? Round(10.0 * Math.Log10(watts.Value * 1000.0), 1) : null;

    public static bool Vencida(DateOnly? validade) => validade.HasValue && validade.Value < Today;

    public static double? AnosLicenciada(DateOnly? primeira)
        => primeira.HasValue
            ? Round((Today.DayNumber - primeira.Value.DayNumber) / 365.25, 1)
            : null;

    public static bool CoordenadaValida(double? lat, double? lon)
    {
        if (!lat.HasValue || !lon.HasValue) return false;
        if (lat.Value == 0 && lon.Value == 0) return false;
        return lat.Value >= -33.75 && lat.Value <= 5.28 && lon.Value >= -73.99 && lon.Value <= -28.83;
    }

    public static bool? AzimuteValido(double? az) => az.HasValue ? az.Value >= 0 && az.Value <= 360 : null;

    public static bool? ElevacaoValida(double? el) => el.HasValue ? el.Value >= -90 && el.Value <= 90 : null;
}
