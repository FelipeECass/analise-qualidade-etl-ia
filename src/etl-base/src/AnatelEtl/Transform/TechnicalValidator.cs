namespace AnatelEtl.Transform;

/// <summary>
/// Validações técnicas de plausibilidade. Cada método devolve <c>null</c> quando não há
/// dado suficiente para decidir (diferente de "inválido").
/// </summary>
public static class TechnicalValidator
{
    private const double MaxAzimuthDegrees = 360;
    private const double MaxElevationDegrees = 90;
    private const double DuplexToleranceMhz = 0.5;
    private const double MilliwattsPerWatt = 1000;

    /// <summary>
    /// Espaçamento duplex (|Tx − Rx|, em MHz) esperado por faixa celular. Valores calibrados
    /// com os dados reais de SC; as faixas 2300 e 3500 são TDD (Tx = Rx).
    /// </summary>
    private static readonly Dictionary<string, double> ExpectedDuplexMhz = new()
    {
        ["700 MHz"] = 55,
        ["850/900 MHz"] = 45,
        ["1800 MHz"] = 95,
        ["2100 MHz"] = 190,
        ["2600 MHz"] = 120,
        ["2300 MHz"] = 0,
        ["3500 MHz"] = 0,
    };

    private static readonly HashSet<string> CellularGenerations = ["2G", "3G", "4G", "5G"];

    public static bool? IsAzimuthValid(double? degrees) =>
        degrees is { } value ? value is >= 0 and <= MaxAzimuthDegrees : null;

    public static bool? IsElevationValid(double? degrees) =>
        degrees is { } value ? value is >= -MaxElevationDegrees and <= MaxElevationDegrees : null;

    /// <summary>
    /// Compara o duplex medido com o esperado da faixa. Só se aplica a emissões celulares
    /// (2G–5G) em faixas conhecidas; nos demais casos devolve <c>null</c>.
    /// </summary>
    public static bool? IsDuplexConsistent(string generation, string band, double? duplexMhz)
    {
        if (duplexMhz is not { } duplex
            || !CellularGenerations.Contains(generation)
            || !ExpectedDuplexMhz.TryGetValue(band, out var expected))
        {
            return null;
        }

        return Math.Abs(Math.Abs(duplex) - expected) <= DuplexToleranceMhz;
    }

    /// <summary>Converte potência em watts para dBm, arredondado a 1 casa. <c>null</c> se ≤ 0.</summary>
    public static double? ToDbm(double? watts) =>
        watts is > 0
            ? Math.Round(10 * Math.Log10(watts.Value * MilliwattsPerWatt), 1)
            : null;
}
