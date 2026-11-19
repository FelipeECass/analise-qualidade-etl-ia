using System.Globalization;

namespace claude_haiku_completo_r3.Regras;

/// <summary>Colunas derivadas e validações (seções 4 e 5 da especificação).</summary>
public static class Derivadas
{
    /// <summary>Data de referência fixa ("hoje" da especificação).</summary>
    public static readonly DateOnly Hoje = new(2026, 10, 15);

    /// <summary>
    /// Arredonda com metade para longe do zero. O double é convertido pela sua representação
    /// curta de ida e volta ("R"), que é o valor decimal que ele representa (2,25 → 2,3).
    /// </summary>
    public static double Arredondar(double valor, int casas)
    {
        // decimal só comporta magnitudes até ~7,9e28; fora disso, o double já é a fonte.
        if (Math.Abs(valor) > 1e20) return Math.Round(valor, casas, MidpointRounding.AwayFromZero);

        var decimalDoValor = decimal.Parse(valor.ToString("R", CultureInfo.InvariantCulture),
            NumberStyles.Float, CultureInfo.InvariantCulture);
        return (double)Math.Round(decimalDoValor, casas, MidpointRounding.AwayFromZero);
    }

    /// <summary>Operadora a partir da entidade já normalizada (regra 4, ordem de precedência).</summary>
    public static string Operadora(string? entidadeNormalizada)
    {
        if (entidadeNormalizada is null) return "OUTRAS";

        var tokens = entidadeNormalizada.Split(' ');

        if (ContemAlgum(tokens, "TELEFONICA", "VIVO")) return "VIVO";
        if (ContemAlgum(tokens, "CLARO", "EMBRATEL")) return "CLARO";
        if (tokens[0] == "TIM") return "TIM";
        if (tokens[0] == "OI" || ContemAlgum(tokens, "TELEMAR") || ContemSequencia(tokens, "BRASIL", "TELECOM"))
            return "OI";
        if (ContemAlgum(tokens, "ALGAR")) return "ALGAR";
        if (ContemAlgum(tokens, "SERCOMTEL")) return "SERCOMTEL";
        if (ContemAlgum(tokens, "BRISANET")) return "BRISANET";

        return "OUTRAS";
    }

    /// <summary>Geração a partir da tecnologia já em maiúsculas.</summary>
    public static string Geracao(string? tecnologia) => tecnologia switch
    {
        null => "N/D",
        "GSM" or "CDMA" => "2G",
        "WCDMA" or "WDCMA" => "3G",
        "LTE" => "4G",
        "NR" => "5G",
        _ => "OUTRA",
    };

    /// <summary>
    /// Largura de banda em kHz a partir dos 4 primeiros caracteres da designação.
    /// Exatamente uma unidade (H, K, M, G) em 1..3; demais posições são dígitos. Inválido → null.
    /// </summary>
    public static double? LarguraBandaKhz(string? designacao)
    {
        if (designacao is null) return null;
        var d = designacao.ToUpperInvariant();
        if (d.Length < 4) return null;

        var posicaoUnidade = -1;
        for (var i = 0; i < 4; i++)
        {
            var c = d[i];
            if (c is 'H' or 'K' or 'M' or 'G')
            {
                if (posicaoUnidade >= 0 || i == 0) return null;
                posicaoUnidade = i;
            }
            else if (c < '0' || c > '9')
            {
                return null;
            }
        }
        if (posicaoUnidade < 0) return null;

        var inteiro = d.Substring(0, posicaoUnidade);
        var fracao = d.Substring(posicaoUnidade + 1, 4 - posicaoUnidade - 1);
        var texto = fracao.Length == 0 ? inteiro : $"{inteiro}.{fracao}";
        var valor = decimal.Parse(texto, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

        var fator = d[posicaoUnidade] switch
        {
            'H' => 0.001m,
            'K' => 1m,
            'M' => 1000m,
            _ => 1000000m, // 'G'
        };

        return (double)(valor * fator);
    }

    /// <summary>Validade de azimute: 0 ≤ valor ≤ 360. Nulo → null.</summary>
    public static int? AzimuteValido(double? azimute)
    {
        if (azimute is null) return null;
        return azimute >= 0 && azimute <= 360 ? 1 : 0;
    }

    /// <summary>Validade de elevação: −90 ≤ valor ≤ 90. Nulo → null.</summary>
    public static int? ElevacaoValida(double? elevacao)
    {
        if (elevacao is null) return null;
        return elevacao >= -90 && elevacao <= 90 ? 1 : 0;
    }

    /// <summary>Coordenada válida: existe, não é (0,0) e está dentro do retângulo do Brasil (inclusive).</summary>
    public static bool CoordenadaValida(double? latitude, double? longitude)
    {
        if (latitude is null || longitude is null) return false;
        var lat = latitude.Value;
        var lon = longitude.Value;
        if (lat == 0 && lon == 0) return false;
        return lat >= -33.75 && lat <= 5.28 && lon >= -73.99 && lon <= -28.83;
    }

    /// <summary>Potência em dBm, arredondada a 1 casa. Nula ou ≤ 0 → null.</summary>
    public static double? PotenciaDbm(double? potenciaW)
    {
        if (potenciaW is null || potenciaW <= 0) return null;
        return Arredondar(10 * Math.Log10(potenciaW.Value * 1000), 1);
    }

    /// <summary>
    /// Duplex = tx − rx, arredondado a 3 casas. A subtração é decimal exata sobre os valores de origem
    /// (ex.: 158,43125 − 163,04375 = −4,6125 → −4,613). Falta de qualquer um → null.
    /// </summary>
    public static double? Duplex(decimal? tx, decimal? rx)
    {
        if (tx is null || rx is null) return null;
        return (double)Math.Round(tx.Value - rx.Value, 3, MidpointRounding.AwayFromZero);
    }

    /// <summary>Vencida: validade existe e é anterior a Hoje.</summary>
    public static int Vencida(DateOnly? validade) => validade is not null && validade.Value < Hoje ? 1 : 0;

    /// <summary>Anos licenciada: dias desde o primeiro licenciamento ÷ 365,25, 1 casa. Sem data → null.</summary>
    public static double? AnosLicenciada(DateOnly? primeiroLicenciamento)
    {
        if (primeiroLicenciamento is null) return null;
        var dias = Hoje.DayNumber - primeiroLicenciamento.Value.DayNumber;
        return Arredondar(dias / 365.25, 1);
    }

    private static bool ContemAlgum(string[] tokens, params string[] palavras)
    {
        foreach (var t in tokens)
            foreach (var p in palavras)
                if (t == p) return true;
        return false;
    }

    private static bool ContemSequencia(string[] tokens, string a, string b)
    {
        for (var i = 0; i + 1 < tokens.Length; i++)
            if (tokens[i] == a && tokens[i + 1] == b) return true;
        return false;
    }
}
