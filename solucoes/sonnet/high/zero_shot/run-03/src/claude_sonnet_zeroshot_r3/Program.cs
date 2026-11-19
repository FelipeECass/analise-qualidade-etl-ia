using AnatelEtl;

const string Nome = "claude_sonnet_zeroshot_r3";

try
{
    var root = ResolveRoot();
    var etl = new Etl(
        Path.Combine(root, "files"),
        Path.Combine(root, "src", "DDL.sql"),
        Path.Combine(root, "db", Nome + ".db"));

    var sw = System.Diagnostics.Stopwatch.StartNew();
    etl.Run();
    Console.WriteLine($"Tempo total: {sw.Elapsed}");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"ERRO: {ex}");
    return 1;
}

// Raiz do trabalho: D:\Projects\TCC; se não existir (outro ambiente), procura a pasta
// ancestral que contenha "files" e "src/DDL.sql".
static string ResolveRoot()
{
    const string fixedRoot = @"D:\Projects\TCC";
    if (Directory.Exists(fixedRoot)) return fixedRoot;

    foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
    {
        var dir = new DirectoryInfo(start);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "files")) &&
                File.Exists(Path.Combine(dir.FullName, "src", "DDL.sql")))
                return dir.FullName;
            dir = dir.Parent;
        }
    }
    throw new DirectoryNotFoundException(@"Não foi possível localizar a raiz D:\Projects\TCC.");
}
