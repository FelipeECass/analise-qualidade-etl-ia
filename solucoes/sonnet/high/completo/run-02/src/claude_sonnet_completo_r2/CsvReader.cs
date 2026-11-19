using System.Text;

namespace AnatelEtl;

/// <summary>
/// Leitor CSV em streaming para o formato da Anatel: separador '|', valores entre aspas duplas,
/// aspas internas duplicadas (""). Cada registro é decodificado em um único buffer de chars e os campos
/// são expostos como spans, evitando uma alocação de string por campo.
/// </summary>
public sealed class CsvReader : IDisposable
{
    private readonly TextReader _reader;
    private readonly char _sep;
    private readonly char[] _in = new char[1 << 16];
    private int _inPos, _inLen;
    private bool _eof;

    private char[] _rec = new char[4096];
    private int _recLen;
    private int[] _starts = new int[64];
    private int[] _lens = new int[64];

    public int FieldCount { get; private set; }

    public CsvReader(Stream stream, char separator = '|')
    {
        _reader = new StreamReader(stream, new UTF8Encoding(false), true, 1 << 16);
        _sep = separator;
    }

    public ReadOnlySpan<char> Field(int index) =>
        (uint)index < (uint)FieldCount ? _rec.AsSpan(_starts[index], _lens[index]) : ReadOnlySpan<char>.Empty;

    private int Peek()
    {
        if (_inPos >= _inLen)
        {
            if (_eof) return -1;
            _inLen = _reader.Read(_in, 0, _in.Length);
            _inPos = 0;
            if (_inLen <= 0) { _eof = true; _inLen = 0; return -1; }
        }
        return _in[_inPos];
    }

    private int Next()
    {
        int c = Peek();
        if (c >= 0) _inPos++;
        return c;
    }

    private void Append(char c)
    {
        if (_recLen == _rec.Length) Array.Resize(ref _rec, _rec.Length * 2);
        _rec[_recLen++] = c;
    }

    private void EndField(int start)
    {
        if (FieldCount == _starts.Length)
        {
            Array.Resize(ref _starts, FieldCount * 2);
            Array.Resize(ref _lens, FieldCount * 2);
        }
        _starts[FieldCount] = start;
        _lens[FieldCount] = _recLen - start;
        FieldCount++;
    }

    /// <summary>Lê o próximo registro. Linhas totalmente vazias são ignoradas.</summary>
    public bool Read()
    {
        while (true)
        {
            _recLen = 0;
            FieldCount = 0;
            int c = Peek();
            if (c < 0) return false;

            int start = 0;
            bool any = false;
            while (true)
            {
                c = Next();
                if (c < 0) { if (any) EndField(start); break; }
                any = true;
                if (c == '"' && _recLen == start)
                {
                    // campo entre aspas: lê até a aspa de fechamento (aspas duplicadas viram uma)
                    while (true)
                    {
                        c = Next();
                        if (c < 0) break;
                        if (c == '"')
                        {
                            if (Peek() == '"') { Next(); Append('"'); }
                            else break;
                        }
                        else Append((char)c);
                    }
                    continue; // texto após a aspa de fechamento (se houver) é anexado ao campo
                }
                if (c == _sep) { EndField(start); start = _recLen; continue; }
                if (c == '\n') { EndField(start); break; }
                if (c == '\r') { if (Peek() == '\n') Next(); EndField(start); break; }
                Append((char)c);
            }
            if (FieldCount == 1 && _lens[0] == 0) continue; // linha em branco
            return true;
        }
    }

    public void Dispose() => _reader.Dispose();
}
