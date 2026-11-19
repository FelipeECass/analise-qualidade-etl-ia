using System.Text;

namespace AnatelEtl;

/// <summary>
/// Leitor CSV em streaming: separador '|', campos opcionalmente entre aspas duplas,
/// aspas internas duplicadas, quebras de linha permitidas dentro de campos entre aspas.
/// </summary>
public sealed class CsvReader
{
    private readonly TextReader _reader;
    private readonly char _sep;
    private readonly char[] _buf = new char[1 << 16];
    private int _pos;
    private int _len;
    private bool _eof;
    private readonly StringBuilder _sb = new(256);

    public CsvReader(TextReader reader, char separator = '|')
    {
        _reader = reader;
        _sep = separator;
    }

    private int Peek()
    {
        if (_pos >= _len && !Fill()) return -1;
        return _buf[_pos];
    }

    private int Read()
    {
        if (_pos >= _len && !Fill()) return -1;
        return _buf[_pos++];
    }

    private bool Fill()
    {
        if (_eof) return false;
        _len = _reader.Read(_buf, 0, _buf.Length);
        _pos = 0;
        if (_len <= 0)
        {
            _eof = true;
            _len = 0;
            return false;
        }
        return true;
    }

    /// <summary>Lê o próximo registro em <paramref name="fields"/>. Retorna false no fim do arquivo.</summary>
    public bool ReadRecord(List<string> fields)
    {
        fields.Clear();
        while (true)
        {
            int c = Peek();
            if (c < 0) return false;
            // ignora linhas totalmente vazias
            if (c == '\r' || c == '\n')
            {
                Read();
                continue;
            }
            break;
        }

        while (true)
        {
            fields.Add(ReadField(out bool endOfRecord));
            if (endOfRecord) return true;
        }
    }

    private string ReadField(out bool endOfRecord)
    {
        _sb.Clear();
        endOfRecord = false;
        int c = Peek();

        if (c == '"')
        {
            Read();
            while (true)
            {
                c = Read();
                if (c < 0) { endOfRecord = true; break; }
                if (c == '"')
                {
                    if (Peek() == '"')
                    {
                        Read();
                        _sb.Append('"');
                    }
                    else
                    {
                        break; // fecha aspas
                    }
                }
                else
                {
                    _sb.Append((char)c);
                }
            }
            if (endOfRecord) return _sb.ToString();

            // consome lixo eventual entre o fechamento das aspas e o delimitador
            while (true)
            {
                c = Peek();
                if (c < 0) { endOfRecord = true; break; }
                if (c == _sep) { Read(); break; }
                if (c == '\r' || c == '\n') { ConsumeNewline(); endOfRecord = true; break; }
                Read();
                _sb.Append((char)c);
            }
            return _sb.ToString();
        }

        while (true)
        {
            c = Peek();
            if (c < 0) { endOfRecord = true; break; }
            if (c == _sep) { Read(); break; }
            if (c == '\r' || c == '\n') { ConsumeNewline(); endOfRecord = true; break; }
            Read();
            _sb.Append((char)c);
        }
        return _sb.ToString();
    }

    private void ConsumeNewline()
    {
        int c = Read();
        if (c == '\r' && Peek() == '\n') Read();
    }
}
