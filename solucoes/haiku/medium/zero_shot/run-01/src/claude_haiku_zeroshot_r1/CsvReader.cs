using System.Text;

namespace ClaudeHaikuZeroshotR1;

/// <summary>
/// Leitor CSV para o formato da Anatel: separador '|', valores entre aspas duplas,
/// aspas internas duplicadas (""). Aceita quebras de linha dentro de valores entre aspas.
/// Não usa Peek(): StreamReader sobre entrada de ZIP pode devolver -1 falsamente.
/// </summary>
internal static class CsvReader
{
    public static IEnumerable<string[]> Read(TextReader reader)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        bool hasContent = false;
        int? pushback = null;

        int Next()
        {
            if (pushback is int p)
            {
                pushback = null;
                return p;
            }
            return reader.Read();
        }

        int c;
        while ((c = Next()) != -1)
        {
            char ch = (char)c;

            if (inQuotes)
            {
                if (ch == '"')
                {
                    int n = Next();
                    if (n == '"')
                    {
                        sb.Append('"');
                    }
                    else
                    {
                        inQuotes = false;
                        pushback = n;
                    }
                }
                else
                {
                    sb.Append(ch);
                }
                continue;
            }

            switch (ch)
            {
                case '"':
                    inQuotes = true;
                    hasContent = true;
                    break;
                case '|':
                    fields.Add(sb.ToString());
                    sb.Clear();
                    hasContent = true;
                    break;
                case '\r':
                    break;
                case '\n':
                    if (hasContent || sb.Length > 0)
                    {
                        fields.Add(sb.ToString());
                        yield return fields.ToArray();
                    }
                    fields.Clear();
                    sb.Clear();
                    hasContent = false;
                    break;
                default:
                    sb.Append(ch);
                    hasContent = true;
                    break;
            }
        }

        if (hasContent || sb.Length > 0)
        {
            fields.Add(sb.ToString());
            yield return fields.ToArray();
        }
    }
}
