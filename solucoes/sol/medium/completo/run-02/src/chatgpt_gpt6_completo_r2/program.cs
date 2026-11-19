namespace AnatelEtl;

internal static class Program
{
    internal const string OutputName = "chatgpt_gpt6_completo_r2";

    public static int Main()
    {
        try
        {
            string root = ResolveRoot();
            var pipeline = new EtlPipeline(root);
            pipeline.Run();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Falha no ETL: {ex.Message}");
            return 1;
        }
    }

    private static string ResolveRoot()
    {
        if (OperatingSystem.IsWindows())
            return @"D:\Projects\TCC";

        // The deployed project is inside <root>/src/<project>/bin/<config>/net10.0.
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "DDL.sql")) &&
                Directory.Exists(Path.Combine(directory.FullName, "files")))
                return directory.FullName;
        }
        throw new DirectoryNotFoundException("Não foi possível localizar a raiz com files/ e src/DDL.sql.");
    }
}
