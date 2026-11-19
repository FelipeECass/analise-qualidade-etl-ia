using System.Text;

/// <summary>Leitor de CSV com separador '|' e campos entre aspas; devolve os campos já sem aspas.</summary>
sealed class CsvReader
{
    readonly TextReader _r;
    readonly char _sep;
    public CsvReader(TextReader r, char sep = '|') { _r = r; _sep = sep; }

    public bool ReadRecord(List<string> fields)
    {
        fields.Clear();
        int c = _r.Read();
        if (c < 0) return false;
        var sb = new StringBuilder();
        bool inQuotes = false;
        while (true)
        {
            if (c < 0) { fields.Add(sb.ToString()); return true; }
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
            else if (ch == '"') inQuotes = true;
            else if (ch == _sep) { fields.Add(sb.ToString()); sb.Clear(); }
            else if (ch == '\n') { fields.Add(sb.ToString()); return true; }
            else if (ch == '\r') { if (_r.Peek() == '\n') _r.Read(); fields.Add(sb.ToString()); return true; }
            else sb.Append(ch);
            c = _r.Read();
        }
    }
}
