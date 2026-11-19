using System.Text;

namespace AnatelEtl;

/// <summary>
/// Parser de CSV em streaming: separador configurável, valores opcionalmente entre aspas,
/// aspas internas duplicadas, quebras de linha (CR, LF ou CRLF) permitidas dentro de aspas.
/// </summary>
public sealed class CsvReader
{
    private readonly TextReader _reader;
    private readonly char _sep;
    private readonly char[] _buf = new char[1 << 16];
    private int _pos, _len;
    private readonly StringBuilder _sb = new(256);

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

    /// <summary>Lê o próximo registro; retorna false no fim do arquivo.</summary>
    public bool ReadRecord(List<string> fields)
    {
        fields.Clear();
        if (Peek() < 0) return false;

        while (true)
        {
            _sb.Clear();
            int c = Peek();
            if (c == '"')
            {
                Next();
                while (true)
                {
                    c = Next();
                    if (c < 0) break; // aspas não fechadas no fim: aceita o que veio
                    if (c == '"')
                    {
                        if (Peek() == '"') { Next(); _sb.Append('"'); }
                        else break;
                    }
                    else _sb.Append((char)c);
                }
                // caracteres após a aspa de fechamento até o separador são anexados (tolerância)
                while (true)
                {
                    c = Peek();
                    if (c < 0 || c == _sep || c == '\r' || c == '\n') break;
                    _sb.Append((char)Next());
                }
            }
            else
            {
                while (true)
                {
                    c = Peek();
                    if (c < 0 || c == _sep || c == '\r' || c == '\n') break;
                    _sb.Append((char)Next());
                }
            }
            fields.Add(_sb.ToString());

            c = Next();
            if (c == _sep) continue;
            if (c == '\r') { if (Peek() == '\n') Next(); }
            return true; // fim de linha ou fim de arquivo
        }
    }
}
