using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Comparador;

public sealed record Identificacao(string Empresa, string Modelo, string Condicao, string Geracao, string Iteracao)
{
    /// <summary>Nome do projeto e do banco da solução, por exemplo claude_sonnet_zeroshot_r1.</summary>
    public string Solucao => $"{Empresa}_{Modelo}_{Condicao}_r{Geracao}";
    public string Rotulo => $"{Solucao}_i{Iteracao}";
}

public static class Saidas
{
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    const string CabecalhoCsv = "rotulo,empresa,modelo,condicao,geracao,iteracao,taxa,aceita,ausentes,excedentes,divergentes,avisos,categoria_falha";

    /// <summary>JSON e feedback vão para &lt;dir&gt;\&lt;solução&gt;; o resultados.csv consolidado fica em &lt;dir&gt;.</summary>
    public static void Gravar(Identificacao id, Resultado r, string dir)
    {
        var pasta = Path.Combine(dir, id.Solucao);
        Directory.CreateDirectory(pasta);
        var registro = new
        {
            id.Rotulo, id.Empresa, id.Modelo, id.Condicao, id.Geracao, id.Iteracao,
            DataHora = DateTimeOffset.Now,
            Resultado = r,
        };
        File.WriteAllText(Path.Combine(pasta, id.Rotulo + ".json"), JsonSerializer.Serialize(registro, Json));

        var feedback = Path.Combine(pasta, id.Rotulo + "_feedback.md");
        if (r.Aceita) File.Delete(feedback);
        else File.WriteAllText(feedback, Feedback(r));

        var csv = Path.Combine(dir, "resultados.csv");
        if (!File.Exists(csv)) File.WriteAllText(csv, CabecalhoCsv + "\n");
        string[] campos =
        [
            id.Rotulo, id.Empresa, id.Modelo, id.Condicao, id.Geracao, id.Iteracao,
            r.Taxa.ToString("R", CultureInfo.InvariantCulture), r.Aceita ? "sim" : "nao",
            $"{r.Ausentes}", $"{r.Excedentes}", $"{r.Divergentes}", $"{r.Avisos.Count}", r.CategoriaFalha ?? "",
        ];
        File.AppendAllText(csv, string.Join(",", campos) + "\n");
    }

    /// <summary>Preenche o modelo do docs/propmts/07_reprompt.md.</summary>
    public static string Feedback(Resultado r)
    {
        var sb = new StringBuilder("A solução não atingiu o critério de aceitação. Corrija-a com base no resultado da verificação abaixo.\n");

        if (r.Erros is not null)
            sb.Append("\nErros de compilação ou execução:\n\n").Append(r.Erros.Trim()).Append('\n');

        if (r.ErrosEsquema.Count > 0)
        {
            sb.Append("\nDiferenças entre o esquema do banco e o DDL.sql:\n\n");
            foreach (var e in r.ErrosEsquema) sb.Append("- ").Append(e).Append('\n');
        }

        var comDiferenca = r.Tabelas.Where(t => t.Ausentes > 0 || t.Excedentes > 0).ToList();
        if (comDiferenca.Count > 0)
        {
            sb.Append("\nContagem de linhas:\n");
            foreach (var t in comDiferenca)
            {
                sb.Append($"\nTabela {t.Tabela}: {t.Ausentes} linhas ausentes; {t.Excedentes} linhas excedentes ou duplicadas\n");
                Lista(sb, "Exemplos de linhas ausentes (chave):", t.ExemplosAusentes);
                Lista(sb, "Exemplos de linhas excedentes ou duplicadas (chave):", t.ExemplosExcedentes);
            }
        }

        var colunas = r.Tabelas.SelectMany(t => t.Colunas.Select(c => (t.Tabela, c))).ToList();
        if (colunas.Count > 0)
        {
            sb.Append("\nColunas que diferem do resultado esperado:\n");
            foreach (var (tabela, c) in colunas)
            {
                sb.Append($"\nTabela {tabela}, coluna {c.Coluna}: {c.Divergencias} divergências\nExemplos:\n");
                foreach (var e in c.Exemplos) sb.Append($"- linha {e.Chave}: esperado: {e.Esperado} | obtido: {e.Obtido}\n");
            }
        }

        if (r.Avisos.Count > 0)
        {
            sb.Append("\nAvisos sobre o esquema (não reprovam a solução, mas diferem do DDL.sql):\n\n");
            foreach (var a in r.Avisos) sb.Append("- ").Append(a).Append('\n');
        }
        return sb.ToString();
    }

    static void Lista(StringBuilder sb, string titulo, List<string> itens)
    {
        if (itens.Count == 0) return;
        sb.Append(titulo).Append('\n');
        foreach (var i in itens) sb.Append("- ").Append(i).Append('\n');
    }
}
