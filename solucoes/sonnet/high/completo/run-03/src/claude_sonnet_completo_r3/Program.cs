using System.Diagnostics;
using Etl;

try
{
    var sw = Stopwatch.StartNew();
    var paths = Paths.Resolve();
    Console.WriteLine($"Raiz: {paths.Root}");
    Console.WriteLine($"Saída: {paths.DbFile}");

    using var writer = new SqliteWriter(paths.DbFile, paths.DdlFile);
    var c = EtlRunner.Run(paths, writer, Console.WriteLine);
    writer.Commit();

    Console.WriteLine(
        $"Concluído em {sw.Elapsed:hh\\:mm\\:ss}: lidas={c.Read:N0} rejeitadas={c.Rejected:N0} " +
        $"estacao={c.Stations:N0} emissao={c.Emissions:N0} emissoes duplicadas={c.DuplicateEmissions:N0}");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"ERRO: {ex}");
    return 1;
}
