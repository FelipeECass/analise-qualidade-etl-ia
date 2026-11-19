using Etl;

namespace Etl;

public static class Program
{
    private const string DbName = "claude_sonnet_completo_r1.db";

    // Nome do banco na especificação: {{empresa}}_{{modelo}}_completo_{{geracao}}.db
    private static string? FindRoot()
    {
        const string win = @"D:\Projects\TCC";
        if (OperatingSystem.IsWindows() && Directory.Exists(win)) return win;
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var d = new DirectoryInfo(start);
            while (d != null)
            {
                if (Directory.Exists(Path.Combine(d.FullName, "files")) && File.Exists(Path.Combine(d.FullName, "src", "DDL.sql")))
                    return d.FullName;
                d = d.Parent;
            }
        }
        return null;
    }

    public static int Main()
    {
        try
        {
            var root = FindRoot() ?? throw new DirectoryNotFoundException("Pasta raiz do projeto (com files e src/DDL.sql) não encontrada.");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            new Loader().Run(Path.Combine(root, "files"), Path.Combine(root, "src", "DDL.sql"), Path.Combine(root, "db", DbName));
            Console.WriteLine($"Concluído em {sw.Elapsed}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERRO: " + ex);
            return 1;
        }
    }
}
