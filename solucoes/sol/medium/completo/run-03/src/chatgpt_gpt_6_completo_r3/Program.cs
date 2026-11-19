namespace AnatelEtl;

internal static class Program
{
    internal const string Name = "chatgpt_gpt_6_completo_r3";
    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "src", "DDL.sql")) && Directory.Exists(Path.Combine(dir.FullName, "files")))
                return dir.FullName;
        const string windowsRoot = @"D:\Projects\TCC";
        if (OperatingSystem.IsWindows() && File.Exists(Path.Combine(windowsRoot, "src", "DDL.sql"))) return windowsRoot;
        throw new DirectoryNotFoundException("Raiz TCC não encontrada (src/DDL.sql e files).");
    }
    private static int Main()
    {
        string? output = null;
        try
        {
            var root = Root();
            Directory.CreateDirectory(Path.Combine(root, "db"));
            output = Path.Combine(root, "db", Name + ".db");
            if (File.Exists(output)) throw new IOException($"Banco já existe: {output}. Remova-o antes de executar.");
            new Pipeline().Run(root, output);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Falha no ETL: {ex.Message}");
            // Only the pipeline owns a newly created output; never remove an existing database.
            if (output is not null && Pipeline.CreatedOutput)
                foreach (var suffix in new[] { "", "-journal", "-wal", "-shm" })
                    try { File.Delete(output + suffix); } catch (IOException) { }
            return 1;
        }
    }
}
