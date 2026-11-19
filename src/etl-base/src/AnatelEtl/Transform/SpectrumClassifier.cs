namespace AnatelEtl.Transform;

/// <summary>Classificações derivadas do espectro: faixa de frequência e geração da tecnologia.</summary>
public static class SpectrumClassifier
{
    public const string NotAvailable = "N/D";
    public const string Other = "OUTRA";

    private static readonly (double MinMhz, double MaxMhz, string Band)[] Bands =
    [
        (0, 698, "VHF/UHF (<700 MHz)"),
        (698, 806, "700 MHz"),
        (806, 960, "850/900 MHz"),
        (1710, 1880, "1800 MHz"),
        (1880, 2200, "2100 MHz"),
        (2300, 2400, "2300 MHz"),
        (2500, 2700, "2600 MHz"),
        (3300, 3800, "3500 MHz"),
        (3800, 6000, "3,8-6 GHz"),
        (6000, double.PositiveInfinity, "6 GHz+ (enlaces)"),
    ];

    private static readonly Dictionary<string, string> Generations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GSM"] = "2G",
        ["CDMA"] = "2G",
        ["WCDMA"] = "3G",
        ["WDCMA"] = "3G", // erro de digitação presente na base da Anatel
        ["LTE"] = "4G",
        ["NR"] = "5G",
    };

    public static string ClassifyBand(double? frequencyMhz)
    {
        if (frequencyMhz is not { } mhz)
        {
            return NotAvailable;
        }

        foreach (var (min, max, band) in Bands)
        {
            if (mhz >= min && mhz < max)
            {
                return band;
            }
        }

        return Other;
    }

    public static string ClassifyGeneration(string? technology)
    {
        if (string.IsNullOrWhiteSpace(technology))
        {
            return NotAvailable;
        }

        return Generations.TryGetValue(technology.Trim(), out var generation) ? generation : Other;
    }
}
