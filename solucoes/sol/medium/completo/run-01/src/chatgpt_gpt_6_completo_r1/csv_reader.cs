using System.Text;

namespace AnatelEtl;

/// <summary>Streaming parser: quoted pipes, escaped quotes, CRLF/LF and multiline values.</summary>
internal sealed class CsvReader(Stream stream) : IDisposable
{
    private readonly StreamReader reader = new(stream, new UTF8Encoding(false, true), true, 65536);
    private readonly char[] buffer = new char[65536];
    private int position;
    private int length;
    public long RecordNumber { get; private set; }

    private int Peek()
    {
        if (position < length) return buffer[position];
        length = reader.Read(buffer, 0, buffer.Length);
        position = 0;
        return length == 0 ? -1 : buffer[0];
    }

    private int Read()
    {
        int c = Peek();
        if (c >= 0) position++;
        return c;
    }

    public string?[]? ReadRecord()
    {
        if (Peek() < 0) return null;
        var fields = new List<string?>(41);
        var field = new StringBuilder(80);
        bool quoted = false, closed = false, started = false;
        while (true)
        {
            int c = Read();
            if (quoted)
            {
                if (c < 0) throw new InvalidDataException($"CSV: aspas não encerradas no registro {RecordNumber + 1}.");
                if (c == '"')
                {
                    if (Peek() == '"') { Read(); field.Append('"'); }
                    else { quoted = false; closed = true; }
                }
                else field.Append((char)c);
                continue;
            }
            if (c is -1 or '|' or '\r' or '\n')
            {
                string value = field.ToString().Trim();
                fields.Add(value.Length == 0 ? null : value);
                field.Clear();
                closed = started = false;
                if (c == '|') continue;
                if (c == '\r' && Peek() == '\n') Read();
                RecordNumber++;
                return fields.ToArray();
            }
            if (closed)
            {
                if (!char.IsWhiteSpace((char)c))
                    throw new InvalidDataException($"CSV: caractere após aspas no registro {RecordNumber + 1}.");
                continue;
            }
            if (c == '"')
            {
                if (started) throw new InvalidDataException($"CSV: aspas inesperadas no registro {RecordNumber + 1}.");
                quoted = started = true;
            }
            else { field.Append((char)c); started = true; }
        }
    }

    public void Dispose() => reader.Dispose();
}

internal sealed class SourceRow
{
    internal static readonly string[] Required =
    [
        "NumFistel", "NumEstacao", "NumAto", "SiglaUf", "CodMunicipio", "Municipio.NomeMunicipio",
        "NomeEntidade", "EnderecoEstacao", "Latitude", "Longitude", "Tecnologia", "DesignacaoEmissao",
        "FreqTxMHz", "FreqRxMHz", "Azimute", "PotenciaTransmissorWatts", "GanhoAntena", "AlturaAntena",
        "AnguloElevacao", "AnguloMeiaPotenciaAntena", "FrenteCostaAntena", "Polarizacao",
        "CodTipoClasseEstacao", "CodTipoAntena", "CodEquipamentoAntena", "CodEquipamentoTransmissor",
        "Status.state", "DataLicenciamento", "DataPrimeiroLicenciamento", "DataValidade"
    ];
    private readonly Dictionary<string, int> columns;
    private readonly int width;
    private string?[] values = [];

    public SourceRow(string?[] header)
    {
        width = header.Length;
        columns = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < header.Length; i++)
            if (header[i] is not { } name || !columns.TryAdd(name, i))
                throw new InvalidDataException("Cabeçalho vazio ou duplicado.");
        foreach (string name in Required)
            if (!columns.ContainsKey(name)) throw new InvalidDataException($"Coluna obrigatória ausente: {name}.");
    }

    public void Set(string?[] row)
    {
        if (row.Length != width) throw new InvalidDataException($"Registro com {row.Length} campos; esperado: {width}.");
        values = row;
    }
    public string? this[string name] => values[columns[name]];
}
