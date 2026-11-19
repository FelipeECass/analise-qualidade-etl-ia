using System.Globalization;

namespace AnatelEtl.Transform;

/// <summary>
/// Decodifica a "designação de emissão" da UIT (ex.: <c>5M00G7W</c>).
/// Os quatro primeiros caracteres são a largura de banda, com a letra da unidade
/// no lugar da vírgula decimal: <c>5M00</c> = 5,00 MHz; <c>200K</c> = 200 kHz.
/// </summary>
public static class EmissionDesignation
{
    private const int BandwidthLength = 4;

    private static readonly Dictionary<char, double> UnitToKhz = new()
    {
        ['H'] = 0.001,
        ['K'] = 1,
        ['M'] = 1_000,
        ['G'] = 1_000_000,
    };

    /// <summary>Largura de banda em kHz, ou <c>null</c> se a designação for inválida.</summary>
    public static double? ParseBandwidthKhz(string? designation)
    {
        if (string.IsNullOrWhiteSpace(designation) || designation.Length < BandwidthLength)
        {
            return null;
        }

        var head = designation[..BandwidthLength].ToUpperInvariant();
        var unitIndex = head.IndexOfAny([.. UnitToKhz.Keys]);
        if (unitIndex <= 0)
        {
            return null;
        }

        var integerPart = head[..unitIndex];
        var fractionPart = head[(unitIndex + 1)..];
        if (!integerPart.All(char.IsAsciiDigit) || !fractionPart.All(char.IsAsciiDigit))
        {
            return null;
        }

        var text = fractionPart.Length == 0 ? integerPart : $"{integerPart}.{fractionPart}";
        var number = double.Parse(text, CultureInfo.InvariantCulture);
        return number * UnitToKhz[head[unitIndex]];
    }
}
