using System.Globalization;
using System.Text;

namespace AnatelEtl;

/// <summary>Regras da seção 1: limpeza geral.</summary>
public static class Cleaner
{
    /// <summary>Trim; vazio ou só espaços vira null.</summary>
    public static string? Text(string? raw)
    {
        if (raw is null) return null;
        var t = raw.Trim();
        return t.Length == 0 ? null : t;
    }

    public static string? Upper(string? raw)
    {
        var t = Text(raw);
        return t?.ToUpperInvariant();
    }

    /// <summary>
    /// Número: "." ou "," são sempre separador decimal (nunca de milhar). Não finito / inválido → null.
    /// </summary>
    public static double? Number(string? raw)
    {
        var t = Text(raw);
        if (t is null) return null;

        // validação estrita do formato: [+-]?(\d+(\.\d*)?|\.\d+)([eE][+-]?\d+)?   (após trocar ',' por '.')
        int i = 0, n = t.Length;
        if (t[i] == '+' || t[i] == '-') i++;
        int intDigits = 0, fracDigits = 0;
        bool sawSep = false;
        while (i < n && t[i] >= '0' && t[i] <= '9') { i++; intDigits++; }
        if (i < n && (t[i] == '.' || t[i] == ','))
        {
            sawSep = true;
            i++;
            while (i < n && t[i] >= '0' && t[i] <= '9') { i++; fracDigits++; }
        }
        if (intDigits + fracDigits == 0) return null;
        _ = sawSep;
        if (i < n && (t[i] == 'e' || t[i] == 'E'))
        {
            i++;
            if (i < n && (t[i] == '+' || t[i] == '-')) i++;
            int expDigits = 0;
            while (i < n && t[i] >= '0' && t[i] <= '9') { i++; expDigits++; }
            if (expDigits == 0) return null;
        }
        if (i != n) return null;

        var norm = t.IndexOf(',') >= 0 ? t.Replace(',', '.') : t;
        if (!double.TryParse(norm, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return null;
        if (!double.IsFinite(v)) return null;
        if (v == 0) v = 0; // normaliza -0.0
        return v;
    }

    /// <summary>Data estrita yyyy-MM-dd (e válida no calendário).</summary>
    public static DateOnly? Date(string? raw)
    {
        var t = Text(raw);
        if (t is null || t.Length != 10) return null;
        for (int i = 0; i < 10; i++)
        {
            char c = t[i];
            if (i == 4 || i == 7) { if (c != '-') return null; }
            else if (c < '0' || c > '9') return null;
        }
        int y = (t[0] - '0') * 1000 + (t[1] - '0') * 100 + (t[2] - '0') * 10 + (t[3] - '0');
        int m = (t[5] - '0') * 10 + (t[6] - '0');
        int d = (t[8] - '0') * 10 + (t[9] - '0');
        if (y < 1 || m < 1 || m > 12 || d < 1 || d > DateTime.DaysInMonth(y, m)) return null;
        return new DateOnly(y, m, d);
    }

    /// <summary>
    /// Normalização (regra 1.6): remove acentos (NFD, descarta marcas), maiúsculas,
    /// não letra/dígito → espaço, colapsa espaços, trim. Vazio → null.
    /// </summary>
    private static readonly bool NfdWorks = DetectNfd();

    private static bool DetectNfd()
    {
        try { return "\u00C1".Normalize(NormalizationForm.FormD).Length == 2; }
        catch { return false; }
    }

    public static string? Normalize(string? raw)
    {
        var t = Text(raw);
        if (t is null) return null;

        // Se a normalização Unicode do runtime funciona, usa NFD + descarte de marcas; senão, tabela embutida.
        bool useNfd = NfdWorks;
        var decomposed = useNfd ? t.Normalize(NormalizationForm.FormD) : t;
        var sb = new StringBuilder(decomposed.Length);
        bool pendingSpace = false;
        foreach (var raw0 in decomposed)
        {
            var ch = raw0;
            if (useNfd)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            }
            else
            {
                if (AccentTable.IsCombiningMark(ch)) continue;
                ch = AccentTable.Strip(ch);
            }
            var up = char.ToUpperInvariant(ch);
            if (char.IsLetterOrDigit(up))
            {
                if (pendingSpace && sb.Length > 0) sb.Append(' ');
                pendingSpace = false;
                sb.Append(up);
            }
            else pendingSpace = true;
        }
        return sb.Length == 0 ? null : sb.ToString();
    }
}
