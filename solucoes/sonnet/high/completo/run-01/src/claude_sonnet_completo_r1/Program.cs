using AnatelEtl;

namespace AnatelEtl;

public static class Program
{
    public static int Main()
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var root = PathResolver.FindRoot();
            var filesDir = Path.Combine(root, "files");
            var ddl = Path.Combine(root, "src", "DDL.sql");
            var dbPath = Path.Combine(root, "db", "claude_sonnet_completo_r1.db");

            var zips = Directory.GetFiles(filesDir, "*.zip")
                .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (zips.Count == 0) throw new FileNotFoundException($"Nenhum ZIP encontrado em {filesDir}");

            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
            foreach (var suffix in new[] { "", "-journal", "-wal", "-shm" })
            {
                var p = dbPath + suffix;
                if (File.Exists(p)) File.Delete(p);
            }

            Console.WriteLine($"Raiz: {root}");
            using var loader = new SqliteLoader(dbPath, ddl);
            var etl = new EtlProcessor(loader);
            etl.Run(zips);

            var s = etl.Stats;
            Console.WriteLine($"Linhas lidas={s.LinhasLidas:N0} rejeitadas={s.Rejeitadas:N0} duplicadas={s.Duplicadas:N0} " +
                              $"estacoes={s.Estacoes:N0} emissoes={s.Emissoes:N0}");
            Console.WriteLine($"Concluído em {sw.Elapsed}. Banco: {dbPath}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERRO: " + ex);
            return 1;
        }
    }
}

/// <summary>Localiza a raiz do projeto (pasta com files/ e src/DDL.sql): TCC_ROOT, D:\Projects\TCC ou ancestrais.</summary>
public static class PathResolver
{
    private static bool IsRoot(string? p) =>
        !string.IsNullOrEmpty(p) && Directory.Exists(Path.Combine(p, "files")) && File.Exists(Path.Combine(p, "src", "DDL.sql"));

    public static string FindRoot()
    {
        var env = Environment.GetEnvironmentVariable("TCC_ROOT");
        if (IsRoot(env)) return Path.GetFullPath(env!);

        const string windowsRoot = @"D:\Projects\TCC";
        if (IsRoot(windowsRoot)) return windowsRoot;

        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var dir = new DirectoryInfo(start);
            while (dir != null)
            {
                if (IsRoot(dir.FullName)) return dir.FullName;
                dir = dir.Parent;
            }
        }
        throw new DirectoryNotFoundException(@"Raiz do projeto não encontrada (esperado D:\Projects\TCC com files\ e src\DDL.sql).");
    }
}
