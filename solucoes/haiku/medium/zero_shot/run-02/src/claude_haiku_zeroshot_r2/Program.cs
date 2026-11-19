namespace ClaudeHaikuZeroshotR2;

internal static class Program
{
    private static int Main()
    {
        try
        {
            var raiz = EncontrarRaizTcc();
            var resumo = new Etl(raiz).Executar();
            Console.WriteLine(resumo);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Erro: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Sobe a partir da pasta do binário até achar a raiz do TCC (pasta com 'files' e 'src/DDL.sql').
    /// Funciona tanto em D:\Projects\TCC quanto em outro local com a mesma estrutura.
    /// </summary>
    private static string EncontrarRaizTcc()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "files"))
                && File.Exists(Path.Combine(dir.FullName, "src", "DDL.sql")))
            {
                return dir.FullName;
            }
        }
        throw new DirectoryNotFoundException(
            $"Raiz do TCC (pasta com 'files' e 'src/DDL.sql') não encontrada a partir de {AppContext.BaseDirectory}.");
    }
}
