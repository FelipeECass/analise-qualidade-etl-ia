using System.Globalization;
using System.Text;

namespace AnatelEtl;

/// <summary>Regras de limpeza geral (seção 1 da especificação).</summary>
public static class Cleaner
{
    /// <summary>Regras 1.1/1.2: trim; vazio → null.</summary>
    public static string? Text(ReadOnlySpan<char> raw)
    {
        var t = raw.Trim();
        return t.IsEmpty ? null : new string(t);
    }

    public static string? Upper(ReadOnlySpan<char> raw)
    {
        var t = raw.Trim();
        return t.IsEmpty ? null : new string(t).ToUpperInvariant();
    }

    /// <summary>
    /// Regra 1.3: '.' e ',' são sempre separador decimal. Só aceita [+-]? (d+[.d*] | .d+) ([eE][+-]?d+)? ;
    /// qualquer outra coisa (NA, NaN, infinito, lixo) ou resultado não finito → null. -0 vira 0.
    /// </summary>
    public static double? Number(ReadOnlySpan<char> raw)
    {
        var t = raw.Trim();
        if (t.IsEmpty || t.Length > 64) return null;

        Span<char> buf = stackalloc char[t.Length];
        int i = 0, n = t.Length;
        for (int k = 0; k < n; k++) buf[k] = t[k] == ',' ? '.' : t[k];

        if (buf[i] == '+' || buf[i] == '-') i++;
        int digits = 0;
        while (i < n && buf[i] >= '0' && buf[i] <= '9') { i++; digits++; }
        if (i < n && buf[i] == '.')
        {
            i++;
            while (i < n && buf[i] >= '0' && buf[i] <= '9') { i++; digits++; }
        }
        if (digits == 0) return null;
        if (i < n && (buf[i] == 'e' || buf[i] == 'E'))
        {
            i++;
            if (i < n && (buf[i] == '+' || buf[i] == '-')) i++;
            int ed = 0;
            while (i < n && buf[i] >= '0' && buf[i] <= '9') { i++; ed++; }
            if (ed == 0) return null;
        }
        if (i != n) return null;

        if (!double.TryParse(buf, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return null;
        if (!double.IsFinite(v)) return null;
        return v == 0 ? 0.0 : v;
    }

    /// <summary>Regra 1.4: formato estrito yyyy-MM-dd e data de calendário válida; senão null.</summary>
    public static DateOnly? Date(ReadOnlySpan<char> raw)
    {
        var t = raw.Trim();
        if (t.Length != 10 || t[4] != '-' || t[7] != '-') return null;
        int y = 0, m = 0, d = 0;
        for (int k = 0; k < 10; k++)
        {
            if (k == 4 || k == 7) continue;
            char c = t[k];
            if (c < '0' || c > '9') return null;
            int v = c - '0';
            if (k < 4) y = y * 10 + v;
            else if (k < 7) m = m * 10 + v;
            else d = d * 10 + v;
        }
        if (y < 1 || m < 1 || m > 12 || d < 1 || d > DateTime.DaysInMonth(y, m)) return null;
        return new DateOnly(y, m, d);
    }

    /// <summary>
    /// Regra 1.6: remove acentos (NFD + descarte de marcas), maiúsculas, não letra/dígito → espaço,
    /// colapsa espaços e apara. Resultado vazio → null.
    /// </summary>
    public static string? Normalize(ReadOnlySpan<char> raw)
    {
        var t = raw.Trim();
        if (t.IsEmpty) return null;
        string decomposed = new string(t).Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        bool pendingSpace = false;
        foreach (char ch in decomposed)
        {
            var cat = char.GetUnicodeCategory(ch);
            if (cat == UnicodeCategory.NonSpacingMark) continue;
            char u = char.ToUpperInvariant(ch);
            if (char.IsLetterOrDigit(u))
            {
                if (pendingSpace && sb.Length > 0) sb.Append(' ');
                pendingSpace = false;
                sb.Append(u);
            }
            else pendingSpace = true;
        }
        return sb.Length == 0 ? null : sb.ToString();
    }
}
