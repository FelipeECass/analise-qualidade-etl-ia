using System.Text;

namespace AnatelEtl;

/// <summary>
/// Leitor CSV em streaming: separador configurável, aspas duplas com escape por duplicação,
/// quebras de linha CRLF/LF/CR (também dentro de valores entre aspas).
/// </summary>
public sealed class PipeCsvReader
{
    private readonly TextReader _reader;
    private readonly char _sep;
    private readonly char[] _buf = new char[1 << 16];
    private int _pos;
    private int _len;
    private readonly StringBuilder _sb = new();

    public PipeCsvReader(TextReader reader, char separator = '|')
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

    /// <summary>Lê o próximo registro em <paramref name="fields"/>. Retorna false no fim do arquivo.</summary>
    public bool ReadRecord(List<string> fields)
    {
        fields.Clear();
        if (Peek() < 0) return false;

        while (true)
        {
            // um campo
            _sb.Clear();
            int c = Peek();
            if (c == '"')
            {
                Next();
                while (true)
                {
                    c = Next();
                    if (c < 0) break;
                    if (c == '"')
                    {
                        if (Peek() == '"') { Next(); _sb.Append('"'); }
                        else break; // fecha aspas
                    }
                    else _sb.Append((char)c);
                }
                // texto residual entre aspas de fechamento e o separador (não esperado): anexa
                while ((c = Peek()) >= 0 && c != _sep && c != '\r' && c != '\n')
                {
                    _sb.Append((char)c);
                    Next();
                }
            }
            else
            {
                while ((c = Peek()) >= 0 && c != _sep && c != '\r' && c != '\n')
                {
                    _sb.Append((char)c);
                    Next();
                }
            }
            fields.Add(_sb.ToString());

            c = Peek();
            if (c == _sep) { Next(); continue; }
            if (c == '\r')
            {
                Next();
                if (Peek() == '\n') Next();
                return true;
            }
            if (c == '\n') { Next(); return true; }
            return true; // EOF
        }
    }
}
