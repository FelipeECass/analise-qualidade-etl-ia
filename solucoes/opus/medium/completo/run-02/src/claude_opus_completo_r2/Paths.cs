namespace AnatelEtl;

/// <summary>Resolve a raiz do espaço de trabalho (D:\Projects\TCC ou equivalente).</summary>
public static class Paths
{
    public const string DbName = "claude_opus_completo_r2.db";
    private const string WindowsRoot = @"D:\Projects\TCC";

    public static string ResolveRoot()
    {
        if (OperatingSystem.IsWindows() && IsRoot(WindowsRoot)) return WindowsRoot;

        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(start);
            while (dir != null)
            {
                if (IsRoot(dir.FullName)) return dir.FullName;
                dir = dir.Parent;
            }
        }
        throw new DirectoryNotFoundException(
            $"Raiz do espaço de trabalho não encontrada ({WindowsRoot} ou diretório ancestral com 'files' e 'src/DDL.sql').");
    }

    private static bool IsRoot(string path) =>
        Directory.Exists(Path.Combine(path, "files")) && File.Exists(Path.Combine(path, "src", "DDL.sql"));
}
