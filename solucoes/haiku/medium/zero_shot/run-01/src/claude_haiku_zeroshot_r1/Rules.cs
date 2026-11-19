using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ClaudeHaikuZeroshotR1;

/// <summary>Regras de limpeza, tratamento e derivação (seções 1, 3, 4 e 5 da especificação).</summary>
internal static class Rules
{
    /// <summary>Data de referência fixa ("Hoje").</summary>
    public static readonly DateOnly Hoje = new(2026, 10, 15);

    private static readonly Regex IsoDate = new(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.CultureInvariant);

    // ---------- 1.1 / 1.2: limpeza básica ----------
    public static string? Clean(string? raw)
    {
        if (raw is null) return null;
        var s = raw.Trim();
        return s.Length == 0 ? null : s;
    }

    /// <summary>Limpeza seguida de maiúsculas (campos "maiúsculas").</summary>
    public static string? Upper(string? raw) => Clean(raw)?.ToUpperInvariant();

    // ---------- 1.6: normalização de texto ----------
    public static string? NormalizeText(string? raw)
    {
        var s = Clean(raw);
        if (s is null) return null;

        var decomposed = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat is UnicodeCategory.NonSpacingMark
                    or UnicodeCategory.SpacingCombiningMark
                    or UnicodeCategory.EnclosingMark)
                continue;

            var up = char.ToUpperInvariant(ch);
            sb.Append(char.IsLetterOrDigit(up) ? up : ' ');
        }

        var words = sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length == 0 ? null : string.Join(' ', words);
    }

    // ---------- 1.3: números ----------
    /// <summary>
    /// Aceita '.' ou ',' como decimal (nunca milhar). Não finito ou inválido → null.
    /// Ex.: "18.000" → 18; "2,25" → 2.25; "NA", "NaN" → null.
    /// </summary>
    public static decimal? ParseNumber(string? raw)
    {
        var s = Clean(raw);
        if (s is null) return null;
        s = s.Replace(',', '.');
        if (decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return d;
        return null;
    }

    /// <summary>Converte para double (REAL) com forma decimal exata e sem zero negativo.</summary>
    public static double ToDouble(decimal d)
    {
        var v = double.Parse(d.ToString(CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);
        return v == 0 ? 0.0 : v;
    }

    public static double? ToDouble(decimal? d) => d.HasValue ? ToDouble(d.Value) : null;

    // ---------- 1.4: datas ----------
    public static DateOnly? ParseDate(string? raw)
    {
        var s = Clean(raw);
        if (s is null || !IsoDate.IsMatch(s)) return null;
        if (DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d;
        return null;
    }

    public static string? DateText(DateOnly? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // ---------- 4: colunas derivadas ----------

    /// <summary>
    /// Operadora a partir da entidade já normalizada. "Palavra" = delimitada por espaço
    /// (a entidade normalizada só tem letras, dígitos e espaços simples).
    /// </summary>
    public static string Operadora(string? entidadeNorm)
    {
        if (entidadeNorm is null) return "OUTRAS";

        var pad = " " + entidadeNorm + " ";
        var primeira = entidadeNorm.Split(' ')[0];
        bool Contem(string palavra) => pad.Contains(" " + palavra + " ", StringComparison.Ordinal);

        if (Contem("TELEFONICA") || Contem("VIVO")) return "VIVO";
        if (Contem("CLARO") || Contem("EMBRATEL")) return "CLARO";
        if (primeira == "TIM") return "TIM";
        if (primeira == "OI" || Contem("TELEMAR") || pad.Contains(" BRASIL TELECOM ", StringComparison.Ordinal)) return "OI";
        if (Contem("ALGAR")) return "ALGAR";
        if (Contem("SERCOMTEL")) return "SERCOMTEL";
        if (Contem("BRISANET")) return "BRISANET";
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

    /// <summary>
    /// Largura de banda (kHz) a partir dos 4 primeiros caracteres da designação:
    /// uma letra de unidade H/K/M/G em posição 1..3; demais posições são dígitos.
    /// Ex.: 5M00 → 5000; 10M0 → 10000; 200K → 200; 2M50 → 2500; 1H00 → 0,001.
    /// </summary>
    public static double? LarguraBandaKhz(string? designacao)
    {
        var d = Upper(designacao);
        if (d is null || d.Length < 4) return null;

        var p = d[..4];
        int unidade = -1;
        for (int i = 0; i < 4; i++)
        {
            if ("HKMG".IndexOf(p[i]) >= 0)
            {
                if (unidade >= 0) return null;
                unidade = i;
            }
        }
        if (unidade < 1) return null;

        for (int i = 0; i < 4; i++)
        {
            if (i != unidade && !char.IsAsciiDigit(p[i])) return null;
        }

        var inteiro = p[..unidade];
        var fracao = p[(unidade + 1)..];
        var texto = fracao.Length > 0 ? inteiro + "." + fracao : inteiro;
        var valor = decimal.Parse(texto, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

        decimal fator = p[unidade] switch
        {
            'H' => 0.001m,
            'K' => 1m,
            'M' => 1000m,
            _ => 1000000m, // 'G'
        };
        return ToDouble(valor * fator);
    }

    public static decimal? Duplex(decimal? tx, decimal? rx)
    {
        if (tx is null || rx is null) return null;
        return Math.Round(tx.Value - rx.Value, 3, MidpointRounding.AwayFromZero);
    }

    public static decimal? PotenciaDbm(decimal? potenciaW)
    {
        if (potenciaW is null || potenciaW <= 0) return null;
        var dbm = 10 * Math.Log10(ToDouble(potenciaW.Value) * 1000);
        return (decimal)Math.Round(dbm, 1, MidpointRounding.AwayFromZero);
    }

    public static bool Vencida(DateOnly? validade) => validade is not null && validade.Value < Hoje;

    public static decimal? AnosLicenciada(DateOnly? primeiro)
    {
        if (primeiro is null) return null;
        var dias = Hoje.DayNumber - primeiro.Value.DayNumber;
        var anos = (decimal)dias / 365.25m;
        return Math.Round(anos, 1, MidpointRounding.AwayFromZero);
    }

    public static bool CoordenadaValida(decimal? lat, decimal? lon)
    {
        if (lat is null || lon is null) return false;
        if (lat == 0 && lon == 0) return false;
        return lat >= -33.75m && lat <= 5.28m && lon >= -73.99m && lon <= -28.83m;
    }

    // ---------- 5: validações técnicas (três estados) ----------
    public static int? Entre(decimal? valor, decimal min, decimal max)
    {
        if (valor is null) return null;
        return valor >= min && valor <= max ? 1 : 0;
    }
}
