using System.Text;

namespace Etl;

/// <summary>Leitor CSV em streaming: separador '|', aspas '"' com escape por duplicação.</summary>
public sealed class CsvReader : IDisposable
{
    private readonly TextReader _reader;
    private readonly char _sep;
    private readonly StringBuilder _sb = new();
    private int _peeked = -2;

    public CsvReader(TextReader reader, char separator = '|')
    {
        _reader = reader;
        _sep = separator;
    }

    private int Read()
    {
        if (_peeked != -2) { var c = _peeked; _peeked = -2; return c; }
        return _reader.Read();
    }

    private int Peek()
    {
        if (_peeked == -2) _peeked = _reader.Read();
        return _peeked;
    }

    /// <summary>Consome o fim de linha se o próximo caractere for um (LF, CR ou CRLF).</summary>
    private bool TryConsumeEol()
    {
        int c = Peek();
        if (c == '\n') { Read(); return true; }
        if (c == '\r') { Read(); if (Peek() == '\n') Read(); return true; }
        return false;
    }

    /// <summary>Lê o próximo registro em <paramref name="fields"/>. Retorna false no fim do arquivo.</summary>
    public bool ReadRecord(List<string> fields)
    {
        fields.Clear();
        while (TryConsumeEol()) { }          // ignora linhas em branco
        if (Peek() == -1) return false;

        while (true)
        {
            fields.Add(ReadField());
            if (Peek() == _sep) { Read(); continue; }   // há outro campo (possivelmente vazio)
            TryConsumeEol();                             // fim do registro (ou EOF)
            return true;
        }
    }

    // Lê um campo e para antes do separador ou do fim de linha/arquivo.
    private string ReadField()
    {
        _sb.Clear();
        int c = Peek();
        if (c == '"')
        {
            Read();
            while (true)
            {
                c = Read();
                if (c == -1) break;
                if (c == '"')
                {
                    if (Peek() == '"') { Read(); _sb.Append('"'); continue; }
                    break;
                }
                _sb.Append((char)c);
            }
        }
        // restante do campo (campo sem aspas, ou lixo após a aspa de fechamento)
        while (true)
        {
            c = Peek();
            if (c == -1 || c == _sep || c == '\n' || c == '\r') break;
            Read();
            _sb.Append((char)c);
        }
        return _sb.ToString();
    }

    public void Dispose() => _reader.Dispose();
}
