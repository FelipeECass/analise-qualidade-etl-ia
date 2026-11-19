using System.Globalization;
using Comparador;

const string Uso = """
    Uso:
      comparador ciclo    --empresa E --modelo M --condicao C --geracao G --iteracao I [opções]
      comparador comparar --empresa E --modelo M --condicao C --geracao G --iteracao I [--erros arquivo] [opções]

    A solução é <empresa>_<modelo>_<condicao>_r<geracao>, por exemplo claude_sonnet_zeroshot_r1
    (--empresa claude --modelo sonnet --condicao zeroshot --geracao 1).

    ciclo:    apaga o banco antigo, compila e executa <workspace>\src\<solução> e compara o banco gerado.
    comparar: só compara <workspace>\db\<solução>.db (ou --solucao). Com --erros, registra a falha sem ler o banco.

    Opções (padrões entre parênteses):
      --workspace (D:\Projects\TCC)   --gabarito (D:\Projects\TCC_GitHub\db\base.db)
      --ddl (D:\Projects\TCC_GitHub\db\DDL.sql)   --saida (D:\Projects\TCC_GitHub\comparator\resultados)
      --solucao (<workspace>\db\<solução>.db)

    Código de saída: 0 = aceita, 1 = não aceita, 2 = uso incorreto.
    """;

Console.OutputEncoding = System.Text.Encoding.UTF8;

if (args.Length == 0 || args[0] is not ("ciclo" or "comparar"))
{
    Console.Error.WriteLine(Uso);
    return 2;
}

var o = new Dictionary<string, string>();
for (int i = 1; i < args.Length; i += 2)
{
    if (!args[i].StartsWith("--") || i + 1 >= args.Length)
    {
        Console.Error.WriteLine($"Argumento inválido: {args[i]}\n\n{Uso}");
        return 2;
    }
    o[args[i][2..]] = args[i + 1];
}

string[] obrigatorios = ["empresa", "modelo", "condicao", "geracao", "iteracao"];
var faltando = obrigatorios.Where(k => !o.ContainsKey(k)).ToList();
if (faltando.Count > 0)
{
    Console.Error.WriteLine($"Falta: {string.Join(", ", faltando.Select(k => "--" + k))}\n\n{Uso}");
    return 2;
}

string Opcao(string k, string padrao) => o.GetValueOrDefault(k) ?? padrao;
var id = new Identificacao(o["empresa"], o["modelo"], o["condicao"], o["geracao"], o["iteracao"]);
var workspace = Opcao("workspace", @"D:\Projects\TCC");
var gabarito = Opcao("gabarito", @"D:\Projects\TCC_GitHub\db\base.db");
var ddl = Opcao("ddl", @"D:\Projects\TCC_GitHub\db\DDL.sql");
var saida = Opcao("saida", @"D:\Projects\TCC_GitHub\comparator\resultados");
var banco = Opcao("solucao", Path.Combine(workspace, "db", id.Solucao + ".db"));

string? erros = null, detalhe = null;
if (args[0] == "ciclo")
{
    Console.WriteLine($"Compilando e executando {id.Solucao}...");
    (erros, detalhe) = Ciclo.Executar(Path.Combine(workspace, "src", id.Solucao), banco, Path.Combine(saida, id.Solucao, id.Rotulo + "_execucao.log"));
}
else if (o.TryGetValue("erros", out var arquivoErros))
{
    (erros, detalhe) = (File.ReadAllText(arquivoErros), "informado em --erros");
}

Console.WriteLine($"Comparando {banco} com {gabarito}...");
var r = Comparacao.Comparar(ddl, gabarito, banco, saida, erros, detalhe);
Saidas.Gravar(id, r, saida);

// Sem arredondar: com 8,4 milhões de linhas, poucas divergências somem em "P2" (100,00%).
var taxa = (r.Taxa * 100).ToString("0.##########", CultureInfo.GetCultureInfo("pt-BR"));
Console.WriteLine($"{id.Rotulo}: taxa {taxa}% = {r.Acertos}/{r.LinhasGabarito + r.Excedentes} ({r.Ausentes} ausentes; " +
                  $"{r.Excedentes} excedentes; {r.Divergentes} divergentes) -> {(r.Aceita ? "ACEITA" : "NÃO ACEITA: " + r.CategoriaFalha)}");
foreach (var a in r.Avisos) Console.WriteLine($"Aviso: {a}");
Console.WriteLine($"Resultados em {Path.Combine(saida, id.Solucao)}; resumo em {Path.Combine(saida, "resultados.csv")}");
return r.Aceita ? 0 : 1;
