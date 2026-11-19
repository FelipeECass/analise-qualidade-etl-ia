using System.Diagnostics;
using AnatelEtl;

try
{
    var sw = Stopwatch.StartNew();
    string root = PathResolver.FindRoot();
    string filesDir = Path.Combine(root, "files");
    string ddl = Path.Combine(root, "src", "DDL.sql");
    string db = Path.Combine(root, "db", "claude_sonnet_completo_r2.db");

    var zips = Directory.GetFiles(filesDir, "*.zip").OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToList();
    var only = Environment.GetEnvironmentVariable("ETL_ONLY"); // depuração: lista de UFs separadas por vírgula
    if (!string.IsNullOrWhiteSpace(only))
    {
        var set = only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        zips = zips.Where(z => set.Contains(Path.GetFileNameWithoutExtension(z))).ToList();
    }
    var dbOverride = Environment.GetEnvironmentVariable("ETL_DB");
    if (!string.IsNullOrWhiteSpace(dbOverride)) db = dbOverride;
    if (zips.Count == 0) throw new FileNotFoundException($"Nenhum ZIP encontrado em {filesDir}");

    using var loader = new Loader(db, ddl);
    foreach (var zip in zips)
    {
        var t = sw.Elapsed;
        loader.ProcessZip(zip);
        Console.WriteLine($"{Path.GetFileName(zip)} ok ({(sw.Elapsed - t).TotalSeconds:F1}s) lidas={loader.Stats.Read} estacoes={loader.Stats.Stations} emissoes={loader.Stats.Emissions}");
    }
    loader.Commit();
    var s = loader.Stats;
    Console.WriteLine($"Concluído em {sw.Elapsed}: lidas={s.Read} rejeitadas={s.Rejected} emissoes_duplicadas={s.DuplicateEmissions} estacoes={s.Stations} emissoes={s.Emissions}");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"ERRO: {ex}");
    return 1;
}
