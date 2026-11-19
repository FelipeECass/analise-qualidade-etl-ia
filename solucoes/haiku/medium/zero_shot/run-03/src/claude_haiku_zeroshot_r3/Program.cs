using Microsoft.Data.Sqlite;

namespace ClaudeHaikuZeroshotR3;

internal static class Program
{
    private const string RaizPadrao = @"D:\Projects\TCC";
    private const string NomeBanco = "claude_haiku_zeroshot_r3.db";

    private static int Main()
    {
        try
        {
            Executar();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Erro: {ex.Message}");
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static void Executar()
    {
        var raiz = ResolverRaiz();
        Console.WriteLine($"Raiz do TCC: {raiz}");

        var pastaZips = Path.Combine(raiz, "files");
        var arquivoDdl = Path.Combine(raiz, "src", "DDL.sql");
        var pastaDb = Path.Combine(raiz, "db");
        var caminhoDb = Path.Combine(pastaDb, NomeBanco);

        if (!Directory.Exists(pastaZips))
        {
            throw new DirectoryNotFoundException($"Pasta de entrada não encontrada: {pastaZips}");
        }
        if (!File.Exists(arquivoDdl))
        {
            throw new FileNotFoundException("DDL não encontrado.", arquivoDdl);
        }

        var zips = Directory.GetFiles(pastaZips, "*.zip")
            .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
            .ToArray();
        if (zips.Length == 0)
        {
            throw new FileNotFoundException($"Nenhum ZIP encontrado em {pastaZips}.");
        }

        Directory.CreateDirectory(pastaDb);
        ApagarBanco(caminhoDb);

        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = caminhoDb }.ConnectionString);
        conn.Open();

        using (var pragma = conn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA synchronous = OFF;";
            pragma.ExecuteNonQuery();
        }

        using (var schema = conn.CreateCommand())
        {
            schema.CommandText = File.ReadAllText(arquivoDdl);
            schema.ExecuteNonQuery();
        }

        using var carga = new Carga(conn);
        foreach (var zip in zips)
        {
            carga.CarregarZip(zip);
            Console.WriteLine($"Processado: {Path.GetFileName(zip)}");
        }

        Console.WriteLine($"Linhas lidas: {carga.LinhasLidas}");
        Console.WriteLine($"Linhas rejeitadas: {carga.LinhasRejeitadas}");
        Console.WriteLine($"Estações gravadas: {carga.EstacoesGravadas}");
        Console.WriteLine($"Emissões gravadas: {carga.EmissoesGravadas}");
        Console.WriteLine($"Emissões duplicadas (não carregadas): {carga.EmissoesDuplicadas}");
    }

    /// <summary>
    /// Ordem: TCC_BASE_DIR (se definida); D:\Projects\TCC (se existir); depois a pasta-pai mais
    /// próxima, a partir do executável e do diretório atual, que contenha files\ e src\DDL.sql.
    /// Sem nenhuma correspondência, usa D:\Projects\TCC e a falha de "pasta não encontrada" ocorre depois.
    /// </summary>
    private static string ResolverRaiz()
    {
        static bool ehRaiz(string dir)
            => Directory.Exists(Path.Combine(dir, "files"))
               && File.Exists(Path.Combine(dir, "src", "DDL.sql"));

        var env = Environment.GetEnvironmentVariable("TCC_BASE_DIR");
        if (!string.IsNullOrWhiteSpace(env)) return env;

        if (Directory.Exists(RaizPadrao) && ehRaiz(RaizPadrao)) return RaizPadrao;

        foreach (var inicio in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var dir = new DirectoryInfo(inicio); dir is not null; dir = dir.Parent)
            {
                if (ehRaiz(dir.FullName)) return dir.FullName;
            }
        }

        return RaizPadrao;
    }

    private static void ApagarBanco(string caminhoDb)
    {
        foreach (var sufixo in new[] { "", "-journal", "-wal", "-shm" })
        {
            var arquivo = caminhoDb + sufixo;
            if (File.Exists(arquivo))
            {
                File.Delete(arquivo);
            }
        }
    }
}
