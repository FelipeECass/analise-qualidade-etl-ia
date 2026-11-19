using System.Security.Cryptography;
using System.Text;

namespace Etl;

/// <summary>Controla estações já vistas e emissões duplicadas (seção 6).</summary>
public sealed class Deduplicator
{
    private readonly HashSet<string> _stations = new(StringComparer.Ordinal);
    private readonly HashSet<(ulong, ulong)> _emissions = new();

    public int StationCount => _stations.Count;

    /// <summary>True se é a primeira vez que a estação (uf, fistel, num_estacao) aparece.</summary>
    public bool IsNewStation(Station s)
        => _stations.Add(string.Concat(s.Uf, "\u0001", s.Fistel, "\u0001", s.NumEstacao));

    /// <summary>True se a emissão ainda não foi vista (mesma chave de duplicidade da especificação).</summary>
    public bool IsNewEmission(Emission e)
    {
        var sb = new StringBuilder(128);
        sb.Append(e.Uf).Append('\u0001').Append(e.Fistel).Append('\u0001').Append(e.NumEstacao).Append('\u0001');
        AppendText(sb, e.NumAto);
        AppendNum(sb, e.Azimute);
        AppendNum(sb, e.FreqTxMhz);
        AppendNum(sb, e.FreqRxMhz);
        AppendText(sb, e.DesignacaoEmissao);
        AppendText(sb, e.Tecnologia);

        Span<byte> hash = stackalloc byte[16];
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        MD5.HashData(bytes, hash);
        var key = (BitConverter.ToUInt64(hash[..8]), BitConverter.ToUInt64(hash[8..]));
        return _emissions.Add(key);
    }

    // null e texto vazio nunca coincidem: texto vem prefixado por 'T', null por 'N'.
    private static void AppendText(StringBuilder sb, string? v)
    {
        if (v == null) sb.Append('N'); else sb.Append('T').Append(v);
        sb.Append('\u0001');
    }

    private static void AppendNum(StringBuilder sb, double? v)
    {
        if (!v.HasValue) sb.Append('N');
        else sb.Append('D').Append(BitConverter.DoubleToInt64Bits(v.Value == 0 ? 0.0 : v.Value));
        sb.Append('\u0001');
    }
}
