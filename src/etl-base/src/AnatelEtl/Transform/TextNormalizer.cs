using System.Globalization;
using System.Text;

namespace AnatelEtl.Transform;

public static class TextNormalizer
{
    /// <summary>
    /// Remove acentos, converte para maiúsculas e troca qualquer pontuação por espaço único.
    /// Ex.: <c>"Telefônica  Brasil S.A."</c> → <c>"TELEFONICA BRASIL S A"</c>.
    /// </summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var lastWasSpace = true;

        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToUpperInvariant(c));
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                builder.Append(' ');
                lastWasSpace = true;
            }
        }

        var result = builder.ToString().TrimEnd();
        return result.Length == 0 ? null : result;
    }
}
