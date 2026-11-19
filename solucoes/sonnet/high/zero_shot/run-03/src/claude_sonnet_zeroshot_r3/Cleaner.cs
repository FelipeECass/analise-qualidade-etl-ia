using System.Globalization;
using System.Text;

namespace AnatelEtl;

/// <summary>Regras de limpeza e conversão (seção 1) e derivações numéricas (seção 4).</summary>
public static class Cleaner
{
    /// <summary>Remove espaços das pontas; vazio vira null.</summary>
    public static string? Clean(string? s)
    {
        if (s is null) return null;
        var t = s.Trim();
        return t.Length == 0 ? null : t;
    }

    public static string? Upper(string? s) => s?.ToUpperInvariant();

    /// <summary>Número finito; aceita '.' ou ',' como separador decimal (nunca milhar).</summary>
    public static double? ParseNumber(string? s)
    {
        if (s is null) return null;
        var t = s.Replace(',', '.');
        if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return null;
        if (!double.IsFinite(v)) return null;
        return v == 0 ? 0.0 : v; // normaliza -0
    }

    /// <summary>Data estrita yyyy-MM-dd.</summary>
    public static DateOnly? ParseDate(string? s)
    {
        if (s is null) return null;
        if (DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d;
        return null;
    }

    /// <summary>Remove acentos, maiúsculas, não-alfanumérico vira espaço, colapsa espaços.</summary>
    public static string? NormalizeText(string? s)
    {
        if (s is null) return null;
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        bool pendingSpace = false;
        foreach (var ch in d)
        {
            if (char.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            var u = char.ToUpperInvariant(ch);
            if (char.IsLetterOrDigit(u))
            {
                if (pendingSpace && sb.Length > 0) sb.Append(' ');
                pendingSpace = false;
                sb.Append(u);
            }
            else
            {
                pendingSpace = true;
            }
        }
        return sb.Length == 0 ? null : sb.ToString();
    }

    /// <summary>Arredonda com a metade para longe do zero (semântica decimal).</summary>
    public static double RoundHalfAway(double value, int digits)
    {
        try
        {
            var m = (decimal)value; // 15 dígitos significativos: elimina ruído binário
            return (double)Math.Round(m, digits, MidpointRounding.AwayFromZero);
        }
        catch (OverflowException)
        {
            return Math.Round(value, digits, MidpointRounding.AwayFromZero);
        }
    }

    public static string Operadora(string? entidade)
    {
        if (entidade is null) return "OUTRAS";
        var tokens = entidade.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool Has(string w) => Array.IndexOf(tokens, w) >= 0;

        if (Has("TELEFONICA") || Has("VIVO")) return "VIVO";
        if (Has("CLARO") || Has("EMBRATEL")) return "CLARO";
        if (tokens.Length > 0 && tokens[0] == "TIM") return "TIM";
        if ((tokens.Length > 0 && tokens[0] == "OI") || Has("TELEMAR")
            || (" " + entidade + " ").Contains(" BRASIL TELECOM ", StringComparison.Ordinal)) return "OI";
        if (Has("ALGAR")) return "ALGAR";
        if (Has("SERCOMTEL")) return "SERCOMTEL";
        if (Has("BRISANET")) return "BRISANET";
        return "OUTRAS";
    }

    public static string Geracao(string? tecnologia)
    {
        if (tecnologia is null) return "N/D";
        switch (tecnologia.ToUpperInvariant())
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

    public static double? LarguraBandaKhz(string? designacao)
    {
        if (designacao is null) return null;
        var d = designacao.ToUpperInvariant();
        if (d.Length < 4) return null;
        var p = d.Substring(0, 4);

        int unitIdx = -1;
        for (int i = 0; i < 4; i++)
        {
            char c = p[i];
            if (c == 'H' || c == 'K' || c == 'M' || c == 'G')
            {
                if (unitIdx >= 0) return null; // mais de uma letra de unidade
                unitIdx = i;
            }
            else if (!char.IsAsciiDigit(c))
            {
                return null;
            }
        }
        if (unitIdx < 1) return null; // ausente ou na primeira posição

        var intPart = p.Substring(0, unitIdx);
        var fracPart = p.Substring(unitIdx + 1);
        decimal factor = p[unitIdx] switch
        {
            'H' => 0.001m,
            'K' => 1m,
            'M' => 1000m,
            _ => 1_000_000m,
        };
        decimal v = decimal.Parse(fracPart.Length > 0 ? intPart + "." + fracPart : intPart, CultureInfo.InvariantCulture);
        return (double)(v * factor);
    }

    public static double? DuplexMhz(double? tx, double? rx)
        => tx is null || rx is null ? null : RoundHalfAway(tx.Value - rx.Value, 3);

    public static double? PotenciaDbm(double? w)
        => w is null || w.Value <= 0 ? null : RoundHalfAway(10.0 * Math.Log10(w.Value * 1000.0), 1);

    public static double? AnosLicenciada(DateOnly? primeira, DateOnly hoje)
        => primeira is null ? null : RoundHalfAway((hoje.DayNumber - primeira.Value.DayNumber) / 365.25, 1);

    public static bool CoordenadaValida(double? lat, double? lon)
    {
        if (lat is null || lon is null) return false;
        if (lat.Value == 0 && lon.Value == 0) return false;
        return lat.Value >= -33.75 && lat.Value <= 5.28 && lon.Value >= -73.99 && lon.Value <= -28.83;
    }
}
