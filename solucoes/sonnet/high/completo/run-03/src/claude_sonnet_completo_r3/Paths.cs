namespace Etl;

/// <summary>Resolve as pastas de entrada/saída (D:\Projects\TCC no Windows).</summary>
public sealed class Paths
{
    public const string Name = "claude_sonnet_completo_r3";

    public string Root { get; }
    public string FilesDir => Path.Combine(Root, "files");
    public string DdlFile => Path.Combine(Root, "src", "DDL.sql");
    public string DbFile => Path.Combine(Root, "db", Name + ".db");

    private Paths(string root) => Root = root;

    public static Paths Resolve()
    {
        if (OperatingSystem.IsWindows() && Directory.Exists(@"D:\Projects\TCC"))
            return new Paths(@"D:\Projects\TCC");

        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var dir = new DirectoryInfo(start);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "files")) &&
                    File.Exists(Path.Combine(dir.FullName, "src", "DDL.sql")))
                    return new Paths(dir.FullName);
                dir = dir.Parent;
            }
        }
        throw new DirectoryNotFoundException(
            "Não foi possível localizar a raiz do projeto (pasta com 'files' e 'src/DDL.sql').");
    }
}
