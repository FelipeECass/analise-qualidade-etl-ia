using System.Text;

namespace Etl;

/// <summary>
/// Leitor de registros delimitados por '|' com campos entre aspas duplas ("" = aspas literais).
/// Suporta separador e quebra de linha dentro de aspas (registro multi-linha).
/// </summary>
public sealed class CsvReader
{
    private readonly TextReader _reader;
    private readonly char _sep;
    private readonly StringBuilder _sb = new();
    private readonly List<string> _fields = new(64);

    public CsvReader(TextReader reader, char sep = '|')
    {
        _reader = reader;
        _sep = sep;
    }

    /// <summary>Lê o próximo registro; null no fim do arquivo. Linhas totalmente vazias são ignoradas.</summary>
    public string[]? Read()
    {
        string? line;
        do
        {
            line = _reader.ReadLine();
            if (line == null) return null;
        } while (line.Length == 0);

        _fields.Clear();
        _sb.Clear();
        bool inQuotes = false;
        bool wasQuoted = false;
        int i = 0;
        while (true)
        {
            if (i >= line.Length)
            {
                if (inQuotes)
                {
                    // quebra de linha dentro de aspas: continua no próximo registro físico
                    var next = _reader.ReadLine();
                    if (next == null) break; // aspas não fechadas no EOF: aceita o que houver
                    _sb.Append('\n');
                    line = next;
                    i = 0;
                    continue;
                }
                break;
            }
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { _sb.Append('"'); i += 2; continue; }
                    inQuotes = false; i++; continue;
                }
                _sb.Append(c); i++;
            }
            else
            {
                if (c == _sep)
                {
                    _fields.Add(_sb.ToString()); _sb.Clear(); wasQuoted = false; i++; continue;
                }
                if (c == '"' && (_sb.Length == 0 && !wasQuoted))
                {
                    inQuotes = true; wasQuoted = true; i++; continue;
                }
                _sb.Append(c); i++;
            }
        }
        _fields.Add(_sb.ToString());
        return _fields.ToArray();
    }
}
