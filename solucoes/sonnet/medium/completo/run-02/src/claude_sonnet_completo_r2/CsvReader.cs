using System.Text;

namespace Etl;

/// <summary>Leitor de registros CSV (separador '|', aspas duplas, "" como escape, quebras dentro de aspas).</summary>
public sealed class CsvReader
{
    private readonly TextReader _r;
    private readonly char _sep;

    public CsvReader(TextReader r, char sep = '|') { _r = r; _sep = sep; }

    /// <summary>Lê o próximo registro; null no fim do arquivo.</summary>
    public List<string>? ReadRecord()
    {
        int c = _r.Read();
        if (c < 0) return null;
        var fields = new List<string>(48);
        var sb = new StringBuilder();
        bool inQuotes = false;
        bool any = false;
        while (true)
        {
            if (c < 0) break;
            char ch = (char)c;
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (_r.Peek() == '"') { _r.Read(); sb.Append('"'); }
                    else inQuotes = false;
                }
                else sb.Append(ch);
            }
            else if (ch == '"') { inQuotes = true; any = true; }
            else if (ch == _sep) { fields.Add(sb.ToString()); sb.Clear(); any = true; }
            else if (ch == '\r') { if (_r.Peek() == '\n') _r.Read(); break; }
            else if (ch == '\n') break;
            else { sb.Append(ch); any = true; }
            c = _r.Read();
        }
        if (!any && sb.Length == 0 && fields.Count == 0) return new List<string>(); // linha vazia
        fields.Add(sb.ToString());
        return fields;
    }
}
