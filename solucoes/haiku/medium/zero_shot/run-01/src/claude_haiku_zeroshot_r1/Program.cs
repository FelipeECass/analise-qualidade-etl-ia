namespace ClaudeHaikuZeroshotR1;

internal static class Program
{
    private const string NomeBase = "claude_haiku_zeroshot_r1";

    private static int Main()
    {
        try
        {
            var raiz = EncontrarRaiz();
            var filesDir = Path.Combine(raiz, "files");
            var ddlPath = Path.Combine(raiz, "src", "DDL.sql");
            var dbPath = Path.Combine(raiz, "db", NomeBase + ".db");

            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
            ApagarBanco(dbPath);

            Etl.Run(filesDir, ddlPath, dbPath);
            Console.WriteLine($"Banco gerado: {dbPath}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERRO: {ex.Message}");
            return 1;
        }
    }

    /// <summary>Sobe a partir do diretório atual (e do diretório do binário) até achar src/DDL.sql.</summary>
    private static string EncontrarRaiz()
    {
        foreach (var inicio in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(inicio); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "src", "DDL.sql")))
                    return dir.FullName;
            }
        }

        foreach (var candidato in new[] { @"D:\Projects\TCC", "/workspace" })
        {
            if (File.Exists(Path.Combine(candidato, "src", "DDL.sql")))
                return candidato;
        }

        throw new FileNotFoundException("Não encontrei a raiz do projeto (pasta com src/DDL.sql e files/).");
    }

    private static void ApagarBanco(string dbPath)
    {
        foreach (var arq in new[] { dbPath, dbPath + "-wal", dbPath + "-shm", dbPath + "-journal" })
        {
            if (File.Exists(arq)) File.Delete(arq);
        }
    }
}
