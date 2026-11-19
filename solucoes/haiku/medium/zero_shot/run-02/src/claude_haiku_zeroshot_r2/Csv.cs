using System.Text;

namespace ClaudeHaikuZeroshotR2;

/// <summary>
/// Leitor de CSV com separador '|', valores entre aspas duplas, aspas escapadas como "" e
/// quebras de linha dentro de aspas. Não faz nenhuma limpeza de conteúdo.
/// </summary>
internal static class Csv
{
    public static IEnumerable<string[]> Registros(TextReader leitor)
    {
        var campos = new List<string>();
        var campo = new StringBuilder();
        var emAspas = false;
        var linhaIniciada = false;

        int lido;
        while ((lido = leitor.Read()) != -1)
        {
            var c = (char)lido;

            if (emAspas)
            {
                if (c == '"')
                {
                    if (leitor.Peek() == '"')
                    {
                        leitor.Read();
                        campo.Append('"');
                    }
                    else
                    {
                        emAspas = false;
                    }
                }
                else
                {
                    campo.Append(c);
                }
                continue;
            }

            switch (c)
            {
                case '"':
                    emAspas = true;
                    linhaIniciada = true;
                    break;
                case '|':
                    campos.Add(campo.ToString());
                    campo.Clear();
                    linhaIniciada = true;
                    break;
                case '\r':
                    break;
                case '\n':
                    if (linhaIniciada)
                    {
                        campos.Add(campo.ToString());
                        yield return campos.ToArray();
                        campos.Clear();
                        campo.Clear();
                        linhaIniciada = false;
                    }
                    break;
                default:
                    campo.Append(c);
                    linhaIniciada = true;
                    break;
            }
        }

        if (linhaIniciada)
        {
            campos.Add(campo.ToString());
            yield return campos.ToArray();
        }
    }
}
