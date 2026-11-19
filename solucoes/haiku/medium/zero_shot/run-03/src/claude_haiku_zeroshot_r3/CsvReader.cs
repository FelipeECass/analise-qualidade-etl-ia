using System.Text;

namespace ClaudeHaikuZeroshotR3;

/// <summary>
/// Leitor CSV com delimitador '|', aspas duplas e aspas escapadas por duplicação ("").
/// Aceita terminadores CRLF e LF, e campos com quebras de linha dentro das aspas.
/// Linhas em branco são ignoradas.
/// </summary>
internal static class CsvReader
{
    public static IEnumerable<string[]> Read(TextReader reader, char delimiter = '|')
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        bool hasContent = false;

        while (true)
        {
            int ci = reader.Read();
            if (ci < 0)
            {
                if (hasContent)
                {
                    fields.Add(sb.ToString());
                    yield return fields.ToArray();
                }
                yield break;
            }

            char c = (char)ci;

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (reader.Peek() == '"')
                    {
                        reader.Read();
                        sb.Append('"');
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    sb.Append(c);
                }
                continue;
            }

            if (c == '"')
            {
                inQuotes = true;
                hasContent = true;
            }
            else if (c == delimiter)
            {
                fields.Add(sb.ToString());
                sb.Clear();
                hasContent = true;
            }
            else if (c == '\r' || c == '\n')
            {
                if (c == '\r' && reader.Peek() == '\n')
                {
                    reader.Read();
                }
                if (hasContent)
                {
                    fields.Add(sb.ToString());
                    yield return fields.ToArray();
                }
                fields.Clear();
                sb.Clear();
                hasContent = false;
            }
            else
            {
                sb.Append(c);
                hasContent = true;
            }
        }
    }
}
