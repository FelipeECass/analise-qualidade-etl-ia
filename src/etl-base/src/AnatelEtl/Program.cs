using AnatelEtl;
using AnatelEtl.Load;
using Microsoft.Data.Sqlite;

const string DefaultDatabase = "anatel.db";

if (args.Length < 2)
{
    Console.Error.WriteLine("Uso: AnatelEtl <arquivo.zip> <UF> [banco.db]");
    Console.Error.WriteLine("Ex.: AnatelEtl D:\\Projects\\TCC\\SC.zip SC anatel.db");
    return 1;
}

var zipPath = args[0];
var uf = args[1].ToUpperInvariant();
var databasePath = args.Length > 2 ? args[2] : DefaultDatabase;

using var connection = new SqliteConnection($"Data Source={databasePath}");
connection.Open();

var stopwatch = System.Diagnostics.Stopwatch.StartNew();
var report = new EtlPipeline(connection, TimeProvider.System).Run(zipPath);
stopwatch.Stop();

Console.WriteLine($"ETL de {uf} concluído em {stopwatch.Elapsed.TotalSeconds:F1}s -> {databasePath}");
foreach (var (key, value) in report.Counters)
{
    Console.WriteLine($"  {key,-40} {value,10:N0}");
}

var queries = new AnalyticsQueries(connection);
PrintGroup("Emissões por geração", queries.EmissoesPorGeracao(uf));
PrintGroup("Emissões por operadora", queries.EmissoesPorOperadora(uf));
PrintGroup("Emissões por faixa de frequência", queries.EmissoesPorFaixa(uf));
return 0;

static void PrintGroup(string title, IEnumerable<GroupCount> rows)
{
    Console.WriteLine();
    Console.WriteLine(title);
    foreach (var row in rows)
    {
        Console.WriteLine($"  {row.Grupo,-20} {row.Total,10:N0}");
    }
}
