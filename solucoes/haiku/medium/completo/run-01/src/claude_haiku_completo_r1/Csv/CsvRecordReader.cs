using System.Text;

namespace Etl.Csv;

/// <summary>
/// Leitor em streaming de CSV com separador '|' e campos entre aspas duplas.
/// Trata "" como aspas escapadas, quebras de linha dentro de campo e CRLF/LF.
/// </summary>
public sealed class CsvRecordReader : IDisposable
{
    private const int Eof = -1;
    private const int NoPushback = -2;

    private readonly TextReader _reader;
    private int _pushback = NoPushback;

    public CsvRecordReader(TextReader reader) => _reader = reader;

    private int Next()
    {
        if (_pushback != NoPushback)
        {
            var c = _pushback;
            _pushback = NoPushback;
            return c;
        }
        return _reader.Read();
    }

    /// <summary>Próximo registro (campos brutos, sem trim), ou null no fim do arquivo.</summary>
    public string[]? ReadRecord()
    {
        var fields = new List<string>(48);
        var sb = new StringBuilder(64);
        var inQuotes = false;
        var quotedField = false;
        var anyChar = false;

        while (true)
        {
            var c = Next();
            if (c == Eof)
            {
                if (!anyChar && fields.Count == 0) return null;
                fields.Add(sb.ToString());
                return fields.ToArray();
            }

            anyChar = true;

            if (inQuotes)
            {
                if (c == '"')
                {
                    var n = Next();
                    if (n == '"')
                    {
                        sb.Append('"');
                    }
                    else
                    {
                        inQuotes = false;
                        _pushback = n;
                    }
                }
                else
                {
                    sb.Append((char)c);
                }
                continue;
            }

            switch (c)
            {
                case '"' when sb.Length == 0 && !quotedField:
                    inQuotes = true;
                    quotedField = true;
                    break;
                case '|':
                    fields.Add(sb.ToString());
                    sb.Clear();
                    quotedField = false;
                    break;
                case '\r':
                    break;
                case '\n':
                    fields.Add(sb.ToString());
                    return fields.ToArray();
                default:
                    sb.Append((char)c);
                    break;
            }
        }
    }

    public void Dispose() => _reader.Dispose();
}
