using System.Text.RegularExpressions;

namespace AnatelEtl.Transform;

/// <summary>Mapeia o nome (sujo) da entidade para uma operadora canônica.</summary>
public static class OperadoraClassifier
{
    public const string Outras = "OUTRAS";

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    private static readonly (Regex Pattern, string Operadora)[] Rules =
    [
        (Build(@"\bTELEFONICA\b|\bVIVO\b"), "VIVO"),
        (Build(@"\bCLARO\b|\bEMBRATEL\b"), "CLARO"),
        (Build(@"^TIM\b"), "TIM"),
        (Build(@"^OI\b|\bTELEMAR\b|\bBRASIL TELECOM\b"), "OI"),
        (Build(@"\bALGAR\b"), "ALGAR"),
        (Build(@"\bSERCOMTEL\b"), "SERCOMTEL"),
        (Build(@"\bBRISANET\b"), "BRISANET"),
    ];

    /// <param name="normalizedName">Nome já passado por <see cref="TextNormalizer.Normalize"/>.</param>
    public static string Classify(string? normalizedName)
    {
        if (string.IsNullOrEmpty(normalizedName))
        {
            return Outras;
        }

        foreach (var (pattern, operadora) in Rules)
        {
            if (pattern.IsMatch(normalizedName))
            {
                return operadora;
            }
        }

        return Outras;
    }

    private static Regex Build(string pattern) =>
        new(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
}
