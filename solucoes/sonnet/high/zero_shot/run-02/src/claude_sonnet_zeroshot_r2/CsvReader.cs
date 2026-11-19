using System.Text;

namespace Etl;

/// <summary>
/// Leitor CSV com separador '|', campos entre aspas duplas, aspas duplicadas como escape
/// e quebras de linha possíveis dentro de campos entre aspas.
/// </summary>
public sealed class CsvReader
{
    private readonly TextReader _reader;
    private readonly char _sep;
    private readonly StringBuilder _sb = new();

    public CsvReader(TextReader reader, char separator = '|')
    {
        _reader = reader;
        _sep = separator;
    }

    /// <summary>Lê um registro; retorna false ao fim do arquivo.</summary>
    public bool ReadRecord(List<string> fields)
    {
        fields.Clear();
        _sb.Clear();
        var c = _reader.Read();
        if (c < 0) return false;

        var inQuotes = false;
        var wasQuoted = false; // campo atual já teve aspas (para "" vazio)
        while (true)
        {
            if (c < 0)
            {
                fields.Add(_sb.ToString());
                return true;
            }
            var ch = (char)c;
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (_reader.Peek() == '"')
                    {
                        _reader.Read();
                        _sb.Append('"');
                    }
                    else inQuotes = false;
                }
                else _sb.Append(ch);
            }
            else if (ch == '"' && _sb.Length == 0 && !wasQuoted)
            {
                inQuotes = true;
                wasQuoted = true;
            }
            else if (ch == _sep)
            {
                fields.Add(_sb.ToString());
                _sb.Clear();
                wasQuoted = false;
            }
            else if (ch == '\r')
            {
                if (_reader.Peek() == '\n') _reader.Read();
                fields.Add(_sb.ToString());
                return true;
            }
            else if (ch == '\n')
            {
                fields.Add(_sb.ToString());
                return true;
            }
            else _sb.Append(ch);
            c = _reader.Read();
        }
    }
}
