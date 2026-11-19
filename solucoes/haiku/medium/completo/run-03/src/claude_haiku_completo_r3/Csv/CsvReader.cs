using System.Text;

namespace claude_haiku_completo_r3.Csv;

/// <summary>
/// Leitor de registros delimitados por '|', com valores entre aspas duplas.
/// Aspas dentro de valor são escritas como "" e lidas como ".
/// Suporta quebra de linha dentro de valor entre aspas (não ocorre nos dados, mas é tratado).
/// Aceita terminador CRLF ou LF.
/// </summary>
public sealed class CsvReader : IDisposable
{
    private readonly TextReader _leitor;
    private readonly char _separador;

    public CsvReader(TextReader leitor, char separador = '|')
    {
        _leitor = leitor;
        _separador = separador;
    }

    /// <summary>Próximo registro, ou null ao chegar ao fim do arquivo.</summary>
    public string[]? LerRegistro()
    {
        var campos = new List<string>();
        var atual = new StringBuilder();
        var dentroDeAspas = false;
        var leuAlgo = false;

        while (true)
        {
            var c = _leitor.Read();
            if (c < 0)
            {
                if (!leuAlgo) return null;
                campos.Add(atual.ToString());
                return campos.ToArray();
            }

            leuAlgo = true;
            var ch = (char)c;

            if (dentroDeAspas)
            {
                if (ch == '"')
                {
                    if (_leitor.Peek() == '"')
                    {
                        _leitor.Read();
                        atual.Append('"');
                    }
                    else
                    {
                        dentroDeAspas = false;
                    }
                }
                else
                {
                    atual.Append(ch);
                }
                continue;
            }

            if (ch == '"')
            {
                dentroDeAspas = true;
            }
            else if (ch == _separador)
            {
                campos.Add(atual.ToString());
                atual.Clear();
            }
            else if (ch == '\r' || ch == '\n')
            {
                if (ch == '\r' && _leitor.Peek() == '\n') _leitor.Read();
                campos.Add(atual.ToString());
                return campos.ToArray();
            }
            else
            {
                atual.Append(ch);
            }
        }
    }

    public void Dispose() => _leitor.Dispose();
}
