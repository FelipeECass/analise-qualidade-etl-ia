using System.Globalization;
using System.IO.Hashing;
using System.Runtime.InteropServices;
using System.Text;

namespace AnatelEtl;

/// <summary>Detecta emissões duplicadas pela chave da seção 6 (NULL == NULL), guardando hashes de 128 bits.</summary>
public sealed class Deduplicator
{
    private readonly HashSet<UInt128> _seen = new(1 << 20);
    private readonly StringBuilder _sb = new(256);

    public int Count => _seen.Count;

    /// <summary>True se a emissão é inédita (e a registra); false se for duplicada.</summary>
    public bool Add(Emissao e)
    {
        _sb.Clear();
        Append(e.Uf); Append(e.Fistel); Append(e.NumEstacao); Append(e.NumAto);
        Append(e.Azimute); Append(e.FreqTxMhz); Append(e.FreqRxMhz);
        Append(e.DesignacaoEmissao); Append(e.Tecnologia);
        return _seen.Add(HashKey(_sb));
    }

    public static string BuildKey(Emissao e)
    {
        var d = new Deduplicator();
        d.Add(e);
        return d._sb.ToString();
    }

    private static UInt128 HashKey(StringBuilder sb)
    {
        Span<char> buf = sb.Length <= 1024 ? stackalloc char[sb.Length] : new char[sb.Length];
        sb.CopyTo(0, buf, sb.Length);
        return XxHash128.HashToUInt128(MemoryMarshal.AsBytes((ReadOnlySpan<char>)buf));
    }

    // \u0001 = separador de campo; \u0002 = marcador de NULL.
    private void Append(string? s)
    {
        if (s == null) _sb.Append('\u0002'); else _sb.Append('\u0003').Append(s);
        _sb.Append('\u0001');
    }

    private void Append(double? v)
    {
        if (v == null) _sb.Append('\u0002');
        else _sb.Append('\u0003').Append((v.Value == 0 ? 0.0 : v.Value).ToString("R", CultureInfo.InvariantCulture));
        _sb.Append('\u0001');
    }
}
