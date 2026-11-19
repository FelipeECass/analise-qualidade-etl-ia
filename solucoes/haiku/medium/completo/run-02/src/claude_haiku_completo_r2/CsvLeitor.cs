using System.Text;

namespace ClaudeHaikuCompletoR2;

/// <summary>
/// Leitor de CSV com delimitador '|' e aspas duplas (regra da especificação: "" dentro de campo
/// é uma aspa; valores sempre entre aspas). Lê linha a linha, sem carregar o arquivo inteiro.
/// Campos com quebra de linha dentro de aspas são suportados.
/// </summary>
internal sealed class CsvLeitor : IDisposable
{
    private const char Delimitador = '|';
    private const char Aspas = '"';

    private readonly TextReader _leitor;
    private int _numeroLinha;

    public CsvLeitor(TextReader leitor) => _leitor = leitor;

    public int NumeroLinha => _numeroLinha;

    /// <summary>Próximo registro, ou null no fim do arquivo. Linhas totalmente vazias são ignoradas.</summary>
    public string[]? ProximoRegistro()
    {
        string? linha;
        do
        {
            linha = _leitor.ReadLine();
            if (linha is null) return null;
            _numeroLinha++;
        } while (linha.Length == 0);

        var campos = new List<string>(41);
        var atual = new StringBuilder();
        var dentroDeAspas = false;
        var i = 0;

        while (true)
        {
            if (i >= linha.Length)
            {
                if (!dentroDeAspas)
                {
                    campos.Add(atual.ToString());
                    break;
                }

                // Campo com aspas continua na linha seguinte.
                var proxima = _leitor.ReadLine() ?? throw new FormatException(
                    $"CSV: aspas não fechadas até o fim do arquivo (linha {_numeroLinha}).");
                _numeroLinha++;
                atual.Append('\n');
                linha = proxima;
                i = 0;
                continue;
            }

            var c = linha[i];
            if (dentroDeAspas)
            {
                if (c == Aspas)
                {
                    if (i + 1 < linha.Length && linha[i + 1] == Aspas)
                    {
                        atual.Append(Aspas);
                        i += 2;
                    }
                    else
                    {
                        dentroDeAspas = false;
                        i++;
                    }
                }
                else
                {
                    atual.Append(c);
                    i++;
                }
            }
            else if (c == Aspas)
            {
                dentroDeAspas = true;
                i++;
            }
            else if (c == Delimitador)
            {
                campos.Add(atual.ToString());
                atual.Clear();
                i++;
            }
            else
            {
                atual.Append(c);
                i++;
            }
        }

        return campos.ToArray();
    }

    public void Dispose() => _leitor.Dispose();
}
