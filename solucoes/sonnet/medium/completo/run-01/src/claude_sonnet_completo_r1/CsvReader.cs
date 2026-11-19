using System.Text;

namespace Etl;

/// <summary>Leitor CSV streaming: separador '|', aspas '"' com duplicação como escape.</summary>
public sealed class CsvReader
{
    private readonly TextReader _r;
    private readonly char _sep;
    private readonly StringBuilder _sb = new();

    public CsvReader(TextReader r, char sep = '|') { _r = r; _sep = sep; }

    /// <summary>Lê a próxima linha lógica; preenche a lista de campos. Retorna false no fim.</summary>
    public bool Read(List<string> fields)
    {
        fields.Clear();
        int c = _r.Read();
        if (c < 0) return false;
        _sb.Clear();
        bool inQuotes = false;
        bool any = false;
        while (true)
        {
            if (c < 0)
            {
                fields.Add(_sb.ToString());
                return true;
            }
            char ch = (char)c;
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (_r.Peek() == '"') { _r.Read(); _sb.Append('"'); }
                    else inQuotes = false;
                }
                else _sb.Append(ch);
            }
            else if (ch == '"') { inQuotes = true; any = true; }
            else if (ch == _sep) { fields.Add(_sb.ToString()); _sb.Clear(); any = true; }
            else if (ch == '\r')
            {
                if (_r.Peek() == '\n') _r.Read();
                break;
            }
            else if (ch == '\n') break;
            else { _sb.Append(ch); any = true; }
            c = _r.Read();
        }
        if (!any && fields.Count == 0 && _sb.Length == 0)
        {
            fields.Clear(); // linha vazia
            return true;
        }
        fields.Add(_sb.ToString());
        return true;
    }
}
