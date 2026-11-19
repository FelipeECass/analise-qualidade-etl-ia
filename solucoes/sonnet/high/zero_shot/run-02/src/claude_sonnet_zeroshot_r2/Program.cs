using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Etl;

public static class Program
{
    private const string ProjectName = "claude_sonnet_zeroshot_r2";

    public static int Main()
    {
        try
        {
            Run();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Erro: {ex}");
            return 1;
        }
    }

    /// <summary>Raiz do trabalho: D:\Projects\TCC; se não existir, sobe a partir do exe até achar files/ e src/DDL.sql.</summary>
    private static string ResolveRoot()
    {
        const string fixedRoot = @"D:\Projects\TCC";
        if (Directory.Exists(fixedRoot)) return fixedRoot;
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "src", "DDL.sql")) && Directory.Exists(Path.Combine(dir.FullName, "files")))
                    return dir.FullName;
                dir = dir.Parent;
            }
        }
        throw new DirectoryNotFoundException($"Pasta de trabalho não encontrada: {fixedRoot}");
    }

    private static void Run()
    {
        var root = ResolveRoot();
        var filesDir = Path.Combine(root, "files");
        var ddlPath = Path.Combine(root, "src", "DDL.sql");
        var dbDir = Path.Combine(root, "db");
        var dbPath = Path.Combine(dbDir, ProjectName + ".db");

        var zips = Directory.GetFiles(filesDir, "*.zip").OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase).ToList();
        if (zips.Count == 0) throw new FileNotFoundException($"Nenhum ZIP encontrado em {filesDir}");

        Directory.CreateDirectory(dbDir);
        foreach (var suffix in new[] { "", "-journal", "-wal", "-shm" })
        {
            var p = dbPath + suffix;
            if (File.Exists(p)) File.Delete(p);
        }

        var ddl = File.ReadAllText(ddlPath, Encoding.UTF8);

        var cs = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString();
        using var conn = new SqliteConnection(cs);
        conn.Open();
        using (var pragma = conn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode = OFF; PRAGMA synchronous = OFF; PRAGMA temp_store = MEMORY;";
            pragma.ExecuteNonQuery();
        }
        using (var create = conn.CreateCommand())
        {
            create.CommandText = ddl;
            create.ExecuteNonQuery();
        }

        using var tx = conn.BeginTransaction();
        var loader = new Loader(conn, tx);
        foreach (var zip in zips)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var before = loader.Stats;
            loader.LoadZip(zip);
            var after = loader.Stats;
            Console.WriteLine($"{Path.GetFileName(zip)}: linhas={after.Read - before.Read} rejeitadas={after.Rejected - before.Rejected} " +
                              $"estacoes={after.Stations - before.Stations} emissoes={after.Emissions - before.Emissions} ({sw.Elapsed.TotalSeconds:F1}s)");
        }
        loader.Dispose();
        tx.Commit();

        var s = loader.Stats;
        Console.WriteLine($"Total: linhas={s.Read} rejeitadas={s.Rejected} estacoes={s.Stations} emissoes={s.Emissions} duplicadas={s.Duplicates}");
    }
}
