using System.Diagnostics;

static string FindRoot()
{
    foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        var d = new DirectoryInfo(start);
        while (d != null)
        {
            if (File.Exists(Path.Combine(d.FullName, "src", "DDL.sql")) && Directory.Exists(Path.Combine(d.FullName, "files")))
                return d.FullName;
            d = d.Parent;
        }
    }
    return @"D:\Projects\TCC";
}

try
{
    var sw = Stopwatch.StartNew();
    var root = FindRoot();
    var dbPath = Path.Combine(root, "db", "claude_sonnet_completo_r3.db");
    using var db = new Database(dbPath, Path.Combine(root, "src", "DDL.sql"));
    var etl = new EtlPipeline(Path.Combine(root, "files"), db);
    etl.Run();
    Console.WriteLine($"Concluído em {sw.Elapsed}: lidas={etl.RowsRead} rejeitadas={etl.RowsRejected} estacoes={etl.Stations} emissoes={etl.Emissions} duplicadas={etl.Duplicates}");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.ToString());
    return 1;
}
