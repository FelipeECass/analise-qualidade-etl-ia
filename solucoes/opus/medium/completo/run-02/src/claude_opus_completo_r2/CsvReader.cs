using System.Text;

namespace AnatelEtl;

/// <summary>Parser CSV em streaming: separador configurável, campos entre aspas, aspas duplicadas, CRLF/LF.</summary>
public sealed class CsvReader
{
    private readonly TextReader _reader;
    private readonly char _sep;
    private readonly char[] _buf = new char[1 << 16];
    private int _pos, _len;
    private readonly StringBuilder _field = new();
    private readonly List<string> _fields = new(64);

    public CsvReader(TextReader reader, char separator)
    {
        _reader = reader;
        _sep = separator;
    }

    private int Peek()
    {
        if (_pos >= _len)
        {
            _len = _reader.Read(_buf, 0, _buf.Length);
            _pos = 0;
            if (_len <= 0) { _len = 0; return -1; }
        }
        return _buf[_pos];
    }

    private int Next()
    {
        int c = Peek();
        if (c >= 0) _pos++;
        return c;
    }

    /// <summary>Lê o próximo registro; null no fim do arquivo. Linhas totalmente vazias são ignoradas.</summary>
    public string[]? ReadRecord()
    {
        while (true)
        {
            int c = Peek();
            if (c < 0) return null;
            if (c == '\r' || c == '\n') { Next(); continue; }
            break;
        }

        _fields.Clear();
        _field.Clear();
        bool inQuotes = false;
        while (true)
        {
            int c = Next();
            if (c < 0)
            {
                _fields.Add(_field.ToString());
                return _fields.ToArray();
            }
            char ch = (char)c;
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (Peek() == '"') { Next(); _field.Append('"'); }
                    else inQuotes = false;
                }
                else _field.Append(ch);
            }
            else if (ch == '"') inQuotes = true;
            else if (ch == _sep) { _fields.Add(_field.ToString()); _field.Clear(); }
            else if (ch == '\r' || ch == '\n')
            {
                if (ch == '\r' && Peek() == '\n') Next();
                _fields.Add(_field.ToString());
                return _fields.ToArray();
            }
            else _field.Append(ch);
        }
    }
}
